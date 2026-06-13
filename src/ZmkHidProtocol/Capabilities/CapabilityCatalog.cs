using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Canonical map between routable action wire bytes and their capability ids.
/// The manifest transmits the id (a string), not the wire byte, so the host
/// owns this mapping: it is the single source the router decodes triggers with
/// and the builder encodes them with. Mirrors the firmware's emit table and the
/// reference host's <c>ACTION_ID_BY_BYTE</c>.
/// </summary>
public static class CapabilityCatalog
{
    /// <summary>
    /// Routable pointing actions: wire byte → capability id. uint32 LE payload,
    /// classified by <see cref="ValueKindByByte"/> for the originate-UI.
    /// </summary>
    private static readonly Dictionary<byte, string> PointingActionIdByByte = new()
    {
        [HidConstants.PointingAction.DpiSet] = "core.pointing.dpi.set",
        [HidConstants.PointingAction.DpiSetIndex] = "core.pointing.dpi.setIndex",
        [HidConstants.PointingAction.DragScrollSet] = "core.pointing.dragScroll.set",
        [HidConstants.PointingAction.SnipeSet] = "core.pointing.snipe.set",
    };

    /// <summary>
    /// Routable RGB actions: wire byte → capability id. These are <c>handles</c>
    /// actions forwarded verbatim like pointing. <c>core.rgb.changed</c> (0xD0) is
    /// a device→host notify, NOT routable, so it is intentionally absent — the
    /// host decodes it via <see cref="RawHidProtocol.TryParseRgbChanged"/>.
    /// </summary>
    private static readonly Dictionary<byte, string> RgbActionIdByByte = new()
    {
        [HidConstants.RgbAction.Set] = "core.rgb.set",
        [HidConstants.RgbAction.SetKey] = "core.rgb.setKey",
    };

    /// <summary>
    /// Every routable action wire byte → capability id (pointing + RGB). The
    /// single source the router decodes triggers with and forwards them by; the
    /// forwarding hot path drops any first byte absent here. Mirrors the
    /// firmware's emit table and the reference host's <c>ACTION_ID_BY_BYTE</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<byte, string> ActionIdByByte =
        PointingActionIdByByte
            .Concat(RgbActionIdByByte)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// How the uint32 LE payload of a routable pointing action is interpreted.
    /// The wire format is uniform (a uint32 at bytes 1-4, see
    /// <see cref="HidConstants.PointingAction"/>), but the meaningful domain
    /// differs per action. A host that <em>originates</em> an action uses this to
    /// render an apt control (checkbox vs numeric field) and to bound the value it
    /// sends; routing never needs it (forwarded triggers are relayed verbatim).
    /// </summary>
    public enum PointingValueKind
    {
        /// <summary>On/off mode flag carried as 0 (off) or 1 (on); firmware treats
        /// any nonzero as on. Used by snipe and drag-scroll.</summary>
        Toggle,

        /// <summary>Absolute CPI/DPI sensitivity, e.g. 400 / 800 / 1600. Used by
        /// <c>core.pointing.dpi.set</c>.</summary>
        Count,

        /// <summary>Zero-based slot into the device's configured DPI preset list.
        /// Used by <c>core.pointing.dpi.setIndex</c>.</summary>
        Index,
    }

    /// <summary>
    /// Per-action interpretation of the uint32 payload, for the four pointing
    /// actions only (the RGB actions in <see cref="ActionIdByByte"/> carry a
    /// structured HSB payload, not a uint32, so they are deliberately absent):
    /// snipe and drag-scroll are on/off modes, DPI-set is an absolute count,
    /// DPI-set-index is a preset slot. Its keys are exactly the pointing subset
    /// of <see cref="ActionIdByByte"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<byte, PointingValueKind> ValueKindByByte =
        new Dictionary<byte, PointingValueKind>
        {
            [HidConstants.PointingAction.DpiSet] = PointingValueKind.Count,
            [HidConstants.PointingAction.DpiSetIndex] = PointingValueKind.Index,
            [HidConstants.PointingAction.DragScrollSet] = PointingValueKind.Toggle,
            [HidConstants.PointingAction.SnipeSet] = PointingValueKind.Toggle,
        };

    /// <summary>True if <paramref name="wireByte"/> is any routable action (pointing or RGB).</summary>
    public static bool IsRoutableAction(byte wireByte) => ActionIdByByte.ContainsKey(wireByte);

    /// <summary>True if <paramref name="wireByte"/> is a routable pointing action
    /// (uint32 payload). Distinguishes the pointing subset from RGB so the
    /// pointing-only <see cref="ActionReportBuilder.PointingAction"/> can reject
    /// an RGB byte even though both are routable.</summary>
    public static bool IsPointingAction(byte wireByte) => PointingActionIdByByte.ContainsKey(wireByte);
}
