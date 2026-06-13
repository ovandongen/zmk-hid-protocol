using System.Buffers.Binary;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Builds the fixed-size host→device reports for capability actions LViz
/// originates: the routable pointing actions (DPI / drag-scroll / snipe) and the
/// layer actions (setBase / activate / deactivate). Pure byte encoding — the
/// counterpart on the decode side is <see cref="ManifestAssembler"/>. Every
/// report is exactly <see cref="HidConstants.ReportSize"/> bytes, zero-padded;
/// the transport prepends the hidapi report-id, mirroring
/// <see cref="Transport.CommandSender.SetLayerStateAsync"/>.
///
/// <para>Routing never calls this: a forwarded trigger is relayed verbatim
/// (emit byte == handle byte). These builders are for LViz acting as the
/// <em>originator</em> of an action — see the app's CapabilityControl.</para>
/// </summary>
public static class ActionReportBuilder
{
    /// <summary>
    /// Routable pointing action: <c>[action, value(uint32 LE)]</c>. The action
    /// byte must be a routable pointing action per
    /// <see cref="CapabilityCatalog.IsRoutableAction"/> (0xE1/0xE2/0xE9/0xEB);
    /// anything else is a caller bug and throws.
    /// </summary>
    public static byte[] PointingAction(byte actionByte, uint value)
    {
        if (!CapabilityCatalog.IsPointingAction(actionByte))
            throw new ArgumentOutOfRangeException(
                nameof(actionByte), $"0x{actionByte:X2} is not a routable pointing action.");

        var report = new byte[HidConstants.ReportSize];
        report[0] = actionByte;
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(1, 4), value);
        return report;
    }

    /// <summary>
    /// <c>core.rgb.set</c> (0xD1): <c>[0xD1, mask, on, hue(u16 LE), sat, val, effect]</c>.
    /// Absolute and idempotent — only the fields present on <paramref name="set"/>
    /// are flagged in the mask and applied; the rest are left untouched on-device.
    /// HSB values are range-checked (hue 0–359, sat/val 0–100); out of range throws.
    /// </summary>
    public static byte[] RgbSet(RgbSet set)
    {
        var report = new byte[HidConstants.ReportSize];
        report[0] = HidConstants.RgbAction.Set;

        byte mask = 0;
        if (set.On is { } on)
        {
            mask |= HidConstants.RgbAction.SetMask.On;
            report[2] = (byte)(on ? 1 : 0);
        }
        if (set.Hue is { } hue)
        {
            mask |= HidConstants.RgbAction.SetMask.Hue;
            BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(3, 2), CheckHue(hue));
        }
        if (set.Sat is { } sat)
        {
            mask |= HidConstants.RgbAction.SetMask.Sat;
            report[5] = CheckSat(sat);
        }
        if (set.Val is { } val)
        {
            mask |= HidConstants.RgbAction.SetMask.Val;
            report[6] = CheckVal(val);
        }
        if (set.Effect is { } effect)
        {
            mask |= HidConstants.RgbAction.SetMask.Effect;
            report[7] = effect;
        }

        report[1] = mask;
        return report;
    }

    /// <summary>
    /// <c>core.rgb.setKey</c> (0xD2): <c>[0xD2, index, hue(u16 LE), sat, val]</c>.
    /// QMK-only per-key colour. HSB values are range-checked; out of range throws.
    /// </summary>
    public static byte[] RgbSetKey(byte index, ushort hue, byte sat, byte val)
    {
        var report = new byte[HidConstants.ReportSize];
        report[0] = HidConstants.RgbAction.SetKey;
        report[1] = index;
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(2, 2), CheckHue(hue));
        report[4] = CheckSat(sat);
        report[5] = CheckVal(val);
        return report;
    }

    private static ushort CheckHue(ushort hue) => hue <= HidConstants.RgbAction.HueMax
        ? hue
        : throw new ArgumentOutOfRangeException(nameof(hue), hue, $"hue must be 0–{HidConstants.RgbAction.HueMax}.");

    private static byte CheckSat(byte sat) => sat <= HidConstants.RgbAction.SatMax
        ? sat
        : throw new ArgumentOutOfRangeException(nameof(sat), sat, $"sat must be 0–{HidConstants.RgbAction.SatMax}.");

    private static byte CheckVal(byte val) => val <= HidConstants.RgbAction.ValMax
        ? val
        : throw new ArgumentOutOfRangeException(nameof(val), val, $"val must be 0–{HidConstants.RgbAction.ValMax}.");

    /// <summary>setBase (0xF4): <c>[0xF4, layerIndex]</c>. Absolute, not confirm-flagged.</summary>
    public static byte[] SetLayerBase(byte layerIndex)
    {
        var report = new byte[HidConstants.ReportSize];
        report[0] = HidConstants.Inbound.SetLayerBase;
        report[1] = layerIndex;
        return report;
    }

    /// <summary>
    /// activate (0xF3): <c>[0xF3, ref(uint16 LE), layerIndex]</c>. Confirm-flagged —
    /// the device replies with a 0xF7 echoing <paramref name="reference"/>.
    /// </summary>
    public static byte[] ActivateLayer(ushort reference, byte layerIndex)
        => LayerRefAction(HidConstants.Inbound.ActivateLayer, reference, layerIndex);

    /// <summary>deactivate (0xF2): <c>[0xF2, ref(uint16 LE), layerIndex]</c>. Confirm-flagged.</summary>
    public static byte[] DeactivateLayer(ushort reference, byte layerIndex)
        => LayerRefAction(HidConstants.Inbound.DeactivateLayer, reference, layerIndex);

    private static byte[] LayerRefAction(byte opcode, ushort reference, byte layerIndex)
    {
        var report = new byte[HidConstants.ReportSize];
        report[0] = opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(1, 2), reference);
        report[3] = layerIndex;
        return report;
    }
}
