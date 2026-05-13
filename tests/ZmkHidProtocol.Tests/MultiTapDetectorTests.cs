using Xunit;
using ZmkHidProtocol.Building;

namespace ZmkHidProtocol.Tests;

public class MultiTapDetectorTests
{
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private MultiTapDetector NewDetector(out int[] firedCount)
    {
        var count = new[] { 0 };
        var det = new MultiTapDetector(() => _now);
        det.Triggered += () => count[0]++;
        firedCount = count;
        return det;
    }

    [Fact]
    public void DoubleTap_WithinWindow_Fires()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);

        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);
        _now = _now.AddMilliseconds(200);
        det.OnKeyEvent(42, true);

        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void SingleTap_DoesNotFire()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);

        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);

        Assert.Equal(0, fired[0]);
    }

    [Fact]
    public void SecondTap_BeyondWindow_DoesNotFire()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);

        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);
        _now = _now.AddMilliseconds(800);
        det.OnKeyEvent(42, true);

        Assert.Equal(0, fired[0]);
    }

    [Fact]
    public void NonConfiguredPosition_IsIgnored()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);

        det.OnKeyEvent(99, true);
        det.OnKeyEvent(99, false);
        det.OnKeyEvent(99, true);
        det.OnKeyEvent(99, false);

        Assert.Equal(0, fired[0]);
    }

    [Fact]
    public void SpuriousPressWithoutRelease_DoesNotCountAsSecondTap()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);

        det.OnKeyEvent(42, true);
        // No release in between — firmware retrigger edge case.
        det.OnKeyEvent(42, true);

        Assert.Equal(0, fired[0]);
    }

    [Fact]
    public void Rearms_AfterFiring()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);

        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);
        det.OnKeyEvent(42, true);
        Assert.Equal(1, fired[0]);
        det.OnKeyEvent(42, false);

        _now = _now.AddMilliseconds(200);
        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);
        det.OnKeyEvent(42, true);
        Assert.Equal(2, fired[0]);
    }

    [Fact]
    public void SetWindowMs_AdjustsWindow()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);
        det.SetWindowMs(1000);

        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);
        _now = _now.AddMilliseconds(800);
        det.OnKeyEvent(42, true);

        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void TripleTap_WhenConfigured()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);
        det.SetTapCount(3);

        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);
        det.OnKeyEvent(42, true);
        Assert.Equal(0, fired[0]);
        det.OnKeyEvent(42, false);
        det.OnKeyEvent(42, true);
        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void SetPosition_ResetsInFlightState()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);
        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);

        // Switch to a different watched position mid-sequence.
        det.SetPosition(99);

        det.OnKeyEvent(99, true);
        det.OnKeyEvent(99, false);
        det.OnKeyEvent(99, true);
        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void SetPosition_Null_Disables()
    {
        var det = NewDetector(out var fired);
        det.SetPosition(42);
        det.SetPosition(null);

        det.OnKeyEvent(42, true);
        det.OnKeyEvent(42, false);
        det.OnKeyEvent(42, true);

        Assert.Equal(0, fired[0]);
    }

    [Fact]
    public void SetWindowMs_Negative_Throws()
    {
        var det = new MultiTapDetector();
        Assert.Throws<ArgumentOutOfRangeException>(() => det.SetWindowMs(-1));
    }

    [Fact]
    public void SetTapCount_LessThanOne_Throws()
    {
        var det = new MultiTapDetector();
        Assert.Throws<ArgumentOutOfRangeException>(() => det.SetTapCount(0));
    }
}
