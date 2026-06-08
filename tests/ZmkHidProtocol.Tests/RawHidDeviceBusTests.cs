using Xunit;
using ZmkHidProtocol.Protocol;
using ZmkHidProtocol.Transport;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Coverage for <see cref="RawHidDeviceBus.SelectBusDevices"/> — the pure
/// dedup / rank / exclude step. The poll loop and per-device read loop are
/// hardware-bound (HidApi.Net) and validated manually; this pins the selection
/// logic that decides <em>which</em> interfaces the bus opens.
/// </summary>
public class RawHidDeviceBusTests
{
    // Defaults to a vendor raw-HID interface (0xFF60/0x61) so most tests omit it.
    private static RawHidCandidate Cand(
        int vid, int pid, string? name, string path, int iface = 0,
        int usagePage = HidConstants.UsagePage, int usage = HidConstants.UsageId)
        => new(vid, pid, name, usagePage, usage, iface, path);

    private static string[] Paths(IReadOnlyList<RawHidCandidate> sel)
        => sel.Select(c => c.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();

    [Fact]
    public void IdenticalVidPid_CollapseToOne_PreferringCombinedInterface()
    {
        var all = new[]
        {
            Cand(0x16C0, 0x27DB, "Bean", "p0", iface: 0),
            Cand(0x16C0, 0x27DB, "Bean", "p1", iface: -1), // combined — should win
            Cand(0x16C0, 0x27DB, "Bean", "p2", iface: 1),
        };

        var sel = RawHidDeviceBus.SelectBusDevices(all, keyboardMatcher: null, keyboardPath: null);

        var only = Assert.Single(sel);
        Assert.Equal("p1", only.Path);
    }

    [Fact]
    public void IdenticalVidPid_NoCombined_PreferLowestInterface()
    {
        var all = new[]
        {
            Cand(0x16C0, 0x27DB, "Bean", "p2", iface: 2),
            Cand(0x16C0, 0x27DB, "Bean", "p0", iface: 0),
            Cand(0x16C0, 0x27DB, "Bean", "p1", iface: 1),
        };

        var sel = RawHidDeviceBus.SelectBusDevices(all, null, null);

        Assert.Equal("p0", Assert.Single(sel).Path);
    }

    [Fact]
    public void KeyboardMatcher_ExcludesMatchingRows()
    {
        var matcher = new DeviceMatcher(0x16C0, 0x27DB, new[] { "Glove80" });
        var all = new[]
        {
            Cand(0x16C0, 0x27DB, "Glove80 Left", "kbd"), // excluded by matcher
            Cand(0x1234, 0x5678, "Bean", "ball"),        // kept
        };

        var sel = RawHidDeviceBus.SelectBusDevices(all, matcher, null);

        Assert.Equal("ball", Assert.Single(sel).Path);
    }

    [Fact]
    public void SharedVidPid_ExcludesKeyboardRow_KeepsSibling()
    {
        // Two boards on the pid.codes VID/PID; only the keyboard is excluded, and
        // exclusion happens per-row (before grouping) so the sibling survives.
        var matcher = new DeviceMatcher(0x16C0, 0x27DB, new[] { "Glove80" });
        var all = new[]
        {
            Cand(0x16C0, 0x27DB, "Glove80 Left", "kbd", iface: -1),  // keyboard
            Cand(0x16C0, 0x27DB, "OtherBoard", "other", iface: -1),  // sibling, same VID/PID
        };

        var sel = RawHidDeviceBus.SelectBusDevices(all, matcher, null);

        Assert.Equal("other", Assert.Single(sel).Path);
    }

    [Fact]
    public void OpenPath_ExcludesKeyboardHandle_Defensively()
    {
        var all = new[]
        {
            Cand(0x16C0, 0x27DB, "KB", "kbdpath"),   // excluded by open-path
            Cand(0x1234, 0x5678, "Ball", "ballpath"),
        };

        var sel = RawHidDeviceBus.SelectBusDevices(all, keyboardMatcher: null, keyboardPath: "kbdpath");

        Assert.Equal("ballpath", Assert.Single(sel).Path);
    }

    [Fact]
    public void NonVendorRawHidInterfaces_AreExcluded()
    {
        var all = new[]
        {
            Cand(0x16C0, 0x27DB, "KB", "raw"),                                  // FF60/61 — kept
            Cand(0x16C0, 0x27DB, "KB", "mouse", usagePage: 0x01, usage: 0x02),  // generic desktop
            Cand(0x16C0, 0x27DB, "KB", "kbcoll", usagePage: 0x01, usage: 0x06), // keyboard collection
        };

        var sel = RawHidDeviceBus.SelectBusDevices(all, null, null);

        Assert.Equal("raw", Assert.Single(sel).Path);
    }

    [Fact]
    public void DistinctDevices_AreAllKept()
    {
        var all = new[]
        {
            Cand(0x16C0, 0x27DB, "A", "a", iface: -1),
            Cand(0x1234, 0x5678, "B", "b", iface: -1),
            Cand(0x9999, 0x8888, "C", "c", iface: 0),
        };

        var sel = RawHidDeviceBus.SelectBusDevices(all, null, null);

        Assert.Equal(new[] { "a", "b", "c" }, Paths(sel));
    }

    [Fact]
    public void EmptyEnumeration_ReturnsEmpty()
    {
        Assert.Empty(RawHidDeviceBus.SelectBusDevices(Array.Empty<RawHidCandidate>(), null, null));
    }
}
