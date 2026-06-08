namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Bookkeeping for confirm-flagged actions (activate / deactivate). The host
/// assigns each outgoing action a uint16 <c>ref</c>, holds a pending table, and
/// resolves the matching waiter when the device echoes the ref back in a 0xF7
/// <see cref="ConfirmAck"/> — or cancels the waiter on timeout. Device-agnostic:
/// the owner wires its device's report stream to <see cref="Resolve"/>.
///
/// <para>Usage is register-then-send so a fast confirm can't race the waiter:
/// call <see cref="Register"/> to allocate the ref + arm the wait, build/send the
/// action report with that ref, then await the returned task. The task yields the
/// handler's ok flag, or throws <see cref="OperationCanceledException"/> on
/// timeout (distinguishing "no confirm" from "device reported not-ok").</para>
/// </summary>
public sealed class ConfirmTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<ushort, TaskCompletionSource<bool>> _pending = new();
    private ushort _next;

    /// <summary>
    /// Allocates a fresh ref, registers a waiter armed to cancel after
    /// <paramref name="timeout"/>, and returns both. The caller must send the
    /// action report carrying <c>Reference</c>, then await <c>Completion</c>.
    /// </summary>
    public (ushort Reference, Task<bool> Completion) Register(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ushort reference;
        lock (_lock)
        {
            reference = NextFreeRef();
            _pending[reference] = tcs;
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        var registration = linked.Token.Register(() =>
        {
            lock (_lock) _pending.Remove(reference);
            tcs.TrySetCanceled();
        });

        // Release the timer + registration once the wait settles, either way.
        tcs.Task.ContinueWith(
            static (_, state) =>
            {
                var (reg, cts) = ((CancellationTokenRegistration, CancellationTokenSource))state!;
                reg.Dispose();
                cts.Dispose();
            },
            (registration, linked),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return (reference, tcs.Task);
    }

    /// <summary>
    /// Resolves the waiter matching <paramref name="ack"/>'s ref with its ok
    /// flag. Returns false (no-op) if no waiter is pending for that ref — a stray
    /// or already-timed-out confirm.
    /// </summary>
    public bool Resolve(ConfirmAck ack)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_lock)
        {
            if (!_pending.Remove(ack.Reference, out tcs)) return false;
        }
        return tcs.TrySetResult(ack.Ok);
    }

    private ushort NextFreeRef()
    {
        // Caller holds _lock. Skip 0 (reserved as a "no ref" sentinel) and any
        // ref still in flight. Wraps 1..65535.
        for (int attempts = 0; attempts <= ushort.MaxValue; attempts++)
        {
            _next = (ushort)(_next == ushort.MaxValue ? 1 : _next + 1);
            if (!_pending.ContainsKey(_next)) return _next;
        }
        throw new InvalidOperationException("No free confirm reference available.");
    }
}
