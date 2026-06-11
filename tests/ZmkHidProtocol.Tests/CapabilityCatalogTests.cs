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
    public void ValueKindByByte_CoversEveryRoutableAction()
    {
        // The two maps must stay in lockstep — a new action byte added to one
        // without the other is a host bug waiting to happen.
        Assert.Equal(
            CapabilityCatalog.ActionIdByByte.Keys.OrderBy(b => b),
            CapabilityCatalog.ValueKindByByte.Keys.OrderBy(b => b));
    }

    [Theory]
    [InlineData(HidConstants.PointingAction.SnipeSet)]
    [InlineData(HidConstants.PointingAction.DragScrollSet)]
    public void SnipeAndDragScroll_AreToggles(byte actionByte)
        => Assert.Equal(CapabilityCatalog.PointingValueKind.Toggle, CapabilityCatalog.ValueKindByByte[actionByte]);

    [Fact]
    public void DpiSet_IsCount()
        => Assert.Equal(CapabilityCatalog.PointingValueKind.Count,
            CapabilityCatalog.ValueKindByByte[HidConstants.PointingAction.DpiSet]);

    [Fact]
    public void DpiSetIndex_IsIndex()
        => Assert.Equal(CapabilityCatalog.PointingValueKind.Index,
            CapabilityCatalog.ValueKindByByte[HidConstants.PointingAction.DpiSetIndex]);
}
