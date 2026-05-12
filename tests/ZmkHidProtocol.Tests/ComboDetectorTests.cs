using Xunit;
using ZmkHidProtocol.Building;

namespace ZmkHidProtocol.Tests;

public class ComboDetectorTests
{
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private ComboDetector NewDetector(out int[] firedCount)
    {
        var count = new[] { 0 };
        var det = new ComboDetector(() => _now);
        det.ComboTriggered += () => count[0]++;
        firedCount = count;
        return det;
    }

    [Fact]
    public void SimultaneousPress_WithinTimeout_Fires()
    {
        var det = NewDetector(out var fired);
        det.SetComboPositions(new[] { 10, 20 });

        det.OnKeyEvent(10, true);
        _now = _now.AddMilliseconds(10);
        det.OnKeyEvent(20, true);

        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void PressSpread_BeyondTimeout_DoesNotFire()
    {
        var det = NewDetector(out var fired);
        det.SetComboPositions(new[] { 10, 20 });

        det.OnKeyEvent(10, true);
        _now = _now.AddMilliseconds(100);
        det.OnKeyEvent(20, true);

        Assert.Equal(0, fired[0]);
    }

    [Fact]
    public void NonComboKey_IsIgnored()
    {
        var det = NewDetector(out var fired);
        det.SetComboPositions(new[] { 10, 20 });

        det.OnKeyEvent(10, true);
        det.OnKeyEvent(99, true);
        det.OnKeyEvent(99, false);

        Assert.Equal(0, fired[0]);
        _now = _now.AddMilliseconds(10);
        det.OnKeyEvent(20, true);
        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void DoesNotFireTwice_WhileHeld()
    {
        var det = NewDetector(out var fired);
        det.SetComboPositions(new[] { 10, 20 });

        det.OnKeyEvent(10, true);
        det.OnKeyEvent(20, true);
        // Spurious repeat press (firmware can re-emit on retrigger scenarios)
        det.OnKeyEvent(10, true);

        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void Rearms_AfterAllKeysReleased()
    {
        var det = NewDetector(out var fired);
        det.SetComboPositions(new[] { 10, 20 });

        det.OnKeyEvent(10, true);
        det.OnKeyEvent(20, true);
        Assert.Equal(1, fired[0]);

        det.OnKeyEvent(10, false);
        det.OnKeyEvent(20, false);

        det.OnKeyEvent(10, true);
        det.OnKeyEvent(20, true);
        Assert.Equal(2, fired[0]);
    }

    [Fact]
    public void SetTimeoutMs_AdjustsWindow()
    {
        var det = NewDetector(out var fired);
        det.SetComboPositions(new[] { 10, 20 });
        det.SetTimeoutMs(200);

        det.OnKeyEvent(10, true);
        _now = _now.AddMilliseconds(150);
        det.OnKeyEvent(20, true);

        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void ThreeKeyCombo_AllRequired()
    {
        var det = NewDetector(out var fired);
        det.SetComboPositions(new[] { 10, 20, 30 });

        det.OnKeyEvent(10, true);
        det.OnKeyEvent(20, true);
        Assert.Equal(0, fired[0]);
        det.OnKeyEvent(30, true);
        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void SetComboPositions_ResetsInFlightState()
    {
        var det = NewDetector(out var fired);
        det.SetComboPositions(new[] { 10, 20 });
        det.OnKeyEvent(10, true);

        det.SetComboPositions(new[] { 30, 40 });
        det.OnKeyEvent(20, true); // not part of new combo
        Assert.Equal(0, fired[0]);

        det.OnKeyEvent(30, true);
        det.OnKeyEvent(40, true);
        Assert.Equal(1, fired[0]);
    }

    [Fact]
    public void SetTimeoutMs_Negative_Throws()
    {
        var det = new ComboDetector();
        Assert.Throws<ArgumentOutOfRangeException>(() => det.SetTimeoutMs(-1));
    }
}
