namespace ZmkHidProtocol.Transport;

/// <summary>
/// Decides whether a HID device (VID/PID/product name) belongs to the
/// keyboard a consumer wants to talk to. Many ZMK boards share the pid.codes
/// VID/PID (0x16C0 / 0x27DB), so product-name discrimination is mandatory.
/// </summary>
public interface IDeviceMatcher
{
    bool Matches(int vendorId, int productId, string? productName);

    /// <summary>
    /// Name-only match. Used by transports that cannot read VID/PID
    /// (notably Windows BLE — WinRT exposes the BLE device name but not
    /// the underlying USB-style IDs).
    /// </summary>
    bool MatchesName(string? productName);
}
