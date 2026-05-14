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
            HidDeviceInfo? info = TryFindDevice();
            if (info is null)
            {
                _rescan.Reset();
                try { _rescan.Wait(ReconnectDelayMs, ct); } catch (OperationCanceledException) { return; }
                continue;
            }

            Device? device = null;
            try
            {
                device = info.ConnectToDevice();
                _device = device;

                _sourceName = string.IsNullOrWhiteSpace(info.ProductString)
                    ? "Raw HID"
                    : $"Raw HID ({info.ProductString})";
                LibLog.Info("RawHid", $"Connected: {_sourceName}");
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

    private HidDeviceInfo? TryFindDevice()
    {
        IEnumerable<HidDeviceInfo> devices;
        try { devices = Hid.Enumerate(); }
        catch (Exception ex)
        {
            LibLog.Debug("RawHid", $"Hid.Enumerate failed: {ex.Message}");
            return null;
        }

        var matcher = _matcher;
        var matcherMatches = new List<HidDeviceInfo>();
        var allDevices = new List<HidDeviceInfo>();
        foreach (var d in devices)
        {
            allDevices.Add(d);
            if (matcher is not null && !matcher.Matches(d.VendorId, d.ProductId, d.ProductString))
                continue;
            matcherMatches.Add(d);
            if (d.UsagePage == HidConstants.UsagePage && d.Usage == HidConstants.UsageId)
                return d;
        }

        LogFirstScanDump(allDevices);
        LogEnumerationOnce(allDevices, matcherMatches);
        return null;
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
