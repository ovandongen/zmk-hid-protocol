using ZmkHidProtocol.Transport.Linux;
using ZmkHidProtocol.Transport.Mac;
using ZmkHidProtocol.Transport.Windows;

namespace ZmkHidProtocol.Transport;

/// <summary>
/// Picks the right platform-specific Raw-HID transport at runtime so
/// consumers don't repeat <c>OperatingSystem.IsX()</c> ladders. Every
/// platform source implements both <see cref="ILayerSource"/> and
/// <see cref="ICommandSink"/>; on Windows the two are returned by a
/// composite that fans out USB + BLE in parallel.
///
/// <para>BLE on Windows requires the <c>net10.0-windows10.0.19041.0</c>
/// target framework. Apps targeting portable <c>net10.0</c> on a Windows
/// host fall back to USB only.</para>
/// </summary>
public static class LayerSourceFactory
{
    /// <summary>
    /// Creates the platform source for the current OS. Returns the same
    /// instance as both <c>Source</c> and <c>Sink</c> on Linux/macOS;
    /// returns a Windows composite that implements both on Windows.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on operating systems other than Windows, macOS, or Linux.
    /// </exception>
    public static (ILayerSource Source, ICommandSink Sink) Create(IDeviceMatcher? matcher = null)
    {
        if (OperatingSystem.IsMacOS())
        {
            var mac = new MacRawHidLayerSource(matcher);
            return (mac, mac);
        }

        if (OperatingSystem.IsLinux())
        {
            var linux = new LinuxRawHidLayerSource(matcher);
            return (linux, linux);
        }

        if (OperatingSystem.IsWindows())
        {
#if WINDOWS
            var composite = new WindowsCompositeLayerSource(matcher);
            return (composite, composite);
#else
            var usb = new WindowsRawHidLayerSource(matcher);
            return (usb, usb);
#endif
        }

        throw new PlatformNotSupportedException(
            $"No ZMK Raw-HID transport for {Environment.OSVersion.Platform}.");
    }
}
