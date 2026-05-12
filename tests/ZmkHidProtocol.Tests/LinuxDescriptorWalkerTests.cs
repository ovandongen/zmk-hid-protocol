using Xunit;
using ZmkHidProtocol.Transport.Linux;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Coverage for the minimal HID report-descriptor walker used by
/// <see cref="LinuxRawHidLayerSource"/> to decide whether a /dev/hidraw node
/// exposes the FF60/61 usage pair we care about. The walker only needs to
/// answer "does this descriptor declare (page, usage) anywhere?" — not fully
/// parse it.
/// </summary>
public class LinuxDescriptorWalkerTests
{
    [Fact]
    public void Matches_TopLevel_FF60_61()
    {
        var desc = new byte[]
        {
            0x06, 0x60, 0xFF,   // Usage Page (FF60)
            0x09, 0x61,         // Usage (0x61)
            0xA1, 0x01,         // Collection (Application)
            0xC0,               // End Collection
        };
        Assert.True(LinuxRawHidLayerSource.DescriptorMatchesUsage(desc, 0xFF60, 0x61));
    }

    [Fact]
    public void Rejects_KeyboardOnly_Descriptor()
    {
        var desc = new byte[]
        {
            0x05, 0x01,         // Usage Page (Generic Desktop)
            0x09, 0x06,         // Usage (Keyboard)
            0xA1, 0x01,
            0xC0,
        };
        Assert.False(LinuxRawHidLayerSource.DescriptorMatchesUsage(desc, 0xFF60, 0x61));
    }

    [Fact]
    public void Rejects_WhenUsagePageMatches_ButUsageDoesNot()
    {
        var desc = new byte[]
        {
            0x06, 0x60, 0xFF,
            0x09, 0x42,         // wrong usage
            0xA1, 0x01,
            0xC0,
        };
        Assert.False(LinuxRawHidLayerSource.DescriptorMatchesUsage(desc, 0xFF60, 0x61));
    }

    [Fact]
    public void Matches_When_Target_Appears_AfterAnotherCollection()
    {
        var desc = new byte[]
        {
            0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0xC0,
            0x06, 0x60, 0xFF, 0x09, 0x61, 0xA1, 0x01, 0xC0,
        };
        Assert.True(LinuxRawHidLayerSource.DescriptorMatchesUsage(desc, 0xFF60, 0x61));
    }

    [Fact]
    public void Empty_Descriptor_DoesNotMatch()
    {
        Assert.False(LinuxRawHidLayerSource.DescriptorMatchesUsage(Array.Empty<byte>(), 0xFF60, 0x61));
    }

    [Fact]
    public void Truncated_Descriptor_DoesNotCrash()
    {
        var desc = new byte[] { 0x06, 0x60 };
        Assert.False(LinuxRawHidLayerSource.DescriptorMatchesUsage(desc, 0xFF60, 0x61));
    }
}
