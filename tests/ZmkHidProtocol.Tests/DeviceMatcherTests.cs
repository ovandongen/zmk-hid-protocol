using Xunit;
using ZmkHidProtocol.Transport;

namespace ZmkHidProtocol.Tests;

public class DeviceMatcherTests
{
    private static readonly DeviceMatcher Go60 = new(0x16C0, 0x27DB, new[] { "Go60" });
    private static readonly DeviceMatcher Glove80 = new(0x16C0, 0x27DB, new[] { "Glove80" });
    private static readonly DeviceMatcher AnyName = new(0x16C0, 0x27DB, Array.Empty<string>());

    [Theory]
    [InlineData("Go60", true)]
    [InlineData("Go60 Left", true)]    // USB suffix variant
    [InlineData("go60", true)]         // case-insensitive
    [InlineData("Glove80", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Matches_NamePrefixSemantics(string? name, bool expected)
    {
        Assert.Equal(expected, Go60.Matches(0x16C0, 0x27DB, name));
    }

    [Fact]
    public void Matches_WrongVid_ReturnsFalse()
    {
        Assert.False(Go60.Matches(0x0001, 0x27DB, "Go60"));
    }

    [Fact]
    public void Matches_WrongPid_ReturnsFalse()
    {
        Assert.False(Go60.Matches(0x16C0, 0xFFFF, "Go60"));
    }

    [Fact]
    public void Matches_EmptyPrefixList_AcceptsAnyName()
    {
        Assert.True(AnyName.Matches(0x16C0, 0x27DB, "Some Other Keyboard"));
        Assert.True(AnyName.Matches(0x16C0, 0x27DB, null));
    }

    [Theory]
    [InlineData("Go60", true)]
    [InlineData("Go60 Left", true)]
    [InlineData("Glove80", false)]
    [InlineData(null, false)]
    public void MatchesName_IgnoresVidPid(string? name, bool expected)
    {
        Assert.Equal(expected, Go60.MatchesName(name));
    }

    [Fact]
    public void MatchesName_EmptyPrefixList_AcceptsAnyName()
    {
        Assert.True(AnyName.MatchesName("anything"));
        Assert.True(AnyName.MatchesName(null));
    }

    [Fact]
    public void Matches_MultiplePrefixes_AnyMatches()
    {
        var multi = new DeviceMatcher(0x16C0, 0x27DB, new[] { "Go60", "Glove80" });
        Assert.True(multi.Matches(0x16C0, 0x27DB, "Go60 Left"));
        Assert.True(multi.Matches(0x16C0, 0x27DB, "Glove80"));
        Assert.False(multi.Matches(0x16C0, 0x27DB, "Sofle"));
    }
}
