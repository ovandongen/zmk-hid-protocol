using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Host-facing facade over <see cref="CapabilityRegistry"/> for the routable-action
/// wire-byte queries the router and builder make. The literal byte→id and
/// byte→value-kind tables that used to live here are gone — they are now projections
/// of the one registry (single source of truth, see
/// <c>docs/capability-registry-plan.md</c>). These members are the stable call
/// surface; the data behind them is derived.
/// </summary>
public static class CapabilityCatalog
{
    /// <inheritdoc cref="CapabilityRegistry.ActionIdByByte"/>
    public static IReadOnlyDictionary<byte, string> ActionIdByByte => CapabilityRegistry.ActionIdByByte;

    /// <inheritdoc cref="CapabilityRegistry.ValueKindByByte"/>
    public static IReadOnlyDictionary<byte, ValueKind> ValueKindByByte => CapabilityRegistry.ValueKindByByte;

    /// <summary>True if <paramref name="wireByte"/> is any routable action (pointing or RGB).</summary>
    public static bool IsRoutableAction(byte wireByte) => CapabilityRegistry.ActionIdByByte.ContainsKey(wireByte);

    /// <summary>
    /// True if <paramref name="wireByte"/> is a routable pointing action (uint32
    /// payload). Distinguishes the pointing subset from RGB so the pointing-only
    /// <see cref="ActionReportBuilder.PointingAction"/> can reject an RGB byte even
    /// though both are routable. Derived: the pointing actions are exactly the
    /// <see cref="PayloadShape.Uint32LE"/> definitions.
    /// </summary>
    public static bool IsPointingAction(byte wireByte) =>
        CapabilityRegistry.Definition(wireByte) is { Payload: PayloadShape.Uint32LE };
}
