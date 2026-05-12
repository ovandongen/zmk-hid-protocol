namespace ZmkHidProtocol.ActiveWindow;

/// <summary>
/// Returns the platform implementation of <see cref="IActiveWindowMonitor"/>
/// for the current OS, matching the dispatch pattern used by
/// <see cref="Transport.LayerSourceFactory"/>.
///
/// <para>The Windows monitor requires the
/// <c>net10.0-windows10.0.19041.0</c> target framework; apps targeting
/// portable <c>net10.0</c> on Windows get a
/// <see cref="PlatformNotSupportedException"/> from this factory.</para>
/// </summary>
public static class ActiveWindowMonitorFactory
{
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on unsupported OSes (or portable <c>net10.0</c> on Windows).
    /// </exception>
    public static IActiveWindowMonitor Create()
    {
        if (OperatingSystem.IsMacOS())
            return new MacActiveWindowMonitor();
        if (OperatingSystem.IsLinux())
            return new LinuxActiveWindowMonitor();
#if WINDOWS
        if (OperatingSystem.IsWindows())
            return new WinActiveWindowMonitor();
#endif
        throw new PlatformNotSupportedException(
            $"No active-window monitor for {Environment.OSVersion.Platform}.");
    }
}
