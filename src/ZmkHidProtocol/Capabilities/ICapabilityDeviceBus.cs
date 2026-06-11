namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// A consumer's read-only view of a multi-device bus: the set of currently open
/// protocol-capable devices plus a hot-plug change signal. The host keyboard is
/// excluded by construction (see <c>Transport.RawHidDeviceBus</c>'s matcher /
/// open-path exclusions) — a router consumes it as a separate
/// <see cref="ICapabilityDevice"/> adapter instead.
///
/// <para>A seam over <c>Transport.RawHidDeviceBus</c> so consumers are testable
/// without hardware. The bus's own Start/Stop/matcher lifecycle stays with
/// whoever constructs it; consumers only read the device set.</para>
/// </summary>
public interface ICapabilityDeviceBus
{
    /// <summary>Currently open bus devices (host keyboard excluded). Thread-safe snapshot.</summary>
    IReadOnlyList<ICapabilityDevice> Devices { get; }

    /// <summary>Raised after the open device set changes (hot-plug). Fires on a background thread.</summary>
    event Action<IReadOnlyList<ICapabilityDevice>>? DevicesChanged;
}
