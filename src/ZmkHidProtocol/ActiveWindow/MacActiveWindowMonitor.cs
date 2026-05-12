using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZmkHidProtocol.Diagnostics;

namespace ZmkHidProtocol.ActiveWindow;

/// <summary>
/// macOS active-window monitor. Reads
/// <c>NSWorkspace.sharedWorkspace.frontmostApplication</c> via the
/// Objective-C runtime every <see cref="PollIntervalMs"/> ms.
///
/// <para><b>WindowTitle is always null.</b> Per-window title access on
/// macOS requires Accessibility API entitlements
/// (<c>NSApplication.shared.isTrusted</c>) and a user-granted permission
/// in System Settings → Privacy &amp; Security → Accessibility. The
/// library deliberately stays inside the no-permissions <c>NSWorkspace</c>
/// surface; apps that need window titles can layer the Accessibility
/// API on top.</para>
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacActiveWindowMonitor : IActiveWindowMonitor
{
    private const int PollIntervalMs = 500;
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private ActiveWindowInfo? _current;

    private readonly IntPtr _nsWorkspaceClass;
    private readonly IntPtr _selSharedWorkspace;
    private readonly IntPtr _selFrontmostApp;
    private readonly IntPtr _selBundleId;
    private readonly IntPtr _selLocalizedName;
    private readonly IntPtr _selUtf8String;

    public MacActiveWindowMonitor()
    {
        // AppKit isn't loaded by default in a .NET process, so NSWorkspace
        // lookup returns nil unless we dlopen the framework first.
        if (dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", RTLD_LAZY) == IntPtr.Zero)
            LibLog.Warn("ActiveWindow/Mac", "dlopen(AppKit) failed; frontmost-app queries will return null.");

        _nsWorkspaceClass = objc_getClass("NSWorkspace");
        _selSharedWorkspace = sel_registerName("sharedWorkspace");
        _selFrontmostApp = sel_registerName("frontmostApplication");
        _selBundleId = sel_registerName("bundleIdentifier");
        _selLocalizedName = sel_registerName("localizedName");
        _selUtf8String = sel_registerName("UTF8String");
    }

    public event Action<ActiveWindowInfo>? FocusChanged;

    public ActiveWindowInfo? Current => _current;

    public void Start()
    {
        if (_runTask is not null) return;
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

    public void Dispose() => Stop();

    private void RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var info = ReadFrontmost();
                if (info is not null && !Equals(info, _current))
                {
                    _current = info;
                    FocusChanged?.Invoke(info);
                }
            }
            catch (Exception ex)
            {
                LibLog.Warn("ActiveWindow/Mac", $"poll failed: {ex.Message}");
            }

            try { Task.Delay(PollIntervalMs, token).Wait(token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private ActiveWindowInfo? ReadFrontmost()
    {
        var workspace = objc_msgSend(_nsWorkspaceClass, _selSharedWorkspace);
        if (workspace == IntPtr.Zero) return null;
        var app = objc_msgSend(workspace, _selFrontmostApp);
        if (app == IntPtr.Zero) return null;

        var bundleId = NSStringToString(objc_msgSend(app, _selBundleId));
        var processName = NSStringToString(objc_msgSend(app, _selLocalizedName)) ?? "unknown";
        return new ActiveWindowInfo(processName, bundleId, WindowTitle: null);
    }

    private string? NSStringToString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return null;
        var utf8 = objc_msgSend(nsString, _selUtf8String);
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    [DllImport(Libobjc)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Libobjc)] private static extern IntPtr sel_registerName(string name);
    [DllImport(Libobjc)] private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    private const int RTLD_LAZY = 1;
    [DllImport("/usr/lib/libSystem.dylib")] private static extern IntPtr dlopen(string path, int mode);
}
