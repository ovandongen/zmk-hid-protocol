using Xunit;
using ZmkHidProtocol.Capabilities;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Staleness guard for the generated firmware-facing doc: the committed
/// <c>docs/capability-registry.md</c> must match what
/// <see cref="CapabilityRegistryDoc.Generate"/> produces from the live registry.
/// If this fails, a capability row changed without regenerating the doc — run the
/// generator (or copy the expected output below) and commit the result.
/// </summary>
public class CapabilityRegistryDocTests
{
    [Fact]
    public void CommittedDoc_MatchesGeneratedRegistry()
    {
        var docPath = Path.Combine(SubmoduleRoot(), CapabilityRegistryDoc.RelativePath);
        Assert.True(File.Exists(docPath), $"Missing generated doc at {docPath}");

        // Normalize line endings so a CRLF checkout doesn't trip the byte compare.
        var committed = File.ReadAllText(docPath).Replace("\r\n", "\n");
        var expected = CapabilityRegistryDoc.Generate().Replace("\r\n", "\n");

        Assert.True(committed == expected,
            $"{CapabilityRegistryDoc.RelativePath} is stale — regenerate it from CapabilityRegistry.");
    }

    // Walk up from the test binary to the submodule root (marked by the solution file).
    private static string SubmoduleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZmkHidProtocol.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
