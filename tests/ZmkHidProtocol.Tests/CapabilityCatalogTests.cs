using Xunit;
using ZmkHidProtocol.Capabilities;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Pins the routable-action catalog: the id map, the routable-byte guard, and
/// the per-action value-kind classification a host uses to render its control.
/// </summary>
public class CapabilityCatalogTests
{
    [Fact]
    public void ValueKindByByte_CoversExactlyThePointingActions()
    {
        // ValueKindByByte classifies the pointing subset only (RGB carries a
        // structured HSB payload, not a uint32). Its keys must equal the pointing
        // actions and be a subset of every routable action — a pointing byte added
        // to one map without the other is a host bug waiting to happen.
        var pointing = new[]
        {
            HidConstants.PointingAction.DpiSet,
            HidConstants.PointingAction.DpiSetIndex,
            HidConstants.PointingAction.DragScrollSet,
            HidConstants.PointingAction.SnipeSet,
        };
        Assert.Equal(pointing.OrderBy(b => b), CapabilityCatalog.ValueKindByByte.Keys.OrderBy(b => b));
        Assert.All(CapabilityCatalog.ValueKindByByte.Keys,
            b => Assert.Contains(b, CapabilityCatalog.ActionIdByByte.Keys));
    }

    [Theory]
    [InlineData(HidConstants.RgbAction.Set, "core.rgb.set")]
    [InlineData(HidConstants.RgbAction.SetKey, "core.rgb.setKey")]
    public void RgbActions_AreRoutableButNotPointing(byte wireByte, string id)
    {
        Assert.True(CapabilityCatalog.IsRoutableAction(wireByte));
        Assert.False(CapabilityCatalog.IsPointingAction(wireByte));
        Assert.Equal(id, CapabilityCatalog.ActionIdByByte[wireByte]);
        // The notify (changed) byte is never routable — the host consumes it.
        Assert.DoesNotContain(HidConstants.RgbAction.Changed, CapabilityCatalog.ActionIdByByte.Keys);
    }

    [Theory]
    [InlineData(HidConstants.PointingAction.DpiSet)]
    [InlineData(HidConstants.PointingAction.SnipeSet)]
    public void PointingActions_AreBothRoutableAndPointing(byte wireByte)
    {
        Assert.True(CapabilityCatalog.IsRoutableAction(wireByte));
        Assert.True(CapabilityCatalog.IsPointingAction(wireByte));
    }

    [Theory]
    [InlineData(HidConstants.PointingAction.SnipeSet)]
    [InlineData(HidConstants.PointingAction.DragScrollSet)]
    public void SnipeAndDragScroll_AreToggles(byte actionByte)
        => Assert.Equal(ValueKind.Toggle, CapabilityCatalog.ValueKindByByte[actionByte]);

    [Fact]
    public void DpiSet_IsCount()
        => Assert.Equal(ValueKind.Count,
            CapabilityCatalog.ValueKindByByte[HidConstants.PointingAction.DpiSet]);

    [Fact]
    public void DpiSetIndex_IsIndex()
        => Assert.Equal(ValueKind.Index,
            CapabilityCatalog.ValueKindByByte[HidConstants.PointingAction.DpiSetIndex]);
}
