using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Reads one device's capability manifest: subscribes to
/// <see cref="ICapabilityDevice.ReportReceived"/>, sends a single 0xF9
/// get-manifest request, reassembles the 0xF8 stream with
/// <see cref="ManifestAssembler"/>, and returns the built
/// <see cref="DeviceManifest"/> once the LAST flag arrives. Mirrors the
/// reference host's <c>query_manifest</c>.
///
/// <para>Standalone on purpose: it works over <em>any</em>
/// <see cref="ICapabilityDevice"/>, so the keyboard adapter and the bus share
/// one reader. It is <em>not</em> bolted onto <c>CommandSender</c>, whose
/// FIFO-by-opcode correlation models single request→response pairs, not a
/// multi-report stream terminated by a flag.</para>
///
/// <para>Strict by design: a stream that never carries the LAST flag within
/// <paramref name="timeout"/> yields <c>null</c> (a device that doesn't speak
/// the manifest protocol, or a truncated read) rather than a partial manifest —
/// the caller retries on the next hot-plug poll. The subscription is always
/// removed in <c>finally</c>.</para>
/// </summary>
public static class ManifestReader
{
    /// <summary>
    /// Queries <paramref name="device"/> for its manifest. Returns the assembled
    /// manifest on success, or <c>null</c> if the stream did not complete within
    /// <paramref name="timeout"/>. Throws <see cref="OperationCanceledException"/>
    /// only if <paramref name="cancellationToken"/> itself fires.
    /// </summary>
    public static async Task<DeviceManifest?> ReadAsync(
        ICapabilityDevice device,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var assembler = new ManifestAssembler();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Action<ReadOnlyMemory<byte>> handler = report =>
        {
            var entry = RawHidProtocol.TryParseManifestEntry(report.Span);
            if (entry is null) return; // interleaved 0xFF/0xF1/etc. — ignored
            assembler.Add(entry);
            if (assembler.IsComplete) completed.TrySetResult(true);
        };

        // Subscribe before sending so a fast first report can't be missed.
        device.ReportReceived += handler;
        try
        {
            var request = new byte[HidConstants.ReportSize];
            request[0] = HidConstants.Inbound.GetManifest; // byte[1] start-index left 0 = from the beginning
            await device.SendReportAsync(request, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await using var _ = timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), completed);

            try
            {
                await completed.Task.ConfigureAwait(false);
                return assembler.Build();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null; // timed out before the LAST flag
            }
        }
        finally
        {
            device.ReportReceived -= handler;
        }
    }
}
