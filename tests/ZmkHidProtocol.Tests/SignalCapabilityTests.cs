using Xunit;
using ZmkHidProtocol.Capabilities;

namespace ZmkHidProtocol.Tests;

/// <summary>
/// Coverage for the two signal.fire manifest disclosure forms: opaque
/// "signal.fire" and enumerated "signal.fire/&lt;id&gt;".
/// </summary>
public class SignalCapabilityTests
{
    [Theory]
    [InlineData("signal.fire")]
    [InlineData("signal.fire/0")]
    [InlineData("signal.fire/7")]
    [InlineData("signal.fire/255")]
    public void IsFire_TrueForBothForms(string id)
    {
        Assert.True(SignalCapability.IsFire(id));
    }

    [Theory]
    [InlineData("core.rgb.set")]
    [InlineData("core.layer.changed")]
    [InlineData("signal.value/7")]
    [InlineData("")]
    public void IsFire_FalseForOthers(string id)
    {
        Assert.False(SignalCapability.IsFire(id));
    }

    [Theory]
    [InlineData("signal.fire/0", 0)]
    [InlineData("signal.fire/7", 7)]
    [InlineData("signal.fire/255", 255)]
    public void TryParseEnumeratedId_ReturnsId(string id, byte expected)
    {
        Assert.Equal(expected, SignalCapability.TryParseEnumeratedId(id));
    }

    [Theory]
    [InlineData("signal.fire")]      // opaque form carries no id
    [InlineData("signal.fire/256")]  // overflows uint8
    [InlineData("signal.fire/999")]  // overflows uint8
    [InlineData("signal.fire/abc")]  // non-numeric
    [InlineData("signal.fire/")]     // empty suffix
    [InlineData("core.rgb.set")]     // not a signal
    public void TryParseEnumeratedId_ReturnsNull(string id)
    {
        Assert.Null(SignalCapability.TryParseEnumeratedId(id));
    }
}
