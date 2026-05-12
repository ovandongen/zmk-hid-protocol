namespace ZmkHidProtocol.Transport;

/// <summary>
/// A source that reads Raw HID reports from a ZMK keyboard and surfaces
/// layer-state and per-key events. Implementations exist for macOS (IOKit),
/// Linux (/dev/hidraw), and Windows (HidSharp USB + WinRT BLE).
///
/// <para>Implementations may invoke events on any thread. Subscribers that
/// touch UI state must marshal to their own UI thread.</para>
/// </summary>
public interface ILayerSource : IDisposable
{
    /// <summary>Raised whenever the active layer changes (highest-set-bit semantics).</summary>
    event Action<int>? LayerChanged;

    /// <summary>
    /// Raised on physical key press / release events. <c>position</c> is the
    /// firmware's matrix index.
    /// </summary>
    event Action<int, bool>? KeyPositionEvent;

    /// <summary>
    /// Raised for every received report. Carries the raw 32-byte payload so
    /// callers (e.g. <c>CommandSender</c>) can pattern-match on opcodes
    /// (0xFE device info, 0xFA config ID) the source itself doesn't expose
    /// as typed events.
    /// </summary>
    event Action<ReadOnlyMemory<byte>>? ReportReceived;

    /// <summary>Raised whenever <see cref="IsConnected"/> changes.</summary>
    event Action? ConnectionChanged;

    /// <summary>True when the source is currently delivering events (device is open).</summary>
    bool IsConnected { get; }

    /// <summary>
    /// The layer the source last reported (or 0 if it has reported nothing
    /// yet). Lets coordinators resync immediately on source-change.
    /// </summary>
    int CurrentLayer { get; }

    /// <summary>Short human-readable name shown in status/diagnostics.</summary>
    string SourceName { get; }

    /// <summary>
    /// Configures which physical keyboard this source should attach to.
    /// May be called before or after <see cref="Start"/>; the source
    /// re-evaluates currently-enumerated devices on change. Pass <c>null</c>
    /// to detach.
    /// </summary>
    void SetMatcher(IDeviceMatcher? matcher);

    /// <summary>Start delivering events (idempotent).</summary>
    void Start();

    /// <summary>Stop delivering events but keep the object usable for a subsequent Start().</summary>
    void Stop();
}
