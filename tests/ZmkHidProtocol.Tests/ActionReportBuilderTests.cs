using System.Buffers.Binary;
using Xunit;
using ZmkHidProtocol.Capabilities;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Exact byte-layout coverage for <see cref="ActionReportBuilder"/>: opcode
/// placement, little-endian payloads, zero-padded tails, and the routable-action
/// guard. Layouts are pinned to the reference host (hid_viz_test.py /
/// hid_viz_hub.py).
/// </summary>
public class ActionReportBuilderTests
{
    [Fact]
    public void PointingAction_EncodesActionByteAndLittleEndianValue()
    {
        var report = ActionReportBuilder.PointingAction(HidConstants.PointingAction.DpiSet, 800);

        Assert.Equal(HidConstants.ReportSize, report.Length);
        Assert.Equal(HidConstants.PointingAction.DpiSet, report[0]);
        // 800 == 0x0320 -> little-endian
        Assert.Equal(0x20, report[1]);
        Assert.Equal(0x03, report[2]);
        Assert.Equal(0x00, report[3]);
        Assert.Equal(0x00, report[4]);
    }

    [Fact]
    public void PointingAction_RoundTripsValueAndZeroPadsTail()
    {
        const uint value = 0x12345678;
        var report = ActionReportBuilder.PointingAction(HidConstants.PointingAction.SnipeSet, value);

        Assert.Equal(value, BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(1, 4)));
        for (int i = 5; i < report.Length; i++)
            Assert.Equal(0, report[i]);
    }

    [Theory]
    [InlineData(0xE1)]
    [InlineData(0xE2)]
    [InlineData(0xE9)]
    [InlineData(0xEB)]
    public void PointingAction_AcceptsEveryRoutableActionByte(byte actionByte)
    {
        var report = ActionReportBuilder.PointingAction(actionByte, 1);
        Assert.Equal(actionByte, report[0]);
    }

    [Theory]
    [InlineData(0xFC)] // set-layer-state — host opcode, not a pointing action
    [InlineData(0xF4)] // setBase
    [InlineData(0x00)]
    public void PointingAction_RejectsNonRoutableByte(byte actionByte)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ActionReportBuilder.PointingAction(actionByte, 0));
    }

    [Fact]
    public void PointingAction_RejectsRgbByte_EvenThoughItIsRoutable()
    {
        // 0xD1 is a routable action (core.rgb.set), but not a pointing one — the
        // pointing builder must still reject it.
        Assert.True(CapabilityCatalog.IsRoutableAction(HidConstants.RgbAction.Set));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ActionReportBuilder.PointingAction(HidConstants.RgbAction.Set, 0));
    }

    [Fact]
    public void RgbSet_EncodesMaskAndFieldsAtFixedOffsets()
    {
        var report = ActionReportBuilder.RgbSet(
            new RgbSet(On: true, Hue: 300, Sat: 80, Val: 50, Effect: 3));

        Assert.Equal(HidConstants.ReportSize, report.Length);
        Assert.Equal(HidConstants.RgbAction.Set, report[0]);

        const byte expectedMask = HidConstants.RgbAction.SetMask.On
            | HidConstants.RgbAction.SetMask.Hue
            | HidConstants.RgbAction.SetMask.Sat
            | HidConstants.RgbAction.SetMask.Val
            | HidConstants.RgbAction.SetMask.Effect;
        Assert.Equal(expectedMask, report[1]);

        Assert.Equal(1, report[2]); // on
        Assert.Equal(300, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(3, 2)));
        Assert.Equal(80, report[5]);
        Assert.Equal(50, report[6]);
        Assert.Equal(3, report[7]);
        for (int i = 8; i < report.Length; i++)
            Assert.Equal(0, report[i]);
    }

    [Fact]
    public void RgbSet_OnlyFlagsAndWritesPresentFields()
    {
        // Set just the brightness — every other field stays unflagged and zero.
        var report = ActionReportBuilder.RgbSet(new RgbSet(Val: 25));

        Assert.Equal(HidConstants.RgbAction.SetMask.Val, report[1]);
        Assert.Equal(25, report[6]);
        Assert.Equal(0, report[2]); // on not flagged
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(3, 2))); // hue not flagged
        Assert.Equal(0, report[5]); // sat not flagged
        Assert.Equal(0, report[7]); // effect not flagged
    }

    [Fact]
    public void RgbSet_OffStateFlagsOnWithZeroPayload()
    {
        // On=false must still set the mask bit so the device applies the off state.
        var report = ActionReportBuilder.RgbSet(new RgbSet(On: false));

        Assert.Equal(HidConstants.RgbAction.SetMask.On, report[1]);
        Assert.Equal(0, report[2]);
    }

    [Fact]
    public void RgbSet_EmptyRequestHasZeroMask()
    {
        var report = ActionReportBuilder.RgbSet(new RgbSet());
        Assert.Equal(HidConstants.RgbAction.Set, report[0]);
        Assert.Equal(0, report[1]);
    }

    [Theory]
    [InlineData(360, 0, 0)]   // hue past 359
    [InlineData(0, 101, 0)]   // sat past 100
    [InlineData(0, 0, 101)]   // val past 100
    public void RgbSet_RejectsOutOfRangeHsb(int hue, int sat, int val)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ActionReportBuilder.RgbSet(new RgbSet(Hue: (ushort)hue, Sat: (byte)sat, Val: (byte)val)));

    [Fact]
    public void RgbSetKey_EncodesIndexAndHsb()
    {
        var report = ActionReportBuilder.RgbSetKey(index: 12, hue: 200, sat: 90, val: 75);

        Assert.Equal(HidConstants.ReportSize, report.Length);
        Assert.Equal(HidConstants.RgbAction.SetKey, report[0]);
        Assert.Equal(12, report[1]);
        Assert.Equal(200, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(2, 2)));
        Assert.Equal(90, report[4]);
        Assert.Equal(75, report[5]);
        for (int i = 6; i < report.Length; i++)
            Assert.Equal(0, report[i]);
    }

    [Fact]
    public void RgbSetKey_RejectsOutOfRangeHue()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ActionReportBuilder.RgbSetKey(index: 0, hue: 400, sat: 0, val: 0));

    [Fact]
    public void SetLayerBase_EncodesOpcodeAndLayerIndex()
    {
        var report = ActionReportBuilder.SetLayerBase(3);

        Assert.Equal(HidConstants.ReportSize, report.Length);
        Assert.Equal(HidConstants.Inbound.SetLayerBase, report[0]); // 0xF4
        Assert.Equal(3, report[1]);
        for (int i = 2; i < report.Length; i++)
            Assert.Equal(0, report[i]);
    }

    [Fact]
    public void ActivateLayer_EncodesOpcodeRefLittleEndianAndLayer()
    {
        var report = ActionReportBuilder.ActivateLayer(reference: 0x1234, layerIndex: 5);

        Assert.Equal(HidConstants.Inbound.ActivateLayer, report[0]); // 0xF3
        Assert.Equal(0x34, report[1]); // ref low byte
        Assert.Equal(0x12, report[2]); // ref high byte
        Assert.Equal(5, report[3]);    // layer index
        for (int i = 4; i < report.Length; i++)
            Assert.Equal(0, report[i]);
    }

    [Fact]
    public void DeactivateLayer_EncodesOpcodeRefLittleEndianAndLayer()
    {
        var report = ActionReportBuilder.DeactivateLayer(reference: 0xBEEF, layerIndex: 7);

        Assert.Equal(HidConstants.Inbound.DeactivateLayer, report[0]); // 0xF2
        Assert.Equal(0xEF, report[1]);
        Assert.Equal(0xBE, report[2]);
        Assert.Equal(7, report[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0xFFFF)]
    public void LayerRefActions_RoundTripRefBoundaries(ushort reference)
    {
        var report = ActionReportBuilder.ActivateLayer(reference, layerIndex: 0xFF);

        Assert.Equal(reference, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(1, 2)));
        Assert.Equal(0xFF, report[3]);
    }
}
