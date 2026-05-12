using Xunit;
using ZmkHidProtocol.Building;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Tests;

public class LayerStateTrackerTests
{
    [Fact]
    public void OnLayerState_UpdatesCurrentAndHighest()
    {
        var t = new LayerStateTracker();
        t.OnLayerState(0b00101);

        Assert.Equal(0b00101u, t.CurrentBitmask);
        Assert.Equal(2, t.HighestActiveLayer);
    }

    [Fact]
    public void OnLayerState_FiresStateChanged_OnlyOnChange()
    {
        var t = new LayerStateTracker();
        var hits = 0;
        t.StateChanged += _ => hits++;

        t.OnLayerState(0b1);
        t.OnLayerState(0b1); // no change
        t.OnLayerState(0b11);

        Assert.Equal(2, hits);
    }

    [Fact]
    public void SaveState_ThenChange_SavedRemains()
    {
        var t = new LayerStateTracker();
        t.OnLayerState(0b0011);
        t.SaveState();
        t.OnLayerState(0b1000);

        Assert.Equal(0b0011u, t.SavedBitmask);
        Assert.Equal(0b1000u, t.CurrentBitmask);
    }

    [Fact]
    public void ExpectAppState_MatchingResponse_FlagsAppControlled()
    {
        var t = new LayerStateTracker();
        t.ExpectAppState(0b0100); // firmware will OR in bit 0 → expects 0b0101

        t.OnLayerState(0b0101);

        Assert.True(t.IsAppControlled);
    }

    [Fact]
    public void ExpectAppState_NonMatchingResponse_ClearsAppControlled()
    {
        var t = new LayerStateTracker();
        t.ExpectAppState(0b0100);
        t.OnLayerState(0b1001); // user pressed something else

        Assert.False(t.IsAppControlled);
    }

    [Fact]
    public void IsAppControlled_StickyUntilNextChange()
    {
        var t = new LayerStateTracker();
        t.ExpectAppState(0b0100);
        t.OnLayerState(0b0101);
        Assert.True(t.IsAppControlled);

        // User adds a momentary layer
        t.OnLayerState(0b1101);
        Assert.False(t.IsAppControlled);
    }

    [Fact]
    public void OnReport_ParsesAndForwards()
    {
        var t = new LayerStateTracker();
        var report = new byte[HidConstants.ReportSize];
        report[0] = HidConstants.Outbound.LayerState;
        // bytes 6-9 little-endian
        report[6] = 0b0010_0000; // bit 5 set
        t.OnReport(report);

        Assert.Equal(5, t.HighestActiveLayer);
    }

    [Fact]
    public void OnReport_IgnoresNonLayerStateReports()
    {
        var t = new LayerStateTracker();
        var report = new byte[HidConstants.ReportSize];
        report[0] = HidConstants.Outbound.DeviceInfo; // not 0xFF

        t.OnReport(report);

        Assert.Equal(0u, t.CurrentBitmask);
    }

    [Fact]
    public void ClearSavedState_NullsSavedBitmask()
    {
        var t = new LayerStateTracker();
        t.OnLayerState(0b11);
        t.SaveState();
        t.ClearSavedState();

        Assert.Null(t.SavedBitmask);
    }
}
