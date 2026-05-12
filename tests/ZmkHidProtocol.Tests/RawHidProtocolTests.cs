using System.Buffers.Binary;
using System.Text;
using Xunit;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Pure parser coverage for the Raw HID firmware protocol. The I/O loop in
/// the platform sources is exercised live; here we verify the 32-byte report
/// decoding against the documented byte layout.
/// </summary>
public class RawHidProtocolTests
{
    private static byte[] LayerStateReport(uint activeBitmask)
    {
        var buf = new byte[32];
        buf[0] = HidConstants.Outbound.LayerState;
        buf[1] = 0x04;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(2, 4), 0x00000001); // default layer
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(6, 4), activeBitmask);
        return buf;
    }

    private static byte[] KeyEventReport(byte position, bool pressed)
    {
        var buf = new byte[32];
        buf[0] = HidConstants.Outbound.KeyEvent;
        buf[2] = position;
        buf[3] = (byte)(pressed ? 1 : 0);
        return buf;
    }

    private static byte[] DeviceInfoReport(byte protocolVersion, string name)
    {
        var buf = new byte[32];
        buf[0] = HidConstants.Outbound.DeviceInfo;
        buf[1] = protocolVersion;
        var bytes = Encoding.UTF8.GetBytes(name);
        bytes.CopyTo(buf, 2);
        return buf;
    }

    private static byte[] ConfigIdReport(string configId)
    {
        var buf = new byte[32];
        buf[0] = HidConstants.Outbound.ConfigId;
        var bytes = Encoding.UTF8.GetBytes(configId);
        bytes.CopyTo(buf, 1);
        return buf;
    }

    [Theory]
    [InlineData(0x00000001u, 0)]   // base layer only
    [InlineData(0x00000003u, 1)]   // base + L1
    [InlineData(0x00000009u, 3)]   // base + L3
    [InlineData(0x00008000u, 15)]  // L15 alone
    [InlineData(0x80000001u, 31)]  // L31 alone (with base)
    public void TryParseLayerState_ReturnsHighestSetBit(uint bitmask, int expected)
    {
        var report = LayerStateReport(bitmask);
        Assert.Equal(expected, RawHidProtocol.TryParseLayerState(report));
    }

    [Fact]
    public void TryParseLayerState_BitmaskZero_ReturnsBase()
    {
        // Firmware can briefly emit 0x00000000 between transitions; treat as base.
        Assert.Equal(0, RawHidProtocol.TryParseLayerState(LayerStateReport(0)));
    }

    [Fact]
    public void TryParseLayerStateBitmask_ReturnsRawValue()
    {
        Assert.Equal(0x80000001u, RawHidProtocol.TryParseLayerStateBitmask(LayerStateReport(0x80000001u)));
    }

    [Fact]
    public void TryParseLayerState_WrongMessageType_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseLayerState(KeyEventReport(0, true)));
    }

    [Fact]
    public void TryParseLayerState_ShortBuffer_ReturnsNull()
    {
        var report = new byte[] { 0xFF, 0x04, 0x01, 0x00 };
        Assert.Null(RawHidProtocol.TryParseLayerState(report));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(59, true)]
    [InlineData(255, false)]
    public void TryParseKeyEvent_RoundTrips(byte position, bool pressed)
    {
        var ev = RawHidProtocol.TryParseKeyEvent(KeyEventReport(position, pressed));
        Assert.NotNull(ev);
        Assert.Equal(position, ev!.Value.Position);
        Assert.Equal(pressed, ev.Value.Pressed);
    }

    [Fact]
    public void TryParseKeyEvent_WrongMessageType_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseKeyEvent(LayerStateReport(0x00000001u)));
    }

    [Fact]
    public void TryParseKeyEvent_ShortBuffer_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseKeyEvent(new byte[] { 0xF1, 0x00 }));
    }

    [Theory]
    [InlineData(0x01, "Glove80")]
    [InlineData(0x01, "Go60")]
    [InlineData(0x07, "Long Name Up To 30 Bytes Max!!")]
    public void TryParseDeviceInfo_RoundTrips(byte version, string name)
    {
        var info = RawHidProtocol.TryParseDeviceInfo(DeviceInfoReport(version, name));
        Assert.NotNull(info);
        Assert.Equal(version, info!.ProtocolVersion);
        Assert.Equal(name, info.Name);
    }

    [Fact]
    public void TryParseDeviceInfo_EmptyName_ReturnsEmptyString()
    {
        var info = RawHidProtocol.TryParseDeviceInfo(DeviceInfoReport(0x01, ""));
        Assert.NotNull(info);
        Assert.Equal(string.Empty, info!.Name);
    }

    [Fact]
    public void TryParseDeviceInfo_WrongMessageType_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseDeviceInfo(KeyEventReport(0, true)));
    }

    [Fact]
    public void TryParseDeviceInfo_ShortBuffer_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseDeviceInfo(new byte[] { 0xFE, 0x01 }));
    }

    [Theory]
    [InlineData("glove80-v1")]
    [InlineData("")]
    [InlineData("go60-custom")]
    public void TryParseConfigId_RoundTrips(string configId)
    {
        Assert.Equal(configId, RawHidProtocol.TryParseConfigId(ConfigIdReport(configId)));
    }

    [Fact]
    public void TryParseConfigId_WrongMessageType_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseConfigId(KeyEventReport(0, true)));
    }

    [Fact]
    public void TryParseConfigId_ShortBuffer_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseConfigId(new byte[] { 0xFA }));
    }

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(0x00000001u, 0)]
    [InlineData(0x80000000u, 31)]
    [InlineData(0x40000001u, 30)]
    public void HighestActiveLayer_Cases(uint bitmask, int expected)
    {
        Assert.Equal(expected, RawHidProtocol.HighestActiveLayer(bitmask));
    }
}
