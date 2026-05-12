namespace ZmkHidProtocol.Protocol;

/// <summary>
/// Response payload of the 0xFE message: firmware protocol version and the
/// keyboard's <c>CONFIG_ZMK_KEYBOARD_NAME</c>.
/// </summary>
public sealed record DeviceInfo(byte ProtocolVersion, string Name);
