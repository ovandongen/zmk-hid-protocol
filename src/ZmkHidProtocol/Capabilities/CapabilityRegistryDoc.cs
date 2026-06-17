using System.Text;

namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Renders the firmware-facing <c>capability-registry.md</c> from
/// <see cref="CapabilityRegistry"/>. The committed doc is the host half of the
/// cross-repo contract (a firmware implementor writing C shouldn't have to read
/// C#); a staleness test regenerates this and asserts the committed file matches,
/// so the doc can't silently drift from the code. See
/// <c>docs/capability-registry-plan.md</c>.
/// </summary>
public static class CapabilityRegistryDoc
{
    /// <summary>Path of the committed doc relative to the submodule root.</summary>
    public const string RelativePath = "docs/capability-registry.md";

    public static string Generate()
    {
        var sb = new StringBuilder();
        sb.Append(
            "# Capability registry (generated)\n\n" +
            "> Generated from `CapabilityRegistry` — **do not edit by hand**. The\n" +
            "> `CapabilityRegistryDocTests` staleness test regenerates this and fails if it\n" +
            "> drifts. This is the host half of the capability contract; firmware mirrors\n" +
            "> `(id ↔ byte ↔ payload)`. The wire stays open: an id absent here is still\n" +
            "> discovered and inventoried by a host, just inert until defined.\n\n" +
            "## Known capabilities\n\n" +
            "| Id | Roles | Tier | Byte | Payload | Value | Confirm | Description |\n" +
            "|---|---|---|---|---|---|---|---|\n");

        foreach (var d in CapabilityRegistry.All)
        {
            var roles = string.Join(", ", d.Roles);
            var wireByte = d.WireByte is { } b ? $"0x{b:X2}" : "—";
            var value = d.Value == ValueKind.None ? "—" : d.Value.ToString();
            var confirm = d.Confirm ? "yes" : "no";
            sb.Append($"| `{d.Id}` | {roles} | {d.Tier} | {wireByte} | {d.Payload} | {value} | {confirm} | {d.Description} |\n");
        }

        sb.Append("\n## Payload shapes\n\n")
          .Append("Byte layout after `[0] = action`:\n\n")
          .Append("| Shape | Layout |\n|---|---|\n");
        foreach (var shape in Enum.GetValues<PayloadShape>())
            sb.Append($"| {shape} | {LayoutOf(shape)} |\n");

        return sb.ToString();
    }

    // The firmware-critical part: the exact byte layout per shape. Kept here (not on
    // the enum) so the generated doc carries it; mirrors PayloadShape's XML docs and
    // ActionReportBuilder.
    private static string LayoutOf(PayloadShape shape) => shape switch
    {
        PayloadShape.None => "(no payload)",
        PayloadShape.Uint32LE => "`[1..4]` uint32 LE",
        PayloadShape.LayerIndex => "`[1]` layer index",
        PayloadShape.LayerRefIndex => "`[1..2]` ref uint16 LE, `[3]` layer index",
        PayloadShape.LayerSetBitmask => "`[1..4]` active-layer bitmask uint32 LE",
        PayloadShape.RgbSetMask => "`[1]` mask, `[2]` on, `[3..4]` hue uint16 LE, `[5]` sat, `[6]` val, `[7]` effect",
        PayloadShape.RgbSetKey => "`[1]` key index, `[2..3]` hue uint16 LE, `[4]` sat, `[5]` val",
        PayloadShape.LayerStateBitmask => "`[1]` format marker = 0x04, `[2..5]` default-layer bitmask uint32 LE (single bit), `[6..9]` active-layer bitmask uint32 LE",
        PayloadShape.KeyEvent => "`[2]` matrix position, `[3]` pressed (0/1)",
        PayloadShape.RgbState => "`[1]` on, `[2..3]` hue uint16 LE, `[4]` sat, `[5]` val, `[6]` effect",
        PayloadShape.SignalId => "`[1]` opaque id",
        _ => "(undocumented)",
    };
}
