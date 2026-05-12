using System.Runtime.Versioning;

namespace ZmkHidProtocol.Transport.Windows;

/// <summary>
/// Fans out the HidSharp-based USB raw-HID source and the WinRT GATT-based
/// BLE raw-HID source so callers see a single <see cref="ILayerSource"/> +
/// <see cref="ICommandSink"/>. The two transports are mutually invisible:
/// HidSharp can't see the ZMK vendor FF60/61 collection over BLE (Windows'
/// HoGP driver strips it) and <see cref="WindowsBleRawHidLayerSource"/>
/// only enumerates GATT services. Running both in parallel means the user
/// doesn't have to know which transport their keyboard is on.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WindowsCompositeLayerSource : ILayerSource, ICommandSink
{
    private readonly WindowsRawHidLayerSource _usb;
    private readonly WindowsBleRawHidLayerSource _ble;
    private volatile int _currentLayer;
    private string _sourceName = "Raw HID";

    public WindowsCompositeLayerSource(IDeviceMatcher? matcher = null)
    {
        _usb = new WindowsRawHidLayerSource(matcher);
        _ble = new WindowsBleRawHidLayerSource(matcher);

        _usb.LayerChanged += OnChildLayer;
        _usb.KeyPositionEvent += OnChildKey;
        _usb.ReportReceived += OnChildReport;
        _usb.ConnectionChanged += OnChildConnectionChanged;
        _ble.LayerChanged += OnChildLayer;
        _ble.KeyPositionEvent += OnChildKey;
        _ble.ReportReceived += OnChildReport;
        _ble.ConnectionChanged += OnChildConnectionChanged;
    }

    public event Action<int>? LayerChanged;
    public event Action<int, bool>? KeyPositionEvent;
    public event Action<ReadOnlyMemory<byte>>? ReportReceived;
    public event Action? ConnectionChanged;

    public bool IsConnected => _usb.IsConnected || _ble.IsConnected;
    public int CurrentLayer => _currentLayer;
    public string SourceName => _sourceName;

    public void SetMatcher(IDeviceMatcher? matcher)
    {
        _usb.SetMatcher(matcher);
        _ble.SetMatcher(matcher);
    }

    public void Start()
    {
        _usb.Start();
        _ble.Start();
    }

    public void Stop()
    {
        _usb.Stop();
        _ble.Stop();
    }

    public ValueTask SendReportAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
    {
        // BLE-preferred: keeps the request/response channel consistent with
        // reads when the user is typing over BLE while USB is plugged in only
        // for charging.
        if (_ble.IsConnected)
            return _ble.SendReportAsync(report, cancellationToken);
        if (_usb.IsConnected)
            return _usb.SendReportAsync(report, cancellationToken);
        throw new InvalidOperationException("No transport connected");
    }

    public void Dispose()
    {
        Stop();
        _usb.Dispose();
        _ble.Dispose();
    }

    private void OnChildLayer(int layer)
    {
        _currentLayer = layer;
        LayerChanged?.Invoke(layer);
    }

    private void OnChildKey(int position, bool pressed) =>
        KeyPositionEvent?.Invoke(position, pressed);

    private void OnChildReport(ReadOnlyMemory<byte> report) =>
        ReportReceived?.Invoke(report);

    private void OnChildConnectionChanged()
    {
        _sourceName = _ble.IsConnected ? _ble.SourceName
            : _usb.IsConnected ? _usb.SourceName
            : "Raw HID";
        ConnectionChanged?.Invoke();
    }
}
