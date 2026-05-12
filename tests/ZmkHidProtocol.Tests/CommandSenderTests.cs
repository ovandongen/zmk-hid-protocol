using System.Buffers.Binary;
using Xunit;
using ZmkHidProtocol.Protocol;
using ZmkHidProtocol.Transport;

namespace ZmkHidProtocol.Tests;

public class CommandSenderTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task QueryDeviceInfo_RoundTrip_ReturnsParsedInfo()
    {
        var fake = new FakeTransport();
        using var sender = new CommandSender(fake, fake);

        var task = sender.QueryDeviceInfoAsync(LongTimeout, CancellationToken.None);
        await fake.WriteObserved;

        Assert.Equal(HidConstants.Inbound.GetDeviceInfo, fake.LastSent.Span[0]);

        fake.RaiseReport(BuildDeviceInfo(2, "Glove80"));

        var info = await task;
        Assert.NotNull(info);
        Assert.Equal(2, info!.ProtocolVersion);
        Assert.Equal("Glove80", info.Name);
    }

    [Fact]
    public async Task QueryConfigId_RoundTrip_ReturnsParsedString()
    {
        var fake = new FakeTransport();
        using var sender = new CommandSender(fake, fake);

        var task = sender.QueryConfigIdAsync(LongTimeout, CancellationToken.None);
        await fake.WriteObserved;

        Assert.Equal(HidConstants.Inbound.GetConfigId, fake.LastSent.Span[0]);

        fake.RaiseReport(BuildConfigId("glove80-v1"));

        var id = await task;
        Assert.Equal("glove80-v1", id);
    }

    [Fact]
    public async Task SetLayerStateAsync_WritesOpcodeAndMaskLittleEndian()
    {
        var fake = new FakeTransport();
        using var sender = new CommandSender(fake, fake);

        await sender.SetLayerStateAsync(0xDEADBEEF, CancellationToken.None);

        var sent = fake.LastSent.Span;
        Assert.Equal(HidConstants.ReportSize, sent.Length);
        Assert.Equal(HidConstants.Inbound.SetLayerState, sent[0]);
        Assert.Equal(0xDEADBEEFu, BinaryPrimitives.ReadUInt32LittleEndian(sent[1..5]));
        for (int i = 5; i < sent.Length; i++) Assert.Equal(0, sent[i]);
    }

    [Fact]
    public async Task QueryDeviceInfo_Timeout_ReturnsNull()
    {
        var fake = new FakeTransport();
        using var sender = new CommandSender(fake, fake);

        var info = await sender.QueryDeviceInfoAsync(ShortTimeout, CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task QueryDeviceInfo_Cancelled_ThrowsOperationCanceled()
    {
        var fake = new FakeTransport();
        using var sender = new CommandSender(fake, fake);

        using var cts = new CancellationTokenSource();
        var task = sender.QueryDeviceInfoAsync(LongTimeout, cts.Token);
        await fake.WriteObserved;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task IrrelevantReports_DoNotCompleteWaiters()
    {
        var fake = new FakeTransport();
        using var sender = new CommandSender(fake, fake);

        var task = sender.QueryDeviceInfoAsync(ShortTimeout, CancellationToken.None);
        await fake.WriteObserved;

        // 0xFF layer-state report and 0xFA config-id report should not satisfy a 0xFD waiter.
        var layerReport = new byte[HidConstants.ReportSize];
        layerReport[0] = HidConstants.Outbound.LayerState;
        fake.RaiseReport(layerReport);
        fake.RaiseReport(BuildConfigId("noise"));

        var info = await task;
        Assert.Null(info); // timed out — no 0xFE arrived
    }

    [Fact]
    public async Task ConcurrentDeviceInfoQueries_AreMatchedFifo()
    {
        var fake = new FakeTransport();
        using var sender = new CommandSender(fake, fake);

        var first = sender.QueryDeviceInfoAsync(LongTimeout, CancellationToken.None);
        await fake.WriteObserved;
        fake.ResetWriteObserved();

        var second = sender.QueryDeviceInfoAsync(LongTimeout, CancellationToken.None);
        await fake.WriteObserved;

        fake.RaiseReport(BuildDeviceInfo(1, "first"));
        fake.RaiseReport(BuildDeviceInfo(2, "second"));

        var a = await first;
        var b = await second;
        Assert.Equal("first", a!.Name);
        Assert.Equal("second", b!.Name);
    }

    [Fact]
    public async Task ConcurrentMixedOpcodes_AreCorrelatedIndependently()
    {
        var fake = new FakeTransport();
        using var sender = new CommandSender(fake, fake);

        var infoTask = sender.QueryDeviceInfoAsync(LongTimeout, CancellationToken.None);
        await fake.WriteObserved;
        fake.ResetWriteObserved();
        var idTask = sender.QueryConfigIdAsync(LongTimeout, CancellationToken.None);
        await fake.WriteObserved;

        // Reply in reverse order — each waiter only listens for its own opcode.
        fake.RaiseReport(BuildConfigId("cfg"));
        fake.RaiseReport(BuildDeviceInfo(3, "kb"));

        Assert.Equal("cfg", await idTask);
        var info = await infoTask;
        Assert.Equal("kb", info!.Name);
    }

    [Fact]
    public void Dispose_UnsubscribesFromReportReceived()
    {
        var fake = new FakeTransport();
        var sender = new CommandSender(fake, fake);
        Assert.True(fake.HasReportSubscribers);
        sender.Dispose();
        Assert.False(fake.HasReportSubscribers);
    }

    private static byte[] BuildDeviceInfo(byte protocolVersion, string name)
    {
        var report = new byte[HidConstants.ReportSize];
        report[0] = HidConstants.Outbound.DeviceInfo;
        report[1] = protocolVersion;
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        nameBytes.CopyTo(report.AsSpan(2));
        return report;
    }

    private static byte[] BuildConfigId(string id)
    {
        var report = new byte[HidConstants.ReportSize];
        report[0] = HidConstants.Outbound.ConfigId;
        var idBytes = System.Text.Encoding.UTF8.GetBytes(id);
        idBytes.CopyTo(report.AsSpan(1));
        return report;
    }

    private sealed class FakeTransport : ILayerSource, ICommandSink
    {
        private TaskCompletionSource _writeObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ReadOnlyMemory<byte> LastSent { get; private set; }
        public Task WriteObserved => _writeObserved.Task;

        public bool HasReportSubscribers => ReportReceived is not null;

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

#pragma warning disable CS0067 // Unused events required by the ILayerSource contract.
        public event Action<int>? LayerChanged;
        public event Action<int, bool>? KeyPositionEvent;
        public event Action<ReadOnlyMemory<byte>>? ReportReceived;
        public event Action? ConnectionChanged;
#pragma warning restore CS0067

        public bool IsConnected => true;
        public int CurrentLayer => 0;
        public string SourceName => "Fake";
        public void SetMatcher(IDeviceMatcher? matcher) { }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
