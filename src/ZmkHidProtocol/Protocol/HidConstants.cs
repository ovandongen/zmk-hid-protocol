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
}
