using Xunit;
using ZmkHidProtocol.Capabilities;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Coverage for <see cref="ConfirmTracker"/>: ref allocation, resolve-by-ref with
/// the ok flag, stray/unknown confirms, and timeout-as-cancellation.
/// </summary>
public class ConfirmTrackerTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public void Register_AllocatesNonZeroDistinctRefs()
    {
        var tracker = new ConfirmTracker();

        var (first, _) = tracker.Register(LongTimeout, CancellationToken.None);
        var (second, _) = tracker.Register(LongTimeout, CancellationToken.None);

        Assert.NotEqual(0, first);
        Assert.NotEqual(0, second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Resolve_CompletesWaiterWithOkTrue()
    {
        var tracker = new ConfirmTracker();
        var (reference, completion) = tracker.Register(LongTimeout, CancellationToken.None);

        Assert.True(tracker.Resolve(new ConfirmAck(reference, Ok: true)));
        Assert.True(await completion);
    }

    [Fact]
    public async Task Resolve_CompletesWaiterWithOkFalse()
    {
        var tracker = new ConfirmTracker();
        var (reference, completion) = tracker.Register(LongTimeout, CancellationToken.None);

        Assert.True(tracker.Resolve(new ConfirmAck(reference, Ok: false)));
        Assert.False(await completion);
    }

    [Fact]
    public void Resolve_UnknownRef_IsNoOp()
    {
        var tracker = new ConfirmTracker();
        tracker.Register(LongTimeout, CancellationToken.None); // ref 1 pending

        Assert.False(tracker.Resolve(new ConfirmAck(999, Ok: true)));
    }

    [Fact]
    public async Task Resolve_OnlyCompletesMatchingRef()
    {
        var tracker = new ConfirmTracker();
        var (refA, a) = tracker.Register(LongTimeout, CancellationToken.None);
        var (refB, b) = tracker.Register(LongTimeout, CancellationToken.None);

        tracker.Resolve(new ConfirmAck(refB, Ok: true));

        Assert.True(await b);
        Assert.False(a.IsCompleted); // refA still pending
        Assert.NotEqual(refA, refB);
    }

    [Fact]
    public async Task Register_Timeout_CancelsWaiter()
    {
        var tracker = new ConfirmTracker();
        var (_, completion) = tracker.Register(ShortTimeout, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
    }

    [Fact]
    public async Task Resolve_AfterTimeout_IsNoOp()
    {
        var tracker = new ConfirmTracker();
        var (reference, completion) = tracker.Register(ShortTimeout, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        // The ref has been reclaimed; a late confirm finds no waiter.
        Assert.False(tracker.Resolve(new ConfirmAck(reference, Ok: true)));
    }
}
