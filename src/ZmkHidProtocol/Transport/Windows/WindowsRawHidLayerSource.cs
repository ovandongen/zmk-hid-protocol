using HidSharp;
using ZmkHidProtocol.Diagnostics;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Transport.Windows;

/// <summary>
/// Raw-HID layer source for Windows USB. Discovers any HID device exposing
/// usage page 0xFF60 / usage 0x61, opens it via HidSharp, and parses 32-byte
/// reports off a background thread.
///
/// <para>HidSharp on Windows enumerates only USB-attached HID devices; BLE
/// goes through <c>WindowsBleRawHidLayerSource</c> (HoGP service 0x1812 is
/// claimed exclusively by Windows' HID driver, so the firmware exposes a
/// parallel vendor GATT service).</para>
///
/// <para>Hot-plug: subscribes to <see cref="DeviceList.Local"/> changes so a
/// device appearing or disappearing wakes the read loop without waiting out
/// the reconnect backoff.</para>
/// </summary>
public sealed class WindowsRawHidLayerSource : ILayerSource, ICommandSink
{
    private const int ReconnectDelayMs = 2000;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private HidStream? _stream;
    private string _sourceName = "Raw HID";
    private volatile bool _connected;
    private volatile int _currentLayer;
    private readonly ManualResetEventSlim _rescan = new(false);
    private volatile IDeviceMatcher? _matcher;
    private string? _lastEnumerationKey;
    private readonly HashSet<string> _descriptorErrorReported = new();
    private readonly object _writeLock = new();

    public WindowsRawHidLayerSource(IDeviceMatcher? matcher = null)
    {
        _matcher = matcher;
    }

    public void SetMatcher(IDeviceMatcher? matcher)
    {
        _matcher = matcher;
        try { _stream?.Close(); } catch { }
        _rescan.Set();
    }

    public event Action<int>? LayerChanged;
    public event Action<int, bool>? KeyPositionEvent;
    public event Action<ReadOnlyMemory<byte>>? ReportReceived;
    public event Action? ConnectionChanged;

    public bool IsConnected => _connected;
    public string SourceName => _sourceName;
    public int CurrentLayer => _currentLayer;

    public void Start()
    {
        if (_runTask is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        DeviceList.Local.Changed += OnDeviceListChanged;
        _runTask = Task.Run(() => RunLoop(ct));
        LibLog.Info("WinRawHid", "WindowsRawHidLayerSource started");
    }

    public void Stop()
    {
        if (_runTask is null) return;
        DeviceList.Local.Changed -= OnDeviceListChanged;
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Close(); } catch { }
        _stream = null;
        try { _runTask.Wait(1000); } catch { }
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
        var stream = _stream;
        if (stream is null) throw new InvalidOperationException("WindowsRawHidLayerSource: device is not open.");
        // HidSharp expects a report-ID byte at index 0. The firmware uses
        // report ID 0 (no report ID), so prepend a leading 0x00.
        var prefixed = new byte[report.Length + 1];
        report.Span.CopyTo(prefixed.AsSpan(1));
        lock (_writeLock)
        {
            stream.Write(prefixed, 0, prefixed.Length);
        }
        return ValueTask.CompletedTask;
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e) => _rescan.Set();

    private void RunLoop(CancellationToken ct)
    {
        var buffer = new byte[HidConstants.ReportSize + 1];

        while (!ct.IsCancellationRequested)
        {
            HidDevice? device = TryFindDevice();
            if (device is null)
            {
                _rescan.Reset();
                try { _rescan.Wait(ReconnectDelayMs, ct); } catch (OperationCanceledException) { return; }
                continue;
            }

            HidStream? stream = null;
            try
            {
                stream = device.Open();
                _stream = stream;
                _stream.ReadTimeout = Timeout.Infinite;

                string? productName = null;
                try { productName = device.GetProductName(); } catch { }
                _sourceName = string.IsNullOrWhiteSpace(productName)
                    ? "Raw HID"
                    : $"Raw HID ({productName})";
                LibLog.Info("WinRawHid", $"Connected: {_sourceName}");
                SetConnected(true);
                _lastEnumerationKey = null;
                _descriptorErrorReported.Clear();

                while (!ct.IsCancellationRequested)
                {
                    int n;
                    try { n = _stream.Read(buffer, 0, buffer.Length); }
                    catch (Exception ex)
                    {
                        LibLog.Debug("WinRawHid", $"Read failed: {ex.Message}");
                        break;
                    }
                    if (n <= 0) continue;

                    int offset = (n == HidConstants.ReportSize + 1) ? 1 : 0;
                    DispatchReport(buffer.AsSpan(offset, n - offset));
                }
            }
            catch (Exception ex)
            {
                LibLog.Warn("WinRawHid", $"Open/read error: {ex.Message}");
            }
            finally
            {
                try { stream?.Close(); } catch { }
                _stream = null;
                if (_connected)
                {
                    SetConnected(false);
                    LibLog.Info("WinRawHid", "Disconnected");
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

    private HidDevice? TryFindDevice()
    {
        HidDevice[] devices;
        try { devices = DeviceList.Local.GetHidDevices().ToArray(); }
        catch (Exception ex)
        {
            LibLog.Debug("WinRawHid", $"GetHidDevices failed: {ex.Message}");
            return null;
        }

        var matcher = _matcher;
        var matcherMatches = new List<HidDevice>();
        foreach (var d in devices)
        {
            if (matcher is not null)
            {
                string? name = null;
                try { name = d.GetProductName(); } catch { }
                if (!matcher.Matches(d.VendorID, d.ProductID, name))
                    continue;
            }
            matcherMatches.Add(d);
            if (HasRawHidUsage(d)) return d;
        }

        LogEnumerationOnce(devices, matcherMatches);
        return null;
    }

    private void LogEnumerationOnce(HidDevice[] all, List<HidDevice> matcherMatches)
    {
        var key = string.Join("|", matcherMatches.Select(d => $"{d.VendorID:X4}:{d.ProductID:X4}@{TryGetDevicePath(d)}"));
        if (key == _lastEnumerationKey) return;
        _lastEnumerationKey = key;

        if (matcherMatches.Count == 0)
        {
            LibLog.Info("WinRawHid",
                $"Discovery: no matcher-matched HID device found among {all.Length} enumerated device(s). " +
                "If the keyboard is connected over Bluetooth, Windows' HoGP driver may not be exposing it as a HID device — check Device Manager > Human Interface Devices.");
            return;
        }

        foreach (var d in matcherMatches)
        {
            string? name = null;
            try { name = d.GetProductName(); } catch { }
            LibLog.Info("WinRawHid",
                $"Discovery: VID={d.VendorID:X4} PID={d.ProductID:X4} name=\"{name ?? "?"}\" path={TryGetDevicePath(d)} — " +
                "matcher-matched but FF60/61 usage not found in report descriptor (or descriptor read failed).");
        }
    }

    private static string TryGetDevicePath(HidDevice device)
    {
        try { return device.DevicePath; } catch { return "(unknown)"; }
    }

    private bool HasRawHidUsage(HidDevice device)
    {
        try
        {
            var desc = device.GetReportDescriptor();
            foreach (var item in desc.DeviceItems)
            {
                foreach (var usage in item.Usages.GetAllValues())
                {
                    uint page = (usage >> 16) & 0xFFFF;
                    uint id = usage & 0xFFFF;
                    if (page == HidConstants.UsagePage && id == HidConstants.UsageId)
                        return true;
                }
            }
        }
        catch (Exception ex)
        {
            var key = TryGetDevicePath(device);
            if (_descriptorErrorReported.Add(key))
                LibLog.Info("WinRawHid",
                    $"Descriptor read failed for VID={device.VendorID:X4} PID={device.ProductID:X4} path={key}: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }
}
