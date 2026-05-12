using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ZmkHidProtocol.Diagnostics;

namespace ZmkHidProtocol.ActiveWindow;

/// <summary>
/// Windows active-window monitor using <c>user32.dll</c> polling.
/// Calls <c>GetForegroundWindow</c>, <c>GetWindowThreadProcessId</c>,
/// and <c>GetWindowText</c> every <see cref="PollIntervalMs"/> ms.
///
/// <para>A future revision can swap to <c>SetWinEventHook</c> for
/// event-driven dispatch — that requires a dedicated message-pump
/// thread, which polling at 500&#160;ms sidesteps. The latency cost is
/// barely perceptible for the app-switch → layer-switch use case.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinActiveWindowMonitor : IActiveWindowMonitor
{
    private const int PollIntervalMs = 500;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private ActiveWindowInfo? _current;

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
                var info = ReadForeground();
                if (info is not null && !Equals(info, _current))
                {
                    _current = info;
                    FocusChanged?.Invoke(info);
                }
            }
            catch (Exception ex)
            {
                LibLog.Warn("ActiveWindow/Windows", $"poll failed: {ex.Message}");
            }

            try { Task.Delay(PollIntervalMs, token).Wait(token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static ActiveWindowInfo? ReadForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        string processName;
        try { processName = Process.GetProcessById((int)pid).ProcessName; }
        catch (ArgumentException) { return null; } // process exited between calls

        var len = GetWindowTextLength(hwnd);
        string? title = null;
        if (len > 0)
        {
            var sb = new StringBuilder(len + 1);
            if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
                title = sb.ToString();
        }

        return new ActiveWindowInfo(processName, BundleId: null, title);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
