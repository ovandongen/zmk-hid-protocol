namespace ZmkHidProtocol.Transport;

/// <summary>
/// Default <see cref="IDeviceMatcher"/>: requires exact VID/PID equality and
/// a case-insensitive prefix match against one of <paramref name="NamePrefixes"/>.
///
/// <para>The suffix of <c>productName</c> often differs by transport (USB
/// commonly exposes "Go60 Left" because the cable side is the host; BLE
/// exposes "Go60" because only the central is visible), so prefix matching
/// is more robust than exact comparison. An empty <paramref name="NamePrefixes"/>
/// list disables the name check.</para>
/// </summary>
public sealed record DeviceMatcher(
    int Vid,
    int Pid,
    IReadOnlyList<string> NamePrefixes) : IDeviceMatcher
{
    public bool Matches(int vendorId, int productId, string? productName)
    {
        if (vendorId != Vid || productId != Pid) return false;
        return MatchesName(productName);
    }

    public bool MatchesName(string? productName)
    {
        if (NamePrefixes.Count == 0) return true;
        if (string.IsNullOrEmpty(productName)) return false;
        foreach (var prefix in NamePrefixes)
        {
            if (productName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
