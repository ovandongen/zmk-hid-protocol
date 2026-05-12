using System.Buffers.Binary;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Transport;

/// <summary>
/// Orchestrates the host → keyboard command opcodes (0xFD device-info,
/// 0xFB config-id, 0xFC set-layer-state) and correlates responses
/// (0xFE, 0xFA) back to the caller. Subscribes to
/// <see cref="ILayerSource.ReportReceived"/> for response routing and
/// uses the paired <see cref="ICommandSink"/> for writes.
///
/// <para>The firmware carries no correlation ID, so concurrent requests
/// of the same opcode are matched FIFO: the first request that observed
/// a write also receives the first response of the matching reply
/// opcode.</para>
///
/// <para>Layer-state quirk: the firmware always keeps layer 0 active on
/// 0xFC regardless of bit 0 in the mask. Callers should preserve their
/// original mask if they care about that bit's logical value.</para>
/// </summary>
public sealed class CommandSender : IDisposable
{
    private readonly ILayerSource _source;
    private readonly ICommandSink _sink;
    private readonly object _lock = new();
    private readonly Queue<TaskCompletionSource<DeviceInfo>> _deviceInfoWaiters = new();
    private readonly Queue<TaskCompletionSource<string>> _configIdWaiters = new();
    private bool _disposed;

    public CommandSender(ILayerSource source, ICommandSink sink)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _source.ReportReceived += OnReport;
    }

    /// <summary>
    /// Sends a 0xFD request and awaits the matching 0xFE response.
    /// Returns null on timeout. Throws <see cref="OperationCanceledException"/>
    /// if <paramref name="cancellationToken"/> fires.
    /// </summary>
    public Task<DeviceInfo?> QueryDeviceInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => AwaitResponseAsync(_deviceInfoWaiters, HidConstants.Inbound.GetDeviceInfo, timeout, cancellationToken);

    /// <summary>
    /// Sends a 0xFB request and awaits the matching 0xFA response.
    /// Returns null on timeout. Throws <see cref="OperationCanceledException"/>
    /// if <paramref name="cancellationToken"/> fires.
    /// </summary>
    public Task<string?> QueryConfigIdAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => AwaitResponseAsync(_configIdWaiters, HidConstants.Inbound.GetConfigId, timeout, cancellationToken);

    /// <summary>
    /// Sends a 0xFC report with the supplied layer bitmask (uint32 LE at bytes 1-4).
    /// Fire-and-forget — the firmware does not acknowledge.
    /// </summary>
    public ValueTask SetLayerStateAsync(uint layerBitmask, CancellationToken cancellationToken)
    {
        var report = new byte[HidConstants.ReportSize];
        report[0] = HidConstants.Inbound.SetLayerState;
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(1, 4), layerBitmask);
        return _sink.SendReportAsync(report, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.ReportReceived -= OnReport;
        FailAll(_deviceInfoWaiters);
        FailAll(_configIdWaiters);
    }

    private async Task<T?> AwaitResponseAsync<T>(
        Queue<TaskCompletionSource<T>> queue,
        byte requestOpcode,
        TimeSpan timeout,
        CancellationToken cancellationToken) where T : class
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) queue.Enqueue(tcs);

        var report = new byte[HidConstants.ReportSize];
        report[0] = requestOpcode;
        await _sink.SendReportAsync(report, cancellationToken).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        await using var _ = timeoutCts.Token.Register(static state =>
            ((TaskCompletionSource<T>)state!).TrySetCanceled(), tcs);

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void OnReport(ReadOnlyMemory<byte> report)
    {
        var span = report.Span;
        if (span.Length < 1) return;
        switch (span[0])
        {
            case HidConstants.Outbound.DeviceInfo:
                if (RawHidProtocol.TryParseDeviceInfo(span) is { } info)
                    CompleteFirst(_deviceInfoWaiters, info);
                break;
            case HidConstants.Outbound.ConfigId:
                if (RawHidProtocol.TryParseConfigId(span) is { } id)
                    CompleteFirst(_configIdWaiters, id);
                break;
        }
    }

    private void CompleteFirst<T>(Queue<TaskCompletionSource<T>> queue, T value)
    {
        lock (_lock)
        {
            while (queue.TryDequeue(out var tcs))
            {
                if (tcs.TrySetResult(value))
                    return;
            }
        }
    }

    private void FailAll<T>(Queue<TaskCompletionSource<T>> queue)
    {
        lock (_lock)
        {
            while (queue.TryDequeue(out var tcs))
                tcs.TrySetCanceled();
        }
    }
}

