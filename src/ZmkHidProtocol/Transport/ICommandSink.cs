namespace ZmkHidProtocol.Transport;

/// <summary>
/// Write-side capability paired with <see cref="ILayerSource"/>. Every
/// platform source implements both so writes reuse the already-open device
/// handle (mandatory on macOS where mid-session re-acquisition is fragile,
/// and on Windows BLE where reconnection takes seconds).
///
/// <para>Reports are exactly <see cref="Protocol.HidConstants.ReportSize"/>
/// bytes; the caller is responsible for padding. Implementations may queue
/// writes onto a transport-owned thread (e.g. CFRunLoop on macOS).</para>
/// </summary>
public interface ICommandSink
{
    ValueTask SendReportAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken);
}
