using Xunit;
using ZmkHidProtocol.Capabilities;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Invariants on the <see cref="CapabilityRegistry"/> — the single source of truth
/// hand-maintained tables can't enforce: unique wire bytes, confirm only on
/// ref-carrying shapes, non-empty roles, scalar-kind only on the scalar shape, and
/// that the derived projections reproduce the routable/value-kind sets.
/// </summary>
public class CapabilityRegistryTests
{
    [Fact]
    public void EveryDefinition_HasNonEmptyRolesAndId()
    {
        Assert.All(CapabilityRegistry.All, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Id));
            Assert.NotEmpty(d.Roles);
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
        });
    }

    [Fact]
    public void WireBytes_AreUnique()
    {
        var bytes = CapabilityRegistry.All.Where(d => d.WireByte is not null).Select(d => d.WireByte!.Value).ToList();
        Assert.Equal(bytes.Count, bytes.Distinct().Count());
    }

    [Fact]
    public void Ids_AreUnique()
    {
        var ids = CapabilityRegistry.All.Select(d => d.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Confirm_OnlyOnRefCarryingShape()
    {
        // Only LayerRefIndex carries the 0xF7 reference; a Confirm row on any other
        // shape has nothing to ack against.
        Assert.All(CapabilityRegistry.All.Where(d => d.Confirm),
            d => Assert.Equal(PayloadShape.LayerRefIndex, d.Payload));
    }

    [Fact]
    public void ScalarValueKind_OnlyOnUint32Shape()
    {
        // A non-None ValueKind is meaningful only for the scalar uint32 payload;
        // structured/empty payloads must be None.
        Assert.All(CapabilityRegistry.All, d =>
        {
            if (d.Value != ValueKind.None)
                Assert.Equal(PayloadShape.Uint32LE, d.Payload);
        });
    }

    [Fact]
    public void RoutableSet_IsExactlyPointingPlusRgb()
    {
        // Routable = has a wire byte AND can be Triggered by a device. Layer actions
        // are Handles-only, so they must NOT appear in the forwarding decode table.
        var routable = CapabilityRegistry.ActionIdByByte;
        Assert.Equal(new byte[]
        {
            HidConstants.RgbAction.Set,
            HidConstants.RgbAction.SetKey,
            HidConstants.PointingAction.DpiSet,
            HidConstants.PointingAction.DpiSetIndex,
            HidConstants.PointingAction.DragScrollSet,
            HidConstants.PointingAction.SnipeSet,
        }.OrderBy(b => b), routable.Keys.OrderBy(b => b));

        Assert.DoesNotContain(HidConstants.Inbound.SetLayerBase, routable.Keys);
        Assert.DoesNotContain(HidConstants.Inbound.ActivateLayer, routable.Keys);
        Assert.DoesNotContain(HidConstants.Inbound.DeactivateLayer, routable.Keys);
    }

    [Fact]
    public void ValueKindProjection_CoversExactlyThePointingActions()
    {
        Assert.Equal(new byte[]
        {
            HidConstants.PointingAction.DpiSet,
            HidConstants.PointingAction.DpiSetIndex,
            HidConstants.PointingAction.DragScrollSet,
            HidConstants.PointingAction.SnipeSet,
        }.OrderBy(b => b), CapabilityRegistry.ValueKindByByte.Keys.OrderBy(b => b));
    }

    [Fact]
    public void LayerSet_IsHandledBitmask_NotRoutableNorPointing()
    {
        // core.layer.set (0xFC) is the app's layer-bitmask push: Handles-only (no
        // device triggers it), so not forwarded; and its own shape so it never gets
        // treated as a pointing/scalar action.
        var d = CapabilityRegistry.Definition(HidConstants.Inbound.SetLayerState);
        Assert.NotNull(d);
        Assert.Equal(CapabilityIds.LayerSet, d!.Id);
        Assert.Equal(new[] { CapabilityRole.Handles }, d.Roles);
        Assert.Equal(PayloadShape.LayerSetBitmask, d.Payload);
        Assert.Equal(ValueKind.None, d.Value);
        Assert.False(d.IsRoutable);
        Assert.DoesNotContain(HidConstants.Inbound.SetLayerState, CapabilityRegistry.ActionIdByByte.Keys);
        Assert.DoesNotContain(HidConstants.Inbound.SetLayerState, CapabilityRegistry.ValueKindByByte.Keys);
    }

    [Fact]
    public void SignalFire_IsTriggersOnly_AndNotRoutable()
    {
        // signal.fire is device-triggered but HOST-handled (out of band), so it has
        // no device Handles target and must stay out of the forwarding table.
        var sig = CapabilityRegistry.Definition(HidConstants.Signal.Fire);
        Assert.NotNull(sig);
        Assert.Equal(CapabilityIds.SignalFire, sig!.Id);
        Assert.Equal(new[] { CapabilityRole.Triggers }, sig.Roles);
        Assert.False(sig.IsRoutable);
        Assert.DoesNotContain(HidConstants.Signal.Fire, CapabilityRegistry.ActionIdByByte.Keys);
    }

    [Theory]
    [InlineData(HidConstants.Outbound.LayerState)]   // core.layer.changed
    [InlineData(HidConstants.Outbound.KeyEvent)]     // core.keyboard.key.event
    [InlineData(HidConstants.RgbAction.Changed)]     // core.rgb.changed
    public void TelemetryNotifies_AreDecodableButNotRoutable(byte wireByte)
    {
        var d = CapabilityRegistry.Definition(wireByte);
        Assert.NotNull(d);
        Assert.Equal(new[] { CapabilityRole.Notifies }, d!.Roles);
        Assert.False(d.IsRoutable);
        Assert.DoesNotContain(wireByte, CapabilityRegistry.ActionIdByByte.Keys);
    }

    [Fact]
    public void LookupByByteAndId_RoundTrip()
    {
        foreach (var d in CapabilityRegistry.All)
        {
            Assert.Same(d, CapabilityRegistry.Definition(d.Id));
            if (d.WireByte is { } b) Assert.Same(d, CapabilityRegistry.Definition(b));
        }
        Assert.Null(CapabilityRegistry.Definition("nope.unknown.id"));
        Assert.Null(CapabilityRegistry.Definition((byte)0x00));
    }
}
