using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZmkHidProtocol.Diagnostics;

namespace ZmkHidProtocol.ActiveWindow;

/// <summary>
/// X11-based active-window monitor. Reads <c>_NET_ACTIVE_WINDOW</c> from
/// the root window and <c>WM_CLASS</c> + <c>_NET_WM_NAME</c>/<c>WM_NAME</c>
/// from the active window every <see cref="PollIntervalMs"/> ms.
///
/// <para><b>Wayland limitation:</b> Native Wayland apps don't expose focus
/// state to X11 clients. On a Wayland session this monitor either sees only
/// XWayland (legacy X) apps or nothing at all. A future implementation can
/// add a GNOME-specific D-Bus path via <c>org.gnome.Shell</c>; other
/// compositors lock down focus tracking entirely.</para>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxActiveWindowMonitor : IActiveWindowMonitor
{
    private const int PollIntervalMs = 500;
    private const string LibX11 = "libX11.so.6";

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private IntPtr _display;
    private ActiveWindowInfo? _current;

    public event Action<ActiveWindowInfo>? FocusChanged;

    public ActiveWindowInfo? Current => _current;

    public void Start()
    {
        if (_runTask is not null) return;

        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
            LibLog.Warn("ActiveWindow/Linux", "Wayland session detected; only XWayland apps will be visible.");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _runTask = Task.Run(() => RunLoop(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* swallow on shutdown */ }
        _runTask = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        if (_display != IntPtr.Zero)
        {
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    private void RunLoop(CancellationToken token)
    {
        _display = XOpenDisplay(null);
        if (_display == IntPtr.Zero)
        {
            LibLog.Warn("ActiveWindow/Linux", "XOpenDisplay failed; no DISPLAY or X server unavailable.");
            return;
        }

        var netActiveWindow = XInternAtom(_display, "_NET_ACTIVE_WINDOW", false);
        var netWmName = XInternAtom(_display, "_NET_WM_NAME", false);
        var utf8String = XInternAtom(_display, "UTF8_STRING", false);
        var root = XDefaultRootWindow(_display);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var info = ReadActiveWindow(root, netActiveWindow, netWmName, utf8String);
                if (info is not null && !Equals(info, _current))
                {
                    _current = info;
                    FocusChanged?.Invoke(info);
                }
            }
            catch (Exception ex)
            {
                LibLog.Warn("ActiveWindow/Linux", $"poll failed: {ex.Message}");
            }

            try { Task.Delay(PollIntervalMs, token).Wait(token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private ActiveWindowInfo? ReadActiveWindow(IntPtr root, IntPtr netActiveWindow, IntPtr netWmName, IntPtr utf8String)
    {
        if (!TryGetWindowProperty(root, netActiveWindow, out var data, out var nitems) || data == IntPtr.Zero)
            return null;
        try
        {
            if ((int)nitems < 1) return null;
            var activeWindow = Marshal.ReadIntPtr(data);
            if (activeWindow == IntPtr.Zero) return null;

            string processName = ReadClassHint(activeWindow);
            string? title = ReadWindowName(activeWindow, netWmName, utf8String);
            return new ActiveWindowInfo(processName, BundleId: null, title);
        }
        finally
        {
            XFree(data);
        }
    }

    private bool TryGetWindowProperty(IntPtr window, IntPtr property, out IntPtr data, out IntPtr nitems)
    {
        var status = XGetWindowProperty(
            _display, window, property,
            IntPtr.Zero, new IntPtr(1024), false, new IntPtr(0),
            out _, out _, out nitems, out _, out data);
        return status == 0;
    }

    private string ReadClassHint(IntPtr window)
    {
        if (XGetClassHint(_display, window, out var hint) == 0)
            return "unknown";
        try
        {
            // res_class is the "WM_CLASS" application class, conventionally PascalCase ("Firefox", "Code").
            var cls = Marshal.PtrToStringAnsi(hint.res_class);
            return string.IsNullOrEmpty(cls) ? Marshal.PtrToStringAnsi(hint.res_name) ?? "unknown" : cls;
        }
        finally
        {
            if (hint.res_name != IntPtr.Zero) XFree(hint.res_name);
            if (hint.res_class != IntPtr.Zero) XFree(hint.res_class);
        }
    }

    private string? ReadWindowName(IntPtr window, IntPtr netWmName, IntPtr utf8String)
    {
        // Prefer _NET_WM_NAME (UTF-8) over the legacy WM_NAME (ANSI).
        var status = XGetWindowProperty(
            _display, window, netWmName,
            IntPtr.Zero, new IntPtr(1024), false, utf8String,
            out _, out _, out var nitems, out _, out var data);
        if (status == 0 && data != IntPtr.Zero && (int)nitems > 0)
        {
            try { return Marshal.PtrToStringUTF8(data); }
            finally { XFree(data); }
        }

        if (XFetchName(_display, window, out var legacy) != 0 && legacy != IntPtr.Zero)
        {
            try { return Marshal.PtrToStringAnsi(legacy); }
            finally { XFree(legacy); }
        }
        return null;
    }

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(string? display);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XInternAtom(IntPtr display, string atom_name, bool only_if_exists);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern int XFree(IntPtr data);
    [DllImport(LibX11)] private static extern int XGetClassHint(IntPtr display, IntPtr window, out XClassHint hint);
    [DllImport(LibX11)] private static extern int XFetchName(IntPtr display, IntPtr window, out IntPtr window_name);

    [DllImport(LibX11)]
    private static extern int XGetWindowProperty(
        IntPtr display, IntPtr w, IntPtr property,
        IntPtr long_offset, IntPtr long_length, bool delete, IntPtr req_type,
        out IntPtr actual_type_return, out int actual_format_return,
        out IntPtr nitems_return, out IntPtr bytes_after_return,
        out IntPtr prop_return);

    [StructLayout(LayoutKind.Sequential)]
    private struct XClassHint
    {
        public IntPtr res_name;
        public IntPtr res_class;
    }
}
