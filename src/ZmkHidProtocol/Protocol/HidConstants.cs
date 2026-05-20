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
    }

    public static class Inbound
    {
        public const byte GetDeviceInfo = 0xFD;
        public const byte GetConfigId = 0xFB;
        public const byte SetLayerState = 0xFC;
    }
}
