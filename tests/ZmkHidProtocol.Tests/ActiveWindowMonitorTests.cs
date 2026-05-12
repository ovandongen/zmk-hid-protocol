using Xunit;
using ZmkHidProtocol.ActiveWindow;

namespace ZmkHidProtocol.Tests;

public class ActiveWindowMonitorTests
{
    [Fact]
    public void Factory_ReturnsPlatformMonitor()
    {
        using var monitor = ActiveWindowMonitorFactory.Create();
        Assert.NotNull(monitor);
    }

    [Fact]
    public void Monitor_StartThenStop_DoesNotThrow()
    {
        using var monitor = ActiveWindowMonitorFactory.Create();
        monitor.Start();
        monitor.Stop();
    }

    [Fact]
    public void Monitor_StartIsIdempotent()
    {
        using var monitor = ActiveWindowMonitorFactory.Create();
        monitor.Start();
        monitor.Start();
        monitor.Stop();
    }

    [Fact]
    public void ActiveWindowInfo_RecordEquality()
    {
        var a = new ActiveWindowInfo("Code", "com.microsoft.VSCode", "main.cs");
        var b = new ActiveWindowInfo("Code", "com.microsoft.VSCode", "main.cs");
        var c = new ActiveWindowInfo("Code", "com.microsoft.VSCode", "other.cs");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
