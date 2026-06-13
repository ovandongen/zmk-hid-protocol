namespace ZmkHidProtocol.Protocol;

/// <summary>
/// Wire-level constants for the zmk-hid-viz firmware module. Single source of
/// truth for opcodes, BLE GATT UUIDs, HID usage page / ID, and report size.
/// </summary>
public static class HidConstants
{
    public const int UsagePage = 0xFF60;
    public const int UsageId = 0x61;
    public const int ReportSize = 32;

    public static class Outbound
    {
        public const byte LayerState = 0xFF;
        public const byte KeyEvent = 0xF1;
        public const byte DeviceInfo = 0xFE;
        public const byte ConfigId = 0xFA;

        /// <summary>One capability per report in response to <see cref="Inbound.GetManifest"/>.</summary>
        public const byte ManifestEntry = 0xF8;

        /// <summary>Acknowledgement of a confirm-flagged handled action (carries the echoed ref).</summary>
        public const byte Confirm = 0xF7;
    }

    public static class Inbound
    {
        public const byte GetDeviceInfo = 0xFD;
        public const byte GetConfigId = 0xFB;
        public const byte SetLayerState = 0xFC;

        /// <summary>Request the device's capability manifest (optional start index at byte 1).</summary>
        public const byte GetManifest = 0xF9;

        /// <summary>Set the default/"to" layer (absolute, no confirm). Byte 1 = layer index.</summary>
        public const byte SetLayerBase = 0xF4;

        /// <summary>Activate one layer (relative, confirm-flagged). Bytes 1-2 = ref LE, byte 3 = layer index.</summary>
        public const byte ActivateLayer = 0xF3;

        /// <summary>Deactivate one layer (relative, confirm-flagged). Bytes 1-2 = ref LE, byte 3 = layer index.</summary>
        public const byte DeactivateLayer = 0xF2;
    }

    /// <summary>
    /// Routable pointing-action wire bytes. The emit byte (a device that
    /// <c>triggers</c> the action) equals the handle byte (a device that
    /// <c>handles</c> it), so the host forwards such a report verbatim — no
    /// re-encoding. Direction is therefore role-dependent, which is why these
    /// sit outside <see cref="Outbound"/>/<see cref="Inbound"/>. Payload is a
    /// uint32 LE value at bytes 1-4.
    /// </summary>
    public static class PointingAction
    {
        public const byte DpiSet = 0xE1;
        public const byte DpiSetIndex = 0xE2;
        public const byte DragScrollSet = 0xE9;
        public const byte SnipeSet = 0xEB;
    }

    /// <summary>
    /// RGB capability wire bytes (<c>core.rgb.*</c>). The only portable surface
    /// is global on/off + HSB + effect, which exists on stock ZMK underglow and
    /// both QMK lighting systems; per-key (<see cref="SetKey"/>) is QMK-only.
    /// <para><see cref="Set"/>/<see cref="SetKey"/> are routable <c>handles</c>
    /// actions: the emit byte equals the handle byte, so the host forwards them
    /// verbatim (same model as <see cref="PointingAction"/>). <see cref="Changed"/>
    /// is a device→host <c>notifies</c> report — never forwarded; the host
    /// decodes it via <see cref="RawHidProtocol.TryParseRgbChanged"/> to track the
    /// latched state. HSB ranges follow ZMK: hue 0–359 (uint16 LE), sat/val
    /// 0–100; effect is an index into the device's effect list.</para>
    /// </summary>
    public static class RgbAction
    {
        /// <summary>notifies · latched: <c>[0xD0, on, hue(u16 LE), sat, val, effect]</c>.</summary>
        public const byte Changed = 0xD0;

        /// <summary>handles · absolute/idempotent: <c>[0xD1, mask, on, hue(u16 LE), sat, val, effect]</c>.
        /// Only fields whose <see cref="SetMask"/> bit is set are applied.</summary>
        public const byte Set = 0xD1;

        /// <summary>handles · QMK-only: <c>[0xD2, index, hue(u16 LE), sat, val]</c>.</summary>
        public const byte SetKey = 0xD2;

        /// <summary>Presence bits for the <see cref="Set"/> field mask (byte 1).</summary>
        public static class SetMask
        {
            public const byte On = 1 << 0;
            public const byte Hue = 1 << 1;
            public const byte Sat = 1 << 2;
            public const byte Val = 1 << 3;
            public const byte Effect = 1 << 4;
        }

        public const ushort HueMax = 359;
        public const byte SatMax = 100;
        public const byte ValMax = 100;
    }
}
