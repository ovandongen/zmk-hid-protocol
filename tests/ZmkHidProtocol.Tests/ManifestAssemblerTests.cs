using System.Text;
using Xunit;
using ZmkHidProtocol.Capabilities;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Coverage for the 0xF8 manifest parser (<see cref="RawHidProtocol.TryParseManifestEntry"/>),
/// the stream reassembler (<see cref="ManifestAssembler"/>), and the 0xF7
/// confirmation parser. All synthetic buffers — no I/O.
/// </summary>
public class ManifestAssemblerTests
{
    private static byte[] ManifestReport(byte seq, byte flags, byte role, byte tier, byte confirm, string chunk)
    {
        var bytes = Encoding.UTF8.GetBytes(chunk);
        Assert.True(
            bytes.Length <= HidConstants.ReportSize - ManifestEntry.HeaderSize,
            "test chunk must fit a single report");
        var buf = new byte[HidConstants.ReportSize];
        buf[0] = HidConstants.Outbound.ManifestEntry;
        buf[1] = seq;
        buf[2] = flags;
        buf[3] = role;
        buf[4] = tier;
        buf[5] = confirm;
        bytes.CopyTo(buf, ManifestEntry.HeaderSize);
        return buf;
    }

    private static byte[] ConfirmReport(ushort reference, bool ok)
    {
        var buf = new byte[HidConstants.ReportSize];
        buf[0] = HidConstants.Outbound.Confirm;
        buf[1] = (byte)(reference & 0xFF);
        buf[2] = (byte)(reference >> 8);
        buf[3] = (byte)(ok ? 1 : 0);
        return buf;
    }

    private static DeviceManifest Assemble(params byte[][] reports)
    {
        var asm = new ManifestAssembler();
        foreach (var r in reports)
        {
            var entry = RawHidProtocol.TryParseManifestEntry(r);
            Assert.NotNull(entry);
            asm.Add(entry!);
        }
        return asm.Build();
    }

    [Fact]
    public void SingleCapability_Parsed()
    {
        var manifest = Assemble(ManifestReport(
            seq: 0, flags: ManifestEntry.FlagLast,
            role: (byte)CapabilityRole.Handles, tier: (byte)CapabilityTier.Core,
            confirm: 0, chunk: "core.layer.set"));

        Assert.Null(manifest.Name);
        Assert.Null(manifest.ConfigId);
        var cap = Assert.Single(manifest.Capabilities);
        Assert.Equal(CapabilityRole.Handles, cap.Role);
        Assert.Equal(CapabilityTier.Core, cap.Tier);
        Assert.False(cap.Confirm);
        Assert.Equal("core.layer.set", cap.Id);
    }

    [Fact]
    public void IdentityRows_PopulateNameAndConfigId()
    {
        var manifest = Assemble(
            ManifestReport(0, 0, (byte)CapabilityRole.Identity, tier: 0, confirm: 0, chunk: "Glove80 Left"),
            ManifestReport(1, 0, (byte)CapabilityRole.Identity, tier: 1, confirm: 0, chunk: "go60-custom"),
            ManifestReport(2, ManifestEntry.FlagLast,
                (byte)CapabilityRole.Notifies, (byte)CapabilityTier.Core, 0, "core.layer.changed"));

        Assert.Equal("Glove80 Left", manifest.Name);
        Assert.Equal("go60-custom", manifest.ConfigId);
        var cap = Assert.Single(manifest.Capabilities);
        Assert.Equal("core.layer.changed", cap.Id);
        Assert.Equal(CapabilityRole.Notifies, cap.Role);
    }

    [Fact]
    public void ConfigIdGuid_SpansTwoReports_Reassembled()
    {
        const string guid = "123e4567-e89b-12d3-a456-426614174000"; // 36 chars > one 26-byte chunk
        int split = HidConstants.ReportSize - ManifestEntry.HeaderSize; // 26
        var manifest = Assemble(
            // first chunk fills the report and carries CONTINUES
            ManifestReport(0, ManifestEntry.FlagContinues,
                (byte)CapabilityRole.Identity, tier: 1, confirm: 0, chunk: guid[..split]),
            // final chunk completes the stream
            ManifestReport(0, ManifestEntry.FlagLast,
                role: 0, tier: 0, confirm: 0, chunk: guid[split..]));

        Assert.Equal(guid, manifest.ConfigId);
        Assert.Empty(manifest.Capabilities);
    }

    [Fact]
    public void IsComplete_OnlyAfterLastFlag()
    {
        var asm = new ManifestAssembler();
        asm.Add(RawHidProtocol.TryParseManifestEntry(ManifestReport(
            0, 0, (byte)CapabilityRole.Handles, (byte)CapabilityTier.Core, 0, "core.layer.set"))!);
        Assert.False(asm.IsComplete);

        asm.Add(RawHidProtocol.TryParseManifestEntry(ManifestReport(
            1, ManifestEntry.FlagLast, (byte)CapabilityRole.Handles, (byte)CapabilityTier.Core, 0, "core.layer.setBase"))!);
        Assert.True(asm.IsComplete);
    }

    [Fact]
    public void OutOfOrderSeq_BuiltAscending()
    {
        var manifest = Assemble(
            ManifestReport(2, 0, (byte)CapabilityRole.Triggers, (byte)CapabilityTier.Core, 0, "core.pointing.dpi.setIndex"),
            ManifestReport(0, 0, (byte)CapabilityRole.Identity, 0, 0, "Trackball"),
            ManifestReport(1, ManifestEntry.FlagLast,
                (byte)CapabilityRole.Triggers, (byte)CapabilityTier.Core, 0, "core.pointing.dpi.set"));

        Assert.Equal("Trackball", manifest.Name);
        Assert.Collection(manifest.Capabilities,
            c => Assert.Equal("core.pointing.dpi.set", c.Id),       // seq 1
            c => Assert.Equal("core.pointing.dpi.setIndex", c.Id)); // seq 2
    }

    [Fact]
    public void ConfirmFlag_MappedToCapability()
    {
        var manifest = Assemble(ManifestReport(
            0, ManifestEntry.FlagLast,
            (byte)CapabilityRole.Handles, (byte)CapabilityTier.Optional,
            confirm: 1, chunk: "core.layer.activate"));

        var cap = Assert.Single(manifest.Capabilities);
        Assert.True(cap.Confirm);
        Assert.Equal(CapabilityTier.Optional, cap.Tier);
        Assert.Equal("core.layer.activate", cap.Id);
    }

    [Fact]
    public void RolesAndTiers_MapToEnums()
    {
        var manifest = Assemble(
            ManifestReport(0, 0, (byte)CapabilityRole.Notifies, (byte)CapabilityTier.FwSpecific, 0, "core.keyboard.combo.fired"),
            ManifestReport(1, ManifestEntry.FlagLast,
                (byte)CapabilityRole.Handles, (byte)CapabilityTier.Profile, 0, "core.rgb.set"));

        Assert.Collection(manifest.Capabilities,
            c =>
            {
                Assert.Equal(CapabilityRole.Notifies, c.Role);
                Assert.Equal(CapabilityTier.FwSpecific, c.Tier);
            },
            c =>
            {
                Assert.Equal(CapabilityRole.Handles, c.Role);
                Assert.Equal(CapabilityTier.Profile, c.Tier);
            });
    }

    [Fact]
    public void TryParseManifestEntry_TrimsNullAndPadding()
    {
        // "core.layer.set" (14 bytes) padded with zeros to the 26-byte chunk.
        var entry = RawHidProtocol.TryParseManifestEntry(ManifestReport(
            0, ManifestEntry.FlagLast, (byte)CapabilityRole.Handles, (byte)CapabilityTier.Core, 0, "core.layer.set"));
        Assert.NotNull(entry);
        Assert.Equal(HidConstants.ReportSize - ManifestEntry.HeaderSize, entry!.Chunk.Length);

        var asm = new ManifestAssembler();
        asm.Add(entry);
        Assert.Equal("core.layer.set", Assert.Single(asm.Build().Capabilities).Id);
    }

    [Fact]
    public void TryParseManifestEntry_WrongType_ReturnsNull()
    {
        var layerState = new byte[HidConstants.ReportSize];
        layerState[0] = HidConstants.Outbound.LayerState;
        Assert.Null(RawHidProtocol.TryParseManifestEntry(layerState));
    }

    [Fact]
    public void TryParseManifestEntry_ShortBuffer_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseManifestEntry(new byte[] { 0xF8, 0x00, 0x00, 0x00, 0x00 }));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(258, true)]   // crosses the LE byte boundary
    [InlineData(65535, true)]
    public void TryParseConfirm_RoundTrips(int reference, bool ok)
    {
        var ack = RawHidProtocol.TryParseConfirm(ConfirmReport((ushort)reference, ok));
        Assert.NotNull(ack);
        Assert.Equal((ushort)reference, ack!.Reference);
        Assert.Equal(ok, ack.Ok);
    }

    [Fact]
    public void TryParseConfirm_WrongType_ReturnsNull()
    {
        var manifest = ManifestReport(0, ManifestEntry.FlagLast,
            (byte)CapabilityRole.Handles, (byte)CapabilityTier.Core, 0, "core.layer.set");
        Assert.Null(RawHidProtocol.TryParseConfirm(manifest));
    }

    [Fact]
    public void TryParseConfirm_ShortBuffer_ReturnsNull()
    {
        Assert.Null(RawHidProtocol.TryParseConfirm(new byte[] { 0xF7, 0x01, 0x00 }));
    }
}
