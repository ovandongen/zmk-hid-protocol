using Xunit;
using ZmkHidProtocol.Capabilities;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Golden test pinned to real 0xF8 manifest streams captured from hardware
/// running the modified firmware (captured 2026-06-08): a Ploopy Bean QMK
/// trackball and a Glove80 ZMK keyboard. Replaying the exact bytes through
/// <see cref="RawHidProtocol.TryParseManifestEntry"/> +
/// <see cref="ManifestAssembler"/> must reproduce each device's advertised
/// manifest. This pins the parser to actual firmware output rather than the
/// spec-as-read — including the two firmwares' differing continuation-report
/// conventions: the Bean repeats role/tier/confirm on the dragScroll
/// continuation, while the Glove80 zeroes them on the configId continuation.
/// Both must reassemble identically because the assembler trusts the first
/// report of each sequence.
/// </summary>
public class ManifestGoldenCaptureTests
{
    private static byte[] Hex(string hex)
    {
        var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var buf = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            buf[i] = Convert.ToByte(parts[i], 16);
        return buf;
    }

    private static DeviceManifest Assemble(string[] rawReports)
    {
        var asm = new ManifestAssembler();
        foreach (var hex in rawReports)
        {
            // Non-0xF8 reports (none in these captures, but possible live) parse
            // as null and are skipped — exactly how the live reader behaves.
            var entry = RawHidProtocol.TryParseManifestEntry(Hex(hex));
            if (entry is not null) asm.Add(entry);
        }
        Assert.True(asm.IsComplete, "stream must terminate with the LAST flag");
        return asm.Build();
    }

    private static void AssertCap(Capability c, CapabilityRole role, CapabilityTier tier, bool confirm, string id)
    {
        Assert.Equal(id, c.Id);
        Assert.Equal(role, c.Role);
        Assert.Equal(tier, c.Tier);
        Assert.Equal(confirm, c.Confirm);
    }

    // ---- Ploopy Bean (QMK trackball) — captured from hardware ----
    private static readonly string[] BeanReports =
    {
        "F8 00 00 00 00 00 42 65 61 6E 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        "F8 01 00 02 00 00 63 6F 72 65 2E 70 6F 69 6E 74 69 6E 67 2E 64 70 69 2E 73 65 74 00 00 00 00 00",
        "F8 02 00 02 00 00 63 6F 72 65 2E 70 6F 69 6E 74 69 6E 67 2E 64 70 69 2E 73 65 74 49 6E 64 65 78",
        "F8 03 00 02 01 00 63 6F 72 65 2E 70 6F 69 6E 74 69 6E 67 2E 73 6E 69 70 65 2E 73 65 74 00 00 00",
        // seq 4 spans two reports; the continuation repeats role/tier/confirm (02 01 00).
        "F8 04 02 02 01 00 63 6F 72 65 2E 70 6F 69 6E 74 69 6E 67 2E 64 72 61 67 53 63 72 6F 6C 6C 2E 73",
        "F8 04 01 02 01 00 65 74 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
    };

    [Fact]
    public void Bean_TrackballManifest_Reassembles()
    {
        var m = Assemble(BeanReports);

        Assert.Equal("Bean", m.Name);
        Assert.Null(m.ConfigId);
        Assert.Collection(m.Capabilities.OrderBy(c => c.Id, StringComparer.Ordinal),
            c => AssertCap(c, CapabilityRole.Handles, CapabilityTier.Core, false, "core.pointing.dpi.set"),
            c => AssertCap(c, CapabilityRole.Handles, CapabilityTier.Core, false, "core.pointing.dpi.setIndex"),
            c => AssertCap(c, CapabilityRole.Handles, CapabilityTier.FwSpecific, false, "core.pointing.dragScroll.set"),
            c => AssertCap(c, CapabilityRole.Handles, CapabilityTier.FwSpecific, false, "core.pointing.snipe.set"));
    }

    // ---- Glove80 Left (ZMK keyboard) — captured from hardware ----
    private static readonly string[] Glove80Reports =
    {
        "F8 00 00 00 00 00 47 6C 6F 76 65 38 30 20 4C 65 66 74 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        // seq 1 (configId GUID) spans two reports; the continuation zeroes role/tier (00 00).
        "F8 01 02 00 01 00 31 32 33 65 34 35 36 37 2D 65 38 39 62 2D 31 32 64 33 2D 61 34 35 36 2D 34 32",
        "F8 01 00 00 00 00 36 36 31 34 31 37 34 30 30 30 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        "F8 02 00 01 03 00 63 6F 72 65 2E 6B 65 79 62 6F 61 72 64 2E 6B 65 79 2E 65 76 65 6E 74 00 00 00",
        "F8 03 00 02 03 01 63 6F 72 65 2E 6C 61 79 65 72 2E 61 63 74 69 76 61 74 65 00 00 00 00 00 00 00",
        "F8 04 00 01 00 00 63 6F 72 65 2E 6C 61 79 65 72 2E 63 68 61 6E 67 65 64 00 00 00 00 00 00 00 00",
        "F8 05 00 02 03 01 63 6F 72 65 2E 6C 61 79 65 72 2E 64 65 61 63 74 69 76 61 74 65 00 00 00 00 00",
        "F8 06 00 02 00 00 63 6F 72 65 2E 6C 61 79 65 72 2E 73 65 74 00 00 00 00 00 00 00 00 00 00 00 00",
        "F8 07 00 02 00 00 63 6F 72 65 2E 6C 61 79 65 72 2E 73 65 74 42 61 73 65 00 00 00 00 00 00 00 00",
        "F8 08 00 03 00 00 63 6F 72 65 2E 70 6F 69 6E 74 69 6E 67 2E 64 70 69 2E 73 65 74 00 00 00 00 00",
        "F8 09 01 03 01 00 63 6F 72 65 2E 70 6F 69 6E 74 69 6E 67 2E 73 6E 69 70 65 2E 73 65 74 00 00 00",
    };

    [Fact]
    public void Glove80_KeyboardManifest_Reassembles()
    {
        var m = Assemble(Glove80Reports);

        Assert.Equal("Glove80 Left", m.Name);
        Assert.Equal("123e4567-e89b-12d3-a456-426614174000", m.ConfigId);
        Assert.Collection(m.Capabilities.OrderBy(c => c.Id, StringComparer.Ordinal),
            c => AssertCap(c, CapabilityRole.Notifies, CapabilityTier.Optional, false, "core.keyboard.key.event"),
            c => AssertCap(c, CapabilityRole.Handles, CapabilityTier.Optional, true, "core.layer.activate"),
            c => AssertCap(c, CapabilityRole.Notifies, CapabilityTier.Core, false, "core.layer.changed"),
            c => AssertCap(c, CapabilityRole.Handles, CapabilityTier.Optional, true, "core.layer.deactivate"),
            c => AssertCap(c, CapabilityRole.Handles, CapabilityTier.Core, false, "core.layer.set"),
            c => AssertCap(c, CapabilityRole.Handles, CapabilityTier.Core, false, "core.layer.setBase"),
            c => AssertCap(c, CapabilityRole.Triggers, CapabilityTier.Core, false, "core.pointing.dpi.set"),
            c => AssertCap(c, CapabilityRole.Triggers, CapabilityTier.FwSpecific, false, "core.pointing.snipe.set"));
    }

    [Fact]
    public void Glove80TriggersAreFulfilledByBeanHandles()
    {
        // The whole point of routing: the keyboard's triggers reference handled
        // ids on the trackball. Verify the captured manifests actually overlap.
        var kbd = Assemble(Glove80Reports);
        var ball = Assemble(BeanReports);

        var triggers = kbd.Capabilities.Where(c => c.Role == CapabilityRole.Triggers).Select(c => c.Id).ToHashSet();
        var handles = ball.Capabilities.Where(c => c.Role == CapabilityRole.Handles).Select(c => c.Id).ToHashSet();

        // dpi.set + snipe.set are triggered by the Glove80 and handled by the Bean.
        Assert.Contains("core.pointing.dpi.set", triggers);
        Assert.Contains("core.pointing.snipe.set", triggers);
        Assert.True(triggers.IsSubsetOf(handles),
            "every Glove80 trigger should have a matching Bean handler in this capture");
    }
}
