using ZmkHidProtocol.Capabilities;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// In-memory <see cref="ICapabilityDevice"/> for protocol tests — modelled on
/// the <c>FakeTransport</c> in <c>CommandSenderTests</c>. Records the last report
/// written, signals <see cref="WriteObserved"/> when a write lands, and lets a
/// test push reports back via <see cref="RaiseReport"/> / <see cref="RaiseReports"/>.
/// </summary>
internal sealed class FakeCapabilityDevice : ICapabilityDevice
{
    private TaskCompletionSource _writeObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string DisplayName { get; init; } = "Fake";
    public string TransportKey { get; init; } = "fake:0000:0000";

    public ReadOnlyMemory<byte> LastSent { get; private set; }
    public Task WriteObserved => _writeObserved.Task;
    public bool HasReportSubscribers => ReportReceived is not null;

    public event Action<ReadOnlyMemory<byte>>? ReportReceived;

    public void ResetWriteObserved() =>
        _writeObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask SendReportAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSent = report.ToArray();
        _writeObserved.TrySetResult();
        return ValueTask.CompletedTask;
    }

    public void RaiseReport(ReadOnlyMemory<byte> report) => ReportReceived?.Invoke(report);

    public void RaiseReports(IEnumerable<byte[]> reports)
    {
        foreach (var r in reports) RaiseReport(r);
    }
}
