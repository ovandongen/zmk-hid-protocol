using System.Text;
using Xunit;
using ZmkHidProtocol.Capabilities;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Coverage for <see cref="ManifestReader"/> over a <see cref="FakeCapabilityDevice"/>:
/// the 0xF9 request is sent, a 0xF8 stream reassembles into a manifest, noise is
/// ignored, a stream without the LAST flag times out to null, and the report
/// subscription is always released.
/// </summary>
public class ManifestReaderTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(2);

    private static byte[] ManifestReport(byte seq, byte flags, byte role, byte tier, byte confirm, string chunk)
    {
        var bytes = Encoding.UTF8.GetBytes(chunk);
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

    // Identity name row + one handled capability, terminated by LAST.
    private static byte[][] MinimalManifest() => new[]
    {
        ManifestReport(0, 0, (byte)CapabilityRole.Identity, 0, 0, "Bean"),
        ManifestReport(1, ManifestEntry.FlagLast,
            (byte)CapabilityRole.Handles, (byte)CapabilityTier.Core, 0, "core.pointing.dpi.set"),
    };

    [Fact]
    public async Task ReadAsync_SendsGetManifestRequest()
    {
        var device = new FakeCapabilityDevice();

        var task = ManifestReader.ReadAsync(device, LongTimeout, CancellationToken.None);
        await device.WriteObserved;

        Assert.Equal(HidConstants.ReportSize, device.LastSent.Length);
        Assert.Equal(HidConstants.Inbound.GetManifest, device.LastSent.Span[0]);
        Assert.Equal(0, device.LastSent.Span[1]); // start index = 0

        // Complete the stream so the reader returns cleanly.
        device.RaiseReports(MinimalManifest());
        await task;
    }

    [Fact]
    public async Task ReadAsync_AssemblesStreamIntoManifest()
    {
        var device = new FakeCapabilityDevice();

        var task = ManifestReader.ReadAsync(device, LongTimeout, CancellationToken.None);
        await device.WriteObserved;
        device.RaiseReports(MinimalManifest());

        var manifest = await task;

        Assert.NotNull(manifest);
        Assert.Equal("Bean", manifest!.Name);
        Assert.Null(manifest.ConfigId);
        var cap = Assert.Single(manifest.Capabilities);
        Assert.Equal("core.pointing.dpi.set", cap.Id);
        Assert.Equal(CapabilityRole.Handles, cap.Role);
        Assert.Equal(CapabilityTier.Core, cap.Tier);
    }

    [Fact]
    public async Task ReadAsync_IgnoresNonManifestReports()
    {
        var device = new FakeCapabilityDevice();

        var task = ManifestReader.ReadAsync(device, LongTimeout, CancellationToken.None);
        await device.WriteObserved;

        // Interleave a 0xFF layer-state and a 0xF1 key-event report — both ignored.
        var layerState = new byte[HidConstants.ReportSize];
        layerState[0] = HidConstants.Outbound.LayerState;
        var keyEvent = new byte[HidConstants.ReportSize];
        keyEvent[0] = HidConstants.Outbound.KeyEvent;
        device.RaiseReport(layerState);
        device.RaiseReport(keyEvent);
        device.RaiseReports(MinimalManifest());

        var manifest = await task;

        Assert.NotNull(manifest);
        Assert.Single(manifest!.Capabilities);
    }

    [Fact]
    public async Task ReadAsync_TimesOutToNull_WhenNothingArrives()
    {
        var device = new FakeCapabilityDevice();

        var manifest = await ManifestReader.ReadAsync(device, ShortTimeout, CancellationToken.None);

        Assert.Null(manifest);
        Assert.False(device.HasReportSubscribers); // unsubscribed in finally
    }

    [Fact]
    public async Task ReadAsync_TimesOutToNull_OnStreamWithoutLastFlag()
    {
        var device = new FakeCapabilityDevice();

        var task = ManifestReader.ReadAsync(device, ShortTimeout, CancellationToken.None);
        await device.WriteObserved;
        // A capability row but no LAST flag — the stream never completes.
        device.RaiseReport(ManifestReport(0, 0,
            (byte)CapabilityRole.Handles, (byte)CapabilityTier.Core, 0, "core.pointing.dpi.set"));

        Assert.Null(await task);
    }

    [Fact]
    public async Task ReadAsync_UnsubscribesAfterSuccess()
    {
        var device = new FakeCapabilityDevice();

        var task = ManifestReader.ReadAsync(device, LongTimeout, CancellationToken.None);
        await device.WriteObserved;
        device.RaiseReports(MinimalManifest());
        await task;

        Assert.False(device.HasReportSubscribers);
    }
}
