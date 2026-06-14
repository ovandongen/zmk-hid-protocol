using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Well-known capability id strings, so the registry seed and every consumer
/// reference one constant instead of a scattered magic string.
/// </summary>
public static class CapabilityIds
{
    public const string PointingDpiSet = "core.pointing.dpi.set";
    public const string PointingDpiSetIndex = "core.pointing.dpi.setIndex";
    public const string PointingDragScrollSet = "core.pointing.dragScroll.set";
    public const string PointingSnipeSet = "core.pointing.snipe.set";

    public const string LayerSet = "core.layer.set";
    public const string LayerSetBase = "core.layer.setBase";
    public const string LayerActivate = "core.layer.activate";
    public const string LayerDeactivate = "core.layer.deactivate";

    public const string RgbSet = "core.rgb.set";
    public const string RgbSetKey = "core.rgb.setKey";

    // Telemetry (device→host).
    public const string LayerChanged = "core.layer.changed";
    public const string KeyEvent = "core.keyboard.key.event";
    public const string RgbChanged = "core.rgb.changed";
    public const string SignalFire = "signal.fire";
}

/// <summary>
/// The single source of truth for every <em>known</em> capability: one
/// <see cref="CapabilityDefinition"/> per id (see
/// <c>docs/capability-registry-plan.md</c>). The router's decode table, the
/// pointing value-kind classification, and the firmware-facing
/// <c>capability-registry.md</c> all derive from <see cref="All"/> — nothing is
/// hand-maintained twice. The wire itself stays open: an id absent here is still
/// discovered + inventoried (just inert), so forward-compatibility is preserved.
///
/// <para>v1 covers the 9 action capabilities the host originates and/or forwards
/// (4 pointing + 3 layer + 2 RGB). Telemetry rows (layer-state, key-event,
/// signal.fire) are deferred; the record shape already admits them.</para>
/// </summary>
public static class CapabilityRegistry
{
    private static readonly CapabilityDefinition[] _all =
    {
        // ── Pointing (uint32 LE; device-triggerable ⇒ routable) ──────────────
        new(CapabilityIds.PointingDpiSet,
            [CapabilityRole.Handles, CapabilityRole.Triggers], CapabilityTier.Core,
            HidConstants.PointingAction.DpiSet, PayloadShape.Uint32LE, ValueKind.Count, Confirm: false,
            "Set pointer CPI/DPI sensitivity (absolute count)."),
        new(CapabilityIds.PointingDpiSetIndex,
            [CapabilityRole.Handles, CapabilityRole.Triggers], CapabilityTier.Core,
            HidConstants.PointingAction.DpiSetIndex, PayloadShape.Uint32LE, ValueKind.Index, Confirm: false,
            "Select a DPI preset slot by zero-based index."),
        new(CapabilityIds.PointingDragScrollSet,
            [CapabilityRole.Handles, CapabilityRole.Triggers], CapabilityTier.FwSpecific,
            HidConstants.PointingAction.DragScrollSet, PayloadShape.Uint32LE, ValueKind.Toggle, Confirm: false,
            "Enable or disable drag-scroll mode (0/1)."),
        new(CapabilityIds.PointingSnipeSet,
            [CapabilityRole.Handles, CapabilityRole.Triggers], CapabilityTier.FwSpecific,
            HidConstants.PointingAction.SnipeSet, PayloadShape.Uint32LE, ValueKind.Toggle, Confirm: false,
            "Enable or disable snipe (precision) mode (0/1)."),

        // ── Layer (app-originate-only: no Triggers ⇒ not forwarded) ──────────
        new(CapabilityIds.LayerSet,
            [CapabilityRole.Handles], CapabilityTier.Core,
            HidConstants.Inbound.SetLayerState, PayloadShape.LayerSetBitmask, ValueKind.None, Confirm: false,
            "Set the active-layer bitmask (the app's auto-switch / mouse-layer push)."),
        new(CapabilityIds.LayerSetBase,
            [CapabilityRole.Handles], CapabilityTier.Core,
            HidConstants.Inbound.SetLayerBase, PayloadShape.LayerIndex, ValueKind.None, Confirm: false,
            "Set the base (default) layer."),
        new(CapabilityIds.LayerActivate,
            [CapabilityRole.Handles], CapabilityTier.Optional,
            HidConstants.Inbound.ActivateLayer, PayloadShape.LayerRefIndex, ValueKind.None, Confirm: true,
            "Activate a layer (momentary/stacked); confirm-acked by reference."),
        new(CapabilityIds.LayerDeactivate,
            [CapabilityRole.Handles], CapabilityTier.Optional,
            HidConstants.Inbound.DeactivateLayer, PayloadShape.LayerRefIndex, ValueKind.None, Confirm: true,
            "Deactivate a previously activated layer; confirm-acked by reference."),

        // ── RGB (device-triggerable ⇒ routable) ──────────────────────────────
        new(CapabilityIds.RgbSet,
            [CapabilityRole.Handles, CapabilityRole.Triggers], CapabilityTier.Optional,
            HidConstants.RgbAction.Set, PayloadShape.RgbSetMask, ValueKind.None, Confirm: false,
            "Set RGB underglow to an absolute state (mask-driven HSV + effect)."),
        new(CapabilityIds.RgbSetKey,
            [CapabilityRole.Handles, CapabilityRole.Triggers], CapabilityTier.Optional,
            HidConstants.RgbAction.SetKey, PayloadShape.RgbSetKey, ValueKind.None, Confirm: false,
            "Set a single key's RGB color (QMK per-key)."),

        // ── Telemetry (device→host notify; the host decodes, never originates) ─
        // Documented here so the registry is the complete capability map, not just
        // the action map. None are routable (no device-side Handles target).
        new(CapabilityIds.LayerChanged,
            [CapabilityRole.Notifies], CapabilityTier.Core,
            HidConstants.Outbound.LayerState, PayloadShape.LayerStateBitmask, ValueKind.None, Confirm: false,
            "Active-layer bitmask changed (the core layer-viz telemetry)."),
        new(CapabilityIds.KeyEvent,
            [CapabilityRole.Notifies], CapabilityTier.Optional,
            HidConstants.Outbound.KeyEvent, PayloadShape.KeyEvent, ValueKind.None, Confirm: false,
            "Per-key press/release by matrix position (live-highlighting telemetry)."),
        new(CapabilityIds.RgbChanged,
            [CapabilityRole.Notifies], CapabilityTier.Optional,
            HidConstants.RgbAction.Changed, PayloadShape.RgbState, ValueKind.None, Confirm: false,
            "RGB state changed (device→host receipt for core.rgb.set)."),
        // signal.fire: a device Triggers it; the HOST handles it (out of band via
        // the signal dispatcher), so there is no device-side Handles target — it is
        // Triggers-only and therefore not router-forwarded.
        new(CapabilityIds.SignalFire,
            [CapabilityRole.Triggers], CapabilityTier.Core,
            HidConstants.Signal.Fire, PayloadShape.SignalId, ValueKind.None, Confirm: false,
            "Opaque host-defined trigger id; meaning is host config, not on the wire."),
    };

    /// <summary>Every known capability definition, in canonical (doc) order.</summary>
    public static IReadOnlyList<CapabilityDefinition> All => _all;

    private static readonly Dictionary<string, CapabilityDefinition> _byId =
        _all.ToDictionary(d => d.Id, StringComparer.Ordinal);

    private static readonly Dictionary<byte, CapabilityDefinition> _byByte =
        _all.Where(d => d.WireByte is not null).ToDictionary(d => d.WireByte!.Value);

    /// <summary>The definition for an id, or null if the id is not in the registry (unknown/inert).</summary>
    public static CapabilityDefinition? Definition(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The definition for a wire byte, or null if no known capability uses it.</summary>
    public static CapabilityDefinition? Definition(byte wireByte) => _byByte.GetValueOrDefault(wireByte);

    /// <summary>
    /// Routable wire byte → capability id (the forwarding decode table): every
    /// definition that has a wire byte and a <see cref="CapabilityRole.Triggers"/>
    /// role. Pointing + RGB today; the layer actions are excluded (Handles-only).
    /// </summary>
    public static readonly IReadOnlyDictionary<byte, string> ActionIdByByte =
        _all.Where(d => d.IsRoutable).ToDictionary(d => d.WireByte!.Value, d => d.Id);

    /// <summary>
    /// Wire byte → <see cref="ValueKind"/> for the scalar (uint32) actions whose
    /// payload carries an interpreted scalar — the four pointing actions. Drives
    /// the originate-UI's control choice (checkbox vs numeric).
    /// </summary>
    public static readonly IReadOnlyDictionary<byte, ValueKind> ValueKindByByte =
        _all.Where(d => d.WireByte is not null && d.Value != ValueKind.None)
            .ToDictionary(d => d.WireByte!.Value, d => d.Value);
}
