namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Reads the two manifest disclosure forms of signal.fire from a capability id
/// string (see signal-capability-spec.md §Manifest). Opaque "signal.fire"
/// advertises presence only; enumerated "signal.fire/&lt;id&gt;" additionally
/// reveals which ids a board fires. The /&lt;id&gt; suffix carries the id's
/// number, never its meaning.
/// </summary>
public static class SignalCapability
{
    public const string FireId = "signal.fire";
    private const string EnumeratedPrefix = "signal.fire/";

    /// <summary>True for "signal.fire" or any "signal.fire/&lt;id&gt;".</summary>
    public static bool IsFire(string capabilityId) =>
        capabilityId == FireId || capabilityId.StartsWith(EnumeratedPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The enumerated id from "signal.fire/&lt;id&gt;", or null for the opaque
    /// "signal.fire" form or any non-signal / malformed / out-of-range string.
    /// </summary>
    public static byte? TryParseEnumeratedId(string capabilityId)
    {
        if (!capabilityId.StartsWith(EnumeratedPrefix, StringComparison.Ordinal)) return null;
        var suffix = capabilityId.AsSpan(EnumeratedPrefix.Length);
        return byte.TryParse(suffix, out var id) ? id : null;
    }
}
