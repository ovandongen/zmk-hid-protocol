namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// A protocol-capable Raw HID device the capability layer can talk to: read its
/// reports and write reports back. Deliberately minimal — <em>I/O plus transport
/// identity only</em>. It carries no manifest and no <em>stable</em> device key;
/// the stable key (configId GUID if present, else vid:pid:name) is derived by the
/// router from the discovered <see cref="DeviceManifest"/>, so a device never
/// exposes a half-initialized key before its manifest has been read.
///
/// <para>Two implementations exist: a bus device wrapping one open hidapi handle
/// (this module) and an adapter over the app's already-open keyboard pipeline (so
/// the keyboard is never opened twice). The router consumes both uniformly.</para>
///
/// <para><see cref="ReportReceived"/> may fire on any thread — subscribers that
/// touch UI state must marshal themselves.</para>
/// </summary>
public interface ICapabilityDevice
{
    /// <summary>Human-readable label for UI/diagnostics (e.g. the product string).</summary>
    string DisplayName { get; }

    /// <summary>
    /// Transport-level identity (e.g. <c>vid:pid:path</c>) — stable for the
    /// lifetime of one open handle and unique per physical interface. Used for
    /// dedup and to exclude the keyboard; <em>not</em> the persisted device key.
    /// </summary>
    string TransportKey { get; }

    /// <summary>Raised for every received 32-byte report. Fires on any thread.</summary>
    event Action<ReadOnlyMemory<byte>>? ReportReceived;

    /// <summary>Writes one report to the device (the transport prepends the report-id).</summary>
    ValueTask SendReportAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken);
}
