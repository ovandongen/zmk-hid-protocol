using HidApi;
using ZmkHidProtocol.Capabilities;
using ZmkHidProtocol.Diagnostics;
using ZmkHidProtocol.Protocol;
using HidDeviceInfo = HidApi.DeviceInfo;

namespace ZmkHidProtocol.Transport;

/// <summary>
/// One enumerated raw-HID interface reduced to the fields bus selection needs.
/// Hardware-free (a plain record, not <see cref="HidApi.DeviceInfo"/>) so the
/// dedup/rank/exclude logic in <see cref="RawHidDeviceBus.SelectBusDevices"/> is
/// unit-testable without a connected device.
/// </summary>
public readonly record struct RawHidCandidate(
    int Vid,
    int Pid,
    string? Name,
    int UsagePage,
    int Usage,
    int InterfaceNumber,
    string Path);

/// <summary>
/// Owns every protocol-capable Raw HID device <em>except</em> the keyboard, which
/// stays owned by the app's HID pipeline and is surfaced to the router through a
/// separate adapter (so it is never opened twice). A background poll enumerates
/// on a ~2 s cadence and only opens/closes when the raw-HID set actually changes;
/// <see cref="DevicesChanged"/> fires with the current device set after each
/// change. Mirrors the reference host's <c>find_devices</c> dedup + the keyboard
/// exclusion LViz adds on top.
/// </summary>
public sealed class RawHidDeviceBus : ICapabilityDeviceBus, IDisposable
{
    private const int PollDelayMs = 2000;

    private readonly object _gate = new();
    private readonly Dictionary<string, RawHidBusDevice> _open = new(); // keyed by hidapi path
    private readonly HashSet<string> _faulted = new();                 // paths whose read loop died
    private readonly ManualResetEventSlim _rescan = new(false);
    private volatile IDeviceMatcher? _keyboardMatcher;
    private readonly Func<string?> _keyboardPath;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private string? _lastEnumKey;

    /// <param name="keyboardMatcher">
    /// Matches the active keyboard so its rows are excluded from the bus. Update
    /// via <see cref="SetKeyboardMatcher"/> on profile change.</param>
    /// <param name="keyboardPath">
    /// Reads the keyboard's currently open hidapi path (e.g.
    /// <see cref="RawHidLayerSource.OpenDevicePath"/>) — a defensive secondary
    /// exclusion for the identical-VID/PID/name case. Evaluated each poll.</param>
    public RawHidDeviceBus(IDeviceMatcher? keyboardMatcher = null, Func<string?>? keyboardPath = null)
    {
        _keyboardMatcher = keyboardMatcher;
        _keyboardPath = keyboardPath ?? (() => null);
    }

    /// <summary>Raised after the open device set changes, carrying the new set.</summary>
    public event Action<IReadOnlyList<ICapabilityDevice>>? DevicesChanged;

    /// <summary>Snapshot of the currently open bus devices (excludes the keyboard).</summary>
    public IReadOnlyList<ICapabilityDevice> Devices
    {
        get { lock (_gate) return _open.Values.ToArray<ICapabilityDevice>(); }
    }

    /// <summary>Updates the keyboard matcher (profile change) and forces a re-scan.</summary>
    public void SetKeyboardMatcher(IDeviceMatcher? matcher)
    {
        _keyboardMatcher = matcher;
        _lastEnumKey = null; // matcher change may add/drop a device the enum hash wouldn't catch
        _rescan.Set();
    }

    public void Start()
    {
        if (_runTask is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _runTask = Task.Run(() => RunLoop(ct));
        LibLog.Info("RawHidBus", "RawHidDeviceBus started");
    }

    public void Stop()
    {
        if (_runTask is null) return;
        try { _cts?.Cancel(); } catch { }
        _rescan.Set();
        try { _runTask.Wait(2000); } catch { }
        _runTask = null;
        _cts?.Dispose();
        _cts = null;

        List<RawHidBusDevice> toClose;
        lock (_gate)
        {
            toClose = _open.Values.ToList();
            _open.Clear();
            _faulted.Clear();
            _lastEnumKey = null;
        }
        foreach (var d in toClose) d.Dispose();
        if (toClose.Count > 0) RaiseDevicesChanged();
    }

    public void Dispose()
    {
        Stop();
        _rescan.Dispose();
    }

    /// <summary>
    /// Pure selection over enumerated candidates: keep only vendor raw-HID
    /// interfaces (0xFF60/0x61), drop the keyboard (matcher match, or open-path
    /// match), then collapse to one device per (vid,pid) preferring the combined
    /// interface (-1) and then the lowest interface number. Mirrors the reference
    /// host's <c>find_devices</c> + <c>_rank</c> with the keyboard exclusion added.
    /// </summary>
    public static IReadOnlyList<RawHidCandidate> SelectBusDevices(
        IEnumerable<RawHidCandidate> enumerated,
        IDeviceMatcher? keyboardMatcher,
        string? keyboardPath)
    {
        return enumerated
            .Where(c => c.UsagePage == HidConstants.UsagePage && c.Usage == HidConstants.UsageId)
            .Where(c => keyboardMatcher is null || !keyboardMatcher.Matches(c.Vid, c.Pid, c.Name))
            .Where(c => keyboardPath is null || c.Path != keyboardPath)
            .GroupBy(c => (c.Vid, c.Pid))
            .Select(g => g
                .OrderBy(c => c.InterfaceNumber == -1 ? 0 : 1) // combined interface first
                .ThenBy(c => c.InterfaceNumber)
                .First())
            .ToList();
    }

    private void RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Reconcile(); }
            catch (Exception ex) { LibLog.Warn("RawHidBus", $"Reconcile failed: {ex.Message}"); }

            _rescan.Reset();
            try { _rescan.Wait(PollDelayMs, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Reconcile()
    {
        List<HidDeviceInfo> devices;
        // Serialize against the keyboard source's enumerate loop — concurrent
        // hidapi enumeration aborts the process on macOS. Materialize inside the
        // lock so the native enumeration completes before the gate is released.
        try { lock (HidGlobalLock.Gate) devices = Hid.Enumerate().ToList(); }
        catch (Exception ex)
        {
            LibLog.Debug("RawHidBus", $"Hid.Enumerate failed: {ex.Message}");
            return;
        }

        // Map enumerated devices to candidates, keeping a path → info lookup so we
        // can re-open the exact selected interfaces.
        var byPath = new Dictionary<string, HidDeviceInfo>();
        var candidates = new List<RawHidCandidate>();
        foreach (var d in devices)
        {
            byPath[d.Path] = d;
            candidates.Add(new RawHidCandidate(
                d.VendorId, d.ProductId, d.ProductString,
                d.UsagePage, d.Usage, d.InterfaceNumber, d.Path));
        }

        bool faultsPending;
        lock (_gate) faultsPending = _faulted.Count > 0;

        // Cheap short-circuit: skip recompute when the raw-HID set is unchanged and
        // nothing faulted. The diff below would no-op anyway, but this also avoids
        // re-running selection every tick.
        var enumKey = string.Join("|", candidates
            .Where(c => c.UsagePage == HidConstants.UsagePage && c.Usage == HidConstants.UsageId)
            .Select(c => c.Path)
            .OrderBy(p => p, StringComparer.Ordinal));
        if (enumKey == _lastEnumKey && !faultsPending) return;
        _lastEnumKey = enumKey;

        var desired = SelectBusDevices(candidates, _keyboardMatcher, _keyboardPath());
        var desiredPaths = desired.Select(d => d.Path).ToHashSet();

        bool changed = false;
        lock (_gate)
        {
            // Drop faulted handles so they get re-opened below if still desired.
            foreach (var path in _faulted.ToList())
            {
                if (_open.Remove(path, out var dead)) { dead.Dispose(); changed = true; }
            }
            _faulted.Clear();

            // Close devices no longer desired (unplugged / now excluded).
            foreach (var path in _open.Keys.Where(p => !desiredPaths.Contains(p)).ToList())
            {
                if (_open.Remove(path, out var gone)) { gone.Dispose(); changed = true; }
            }

            // Open newly desired devices.
            foreach (var cand in desired)
            {
                if (_open.ContainsKey(cand.Path)) continue;
                if (!byPath.TryGetValue(cand.Path, out var info)) continue;
                try
                {
                    var device = new RawHidBusDevice(info);
                    device.Faulted += () => OnDeviceFaulted(cand.Path);
                    device.Start();
                    _open[cand.Path] = device;
                    changed = true;
                }
                catch (Exception ex)
                {
                    // Another app may hold this interface (VIA / QMK Toolbox). Skip it.
                    LibLog.Warn("RawHidBus", $"Open failed for {cand.Path}: {ex.Message}");
                }
            }
        }

        if (changed) RaiseDevicesChanged();
    }

    private void OnDeviceFaulted(string path)
    {
        // Runs on the faulting device's read-loop thread: only record + signal,
        // never dispose here (that thread is ending). Reconcile disposes off-thread.
        lock (_gate) _faulted.Add(path);
        _rescan.Set();
    }

    private void RaiseDevicesChanged() => DevicesChanged?.Invoke(Devices);
}
