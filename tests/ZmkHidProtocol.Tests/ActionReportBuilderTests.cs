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
