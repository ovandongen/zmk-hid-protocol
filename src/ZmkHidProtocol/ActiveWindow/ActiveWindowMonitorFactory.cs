namespace ZmkHidProtocol.ActiveWindow;

/// <summary>
/// Returns the platform implementation of <see cref="IActiveWindowMonitor"/>
/// for the current OS, matching the dispatch pattern used by
/// <see cref="Transport.LayerSourceFactory"/>. All three monitors use only
/// cross-platform-available P/Invoke (no WinRT), so the factory works on
/// both the portable <c>net10.0</c> and the <c>net10.0-windows...</c> TFMs.
/// </summary>
public static class ActiveWindowMonitorFactory
{
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on operating systems other than Windows, macOS, or Linux.
    /// </exception>
    public static IActiveWindowMonitor Create()
    {
        if (OperatingSystem.IsMacOS())
            return new MacActiveWindowMonitor();
        if (OperatingSystem.IsLinux())
            return new LinuxActiveWindowMonitor();
        if (OperatingSystem.IsWindows())
            return new WinActiveWindowMonitor();
        throw new PlatformNotSupportedException(
            $"No active-window monitor for {Environment.OSVersion.Platform}.");
    }
}
