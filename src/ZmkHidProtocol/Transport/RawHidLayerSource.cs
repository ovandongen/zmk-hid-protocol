using System.Diagnostics;
using HidApi;
using ZmkHidProtocol.Diagnostics;
using ZmkHidProtocol.Protocol;
using HidDeviceInfo = HidApi.DeviceInfo;

namespace ZmkHidProtocol.Transport;

/// <summary>
/// Cross-platform raw-HID layer source built on HidApi.Net. Discovers any
/// HID device exposing usage page 0xFF60 / usage 0x61, opens it, and parses
/// 32-byte reports off a background thread. Replaces the previous per-OS
/// transports (HidSharp on Windows USB, WinRT GATT on Windows BLE, IOKit on
/// macOS, /dev/hidraw on Linux) with a single implementation; hidapi handles
/// the platform differences — including HoGP-paired BLE keyboards on Windows,
/// which surface as ordinary HID devices.
/// </summary>
public sealed class RawHidLayerSource : ILayerSource, ICommandSink
{
    private const int ReconnectDelayMs = 2000;
    private const int ReadTimeoutMs = 250;
    // Liveness-probe budget per candidate endpoint when more than one FF60/61
    // device matches (USB + BLE up at once, or a stale duplicate BLE bond).
    private const int ProbeTimeoutMs = 600;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private Device? _device;
    private string _sourceName = "Raw HID";
    private volatile bool _connected;
    private volatile int _currentLayer;
    private readonly ManualResetEventSlim _rescan = new(false);
    private volatile IDeviceMatcher? _matcher;
    private string? _lastEnumerationKey;
    private bool _dumpedFirstScan;
    private readonly object _writeLock = new();

    public RawHidLayerSource(IDeviceMatcher? matcher = null)
    {
        _matcher = matcher;
    }

    public event Action<int>? LayerChanged;
    public event Action<int, bool>? KeyPositionEvent;
    public event Action<ReadOnlyMemory<byte>>? ReportReceived;
    public event Action? ConnectionChanged;

    public bool IsConnected => _connected;
    public int CurrentLayer => _currentLayer;
    public string SourceName => _sourceName;

    /// <summary>
    /// hidapi path of the currently open device, or null when disconnected.
    /// Lets the capability bus exclude the keyboard's own handle as a defensive
    /// secondary to matcher-based exclusion (the identical-VID/PID/name case the
    /// matcher cannot split). Set on connect, cleared on disconnect.
    /// </summary>
    public string? OpenDevicePath { get; private set; }

    public void SetMatcher(IDeviceMatcher? matcher)
    {
        _matcher = matcher;
        try { _device?.Dispose(); } catch { }
        _rescan.Set();
    }

    public void Start()
    {
        if (_runTask is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _runTask = Task.Run(() => RunLoop(ct));
        LibLog.Info("RawHid", "RawHidLayerSource started");
    }

    public void Stop()
    {
        if (_runTask is null) return;
        try { _cts?.Cancel(); } catch { }
        try { _device?.Dispose(); } catch { }
        _device = null;
        _rescan.Set();
        try { _runTask.Wait(2000); } catch { }
        _runTask = null;
        _cts?.Dispose();
        _cts = null;
        SetConnected(false);
    }

    public void Dispose()
    {
        Stop();
        _rescan.Dispose();
    }

    public ValueTask SendReportAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = _device;
        if (device is null) throw new InvalidOperationException("RawHidLayerSource: device is not open.");
        // hidapi expects a report-ID byte at index 0. The firmware uses
        // report ID 0 (no report ID), so prepend a leading 0x00.
        var prefixed = new byte[report.Length + 1];
        report.Span.CopyTo(prefixed.AsSpan(1));
        lock (_writeLock)
        {
            device.Write(prefixed);
        }
        return ValueTask.CompletedTask;
    }

    private void RunLoop(CancellationToken ct)
    {
        // hidapi may or may not prefix incoming reads with a report-ID byte
        // depending on backend; size for the larger case and strip the prefix
        // on dispatch if present.
        var buffer = new byte[HidConstants.ReportSize + 1];

        while (!ct.IsCancellationRequested)
        {
            var candidates = FindCandidates();
            if (candidates.Count == 0)
            {
                _rescan.Reset();
                try { _rescan.Wait(ReconnectDelayMs, ct); } catch (OperationCanceledException) { return; }
                continue;
            }

            HidDeviceInfo? info = SelectLiveDevice(candidates, buffer, ct, out Device? device);
            if (ct.IsCancellationRequested) { try { device?.Dispose(); } catch { } return; }
            if (info is null || device is null)
            {
                _rescan.Reset();
                try { _rescan.Wait(ReconnectDelayMs, ct); } catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                _device = device;
                OpenDevicePath = info.Path;

                _sourceName = string.IsNullOrWhiteSpace(info.ProductString)
                    ? "Raw HID"
                    : $"Raw HID ({info.ProductString})";
                LibLog.Info("RawHid", $"Connected: {_sourceName} (path={info.Path})");
                SetConnected(true);
                _lastEnumerationKey = null;

                while (!ct.IsCancellationRequested)
                {
                    int n;
                    try { n = device.ReadTimeout(buffer, ReadTimeoutMs); }
                    catch (Exception ex)
                    {
                        LibLog.Debug("RawHid", $"Read failed: {ex.Message}");
                        break;
                    }
                    if (n == 0) continue;
                    if (n < 0) break;

                    int offset = (n == HidConstants.ReportSize + 1) ? 1 : 0;
                    DispatchReport(buffer.AsSpan(offset, n - offset));
                }
            }
            catch (Exception ex)
            {
                LibLog.Warn("RawHid", $"Open/read error: {ex.Message}");
            }
            finally
            {
                try { device?.Dispose(); } catch { }
                _device = null;
                OpenDevicePath = null;
                if (_connected)
                {
                    SetConnected(false);
                    LibLog.Info("RawHid", "Disconnected");
                }
            }

            _rescan.Reset();
            try { _rescan.Wait(ReconnectDelayMs, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private void DispatchReport(ReadOnlySpan<byte> payload)
    {
        var copy = payload.ToArray();
        ReportReceived?.Invoke(copy);

        var layer = RawHidProtocol.TryParseLayerState(payload);
        if (layer is int l)
        {
            _currentLayer = l;
            LayerChanged?.Invoke(l);
            return;
        }
        var key = RawHidProtocol.TryParseKeyEvent(payload);
        if (key is { } k) KeyPositionEvent?.Invoke(k.Position, k.Pressed);
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        ConnectionChanged?.Invoke();
    }

    /// <summary>
    /// Returns every FF60/61 endpoint that also passes the active matcher.
    /// More than one is normal when USB and BLE are connected at once, or when
    /// a stale duplicate BLE bond lingers — the caller probes to pick the live
    /// one rather than trusting enumeration order.
    /// </summary>
    private List<HidDeviceInfo> FindCandidates()
    {
        List<HidDeviceInfo> devices;
        // Serialize against the capability bus's enumerate loop — concurrent
        // hidapi enumeration aborts the process on macOS. Materialize inside the
        // lock so the native enumeration completes before the gate is released.
        try { lock (HidGlobalLock.Gate) devices = Hid.Enumerate().ToList(); }
        catch (Exception ex)
        {
            LibLog.Debug("RawHid", $"Hid.Enumerate failed: {ex.Message}");
            return new List<HidDeviceInfo>();
        }

        var matcher = _matcher;
        var matcherMatches = new List<HidDeviceInfo>();
        var allDevices = new List<HidDeviceInfo>();
        var endpoints = new List<HidDeviceInfo>();
        foreach (var d in devices)
        {
            allDevices.Add(d);
            if (matcher is not null && !matcher.Matches(d.VendorId, d.ProductId, d.ProductString))
                continue;
            matcherMatches.Add(d);
            if (d.UsagePage == HidConstants.UsagePage && d.Usage == HidConstants.UsageId)
                endpoints.Add(d);
        }

        if (endpoints.Count > 1)
        {
            LibLog.Info("RawHid",
                $"Discovery: {endpoints.Count} FF60/61 endpoints matched — probing for the live one.");
            foreach (var d in endpoints)
                LibLog.Info("RawHid",
                    $"  candidate: name=\"{d.ProductString ?? "?"}\" iface={d.InterfaceNumber} path={d.Path}");
        }

        if (endpoints.Count == 0)
        {
            LogFirstScanDump(allDevices);
            LogEnumerationOnce(allDevices, matcherMatches);
        }
        return endpoints;
    }

    /// <summary>
    /// Opens <paramref name="info"/> under the global hidapi gate — concurrent
    /// open aborts the process on macOS, so every open in this class flows
    /// through here. Reads/writes on the returned handle are per-device and must
    /// not take the gate.
    /// </summary>
    private static Device OpenGated(HidDeviceInfo info)
    {
        lock (HidGlobalLock.Gate) return info.ConnectToDevice();
    }

    /// <summary>
    /// Opens the candidate endpoints and returns the first that answers a
    /// liveness probe (0xFD → 0xFE), so a dead duplicate bond — which accepts
    /// writes but never delivers input reports — is skipped. A single candidate
    /// is trusted without probing (firmware that doesn't implement 0xFD still
    /// works). If several match but none answer, falls back to the first opened
    /// so the app degrades to the old "first wins" behaviour rather than nothing.
    /// </summary>
    private HidDeviceInfo? SelectLiveDevice(
        List<HidDeviceInfo> candidates, byte[] buffer, CancellationToken ct, out Device? chosen)
    {
        chosen = null;

        if (candidates.Count == 1)
        {
            try { chosen = OpenGated(candidates[0]); return candidates[0]; }
            catch (Exception ex)
            {
                LibLog.Warn("RawHid", $"Open failed (path={candidates[0].Path}): {ex.Message}");
                return null;
            }
        }

        Device? fallback = null;
        HidDeviceInfo? fallbackInfo = null;
        foreach (var info in candidates)
        {
            if (ct.IsCancellationRequested) break;

            Device device;
            try { device = OpenGated(info); }
            catch (Exception ex)
            {
                LibLog.Warn("RawHid", $"Open failed (path={info.Path}): {ex.Message}");
                continue;
            }

            if (ProbeAlive(device, buffer, ct))
            {
                LibLog.Info("RawHid", $"Probe: endpoint responded (path={info.Path}).");
                if (fallback is not null) { try { fallback.Dispose(); } catch { } }
                chosen = device;
                return info;
            }

            LibLog.Info("RawHid", $"Probe: no response (path={info.Path}).");
            if (fallback is null) { fallback = device; fallbackInfo = info; }
            else { try { device.Dispose(); } catch { } }
        }

        if (ct.IsCancellationRequested) { try { fallback?.Dispose(); } catch { } return null; }

        if (fallback is not null)
        {
            LibLog.Warn("RawHid",
                $"Probe: no endpoint answered; falling back to first (path={fallbackInfo!.Path}).");
            chosen = fallback;
            return fallbackInfo;
        }
        return null;
    }

    /// <summary>
    /// Sends a 0xFD device-info request and waits up to <see cref="ProbeTimeoutMs"/>
    /// for the matching 0xFE reply. Reports seen in the meantime are discarded —
    /// the firmware re-emits layer state on the next change, and connect-time
    /// resync covers the current layer.
    /// </summary>
    private bool ProbeAlive(Device device, byte[] buffer, CancellationToken ct)
    {
        try
        {
            // Leading 0x00 is the report-ID byte hidapi expects (firmware uses
            // report ID 0); payload[0] = 0xFD GetDeviceInfo.
            var req = new byte[HidConstants.ReportSize + 1];
            req[1] = HidConstants.Inbound.GetDeviceInfo;
            device.Write(req);
        }
        catch (Exception ex)
        {
            LibLog.Debug("RawHid", $"Probe write failed: {ex.Message}");
            return false;
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ProbeTimeoutMs && !ct.IsCancellationRequested)
        {
            int n;
            try { n = device.ReadTimeout(buffer, ReadTimeoutMs); }
            catch (Exception ex)
            {
                LibLog.Debug("RawHid", $"Probe read failed: {ex.Message}");
                return false;
            }
            if (n <= 0) continue;

            int offset = (n == HidConstants.ReportSize + 1) ? 1 : 0;
            var payload = buffer.AsSpan(offset, n - offset);
            if (payload.Length >= 1 && payload[0] == HidConstants.Outbound.DeviceInfo)
                return true;
        }
        return false;
    }

    private void LogFirstScanDump(List<HidDeviceInfo> all)
    {
        if (_dumpedFirstScan) return;
        _dumpedFirstScan = true;
        LibLog.Info("RawHid", $"First-scan dump: hidapi enumerated {all.Count} device(s).");
        foreach (var d in all)
        {
            LibLog.Info("RawHid",
                $"  VID={d.VendorId:X4} PID={d.ProductId:X4} usage={d.UsagePage:X4}/{d.Usage:X4} " +
                $"iface={d.InterfaceNumber} name=\"{d.ProductString ?? "?"}\" mfg=\"{d.ManufacturerString ?? "?"}\" path={d.Path}");
        }
    }

    private void LogEnumerationOnce(List<HidDeviceInfo> all, List<HidDeviceInfo> matcherMatches)
    {
        var key = string.Join("|", matcherMatches.Select(d => $"{d.VendorId:X4}:{d.ProductId:X4}@{d.Path}"));
        if (key == _lastEnumerationKey) return;
        _lastEnumerationKey = key;

        if (matcherMatches.Count == 0)
        {
            LibLog.Info("RawHid",
                $"Discovery: no matcher-matched HID device found among {all.Count} enumerated device(s).");
            return;
        }

        foreach (var d in matcherMatches)
        {
            LibLog.Info("RawHid",
                $"Discovery: VID={d.VendorId:X4} PID={d.ProductId:X4} name=\"{d.ProductString ?? "?"}\" " +
                $"usage={d.UsagePage:X4}/{d.Usage:X4} path={d.Path} — " +
                "matcher-matched but FF60/61 usage not present on this interface.");
        }
    }
}
