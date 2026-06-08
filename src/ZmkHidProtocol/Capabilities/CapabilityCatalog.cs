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
    public static readonly IReadOnlyDictionary<byte, string> ActionIdByByte =
        new Dictionary<byte, string>
        {
            [HidConstants.PointingAction.DpiSet] = "core.pointing.dpi.set",
            [HidConstants.PointingAction.DpiSetIndex] = "core.pointing.dpi.setIndex",
            [HidConstants.PointingAction.DragScrollSet] = "core.pointing.dragScroll.set",
            [HidConstants.PointingAction.SnipeSet] = "core.pointing.snipe.set",
        };

    /// <summary>True if <paramref name="wireByte"/> is a routable pointing action.</summary>
    public static bool IsRoutableAction(byte wireByte) => ActionIdByByte.ContainsKey(wireByte);
}
