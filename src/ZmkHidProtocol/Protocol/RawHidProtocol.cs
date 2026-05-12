using System.Buffers.Binary;
using System.Text;

namespace ZmkHidProtocol.Protocol;

/// <summary>
/// Pure parser for the 32-byte Raw HID reports emitted by the
/// <a href="https://github.com/ovandongen/zmk-hid-viz">zmk-hid-viz</a> firmware
/// module. Kept I/O-free so it can be exercised by unit tests with synthetic
/// buffers.
/// </summary>
public static class RawHidProtocol
{
    /// <summary>
    /// Parses the active-layer bitmask (uint32 LE at bytes 6-9) from a 0xFF
    /// report. Returns null if the buffer isn't a layer-state report.
    /// </summary>
    public static uint? TryParseLayerStateBitmask(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 10) return null;
        if (payload[0] != HidConstants.Outbound.LayerState) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(payload[6..10]);
    }

    /// <summary>
    /// Returns the highest active layer index from a layer-state report, or
    /// null if the buffer isn't one. A bitmask of 0 (no layers active,
    /// including the base) is reported as layer 0 to match the firmware
    /// invariant that layer 0 is always logically present.
    /// </summary>
    public static int? TryParseLayerState(ReadOnlySpan<byte> payload)
    {
        var bitmask = TryParseLayerStateBitmask(payload);
        if (bitmask is null) return null;
        return HighestActiveLayer(bitmask.Value);
    }

    public static int HighestActiveLayer(uint bitmask)
    {
        if (bitmask == 0) return 0;
        for (int i = 31; i >= 0; i--)
            if ((bitmask & (1u << i)) != 0) return i;
        return 0;
    }

    /// <summary>
    /// Returns (matrix position, pressed flag) for a 0xF1 key-event report,
    /// or null if the buffer isn't one.
    /// </summary>
    public static (int Position, bool Pressed)? TryParseKeyEvent(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4) return null;
        if (payload[0] != HidConstants.Outbound.KeyEvent) return null;
        return (payload[2], payload[3] == 0x01);
    }

    /// <summary>
    /// Parses a 0xFE device-info response. Byte 1 is the firmware protocol
    /// version; bytes 2+ are the keyboard name (zero-padded to the report
    /// size, treated as null-terminated UTF-8).
    /// </summary>
    public static DeviceInfo? TryParseDeviceInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3) return null;
        if (payload[0] != HidConstants.Outbound.DeviceInfo) return null;
        var name = ReadNullTerminatedString(payload[2..]);
        return new DeviceInfo(payload[1], name);
    }

    /// <summary>
    /// Parses a 0xFA config-ID response. Bytes 1+ are the config ID
    /// (zero-padded, treated as null-terminated UTF-8). May be empty if the
    /// keyboard has no <c>CONFIG_HID_VIZ_CONFIG_ID</c> set.
    /// </summary>
    public static string? TryParseConfigId(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2) return null;
        if (payload[0] != HidConstants.Outbound.ConfigId) return null;
        return ReadNullTerminatedString(payload[1..]);
    }

    private static string ReadNullTerminatedString(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        var slice = end < 0 ? bytes : bytes[..end];
        return Encoding.UTF8.GetString(slice);
    }
}
