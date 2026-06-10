using HidApi;
using ZmkHidProtocol.Capabilities;
using ZmkHidProtocol.Diagnostics;
using ZmkHidProtocol.Protocol;
using HidDeviceInfo = HidApi.DeviceInfo;

namespace ZmkHidProtocol.Transport;

/// <summary>
/// One non-keyboard Raw HID device on the capability bus: owns a single open
/// hidapi handle plus a background read loop, exposed as an
/// <see cref="ICapabilityDevice"/>. The read/write mechanics are lifted from
/// <see cref="RawHidLayerSource"/> (the 0x00 report-id prefix on write, the
/// optional report-id strip on read, the 250 ms read timeout, the write lock) —
/// the difference is that this device does not self-reconnect: the owning
/// <see cref="RawHidDeviceBus"/> handles discovery and re-open, and a dead read
/// loop raises <see cref="Faulted"/> so the bus can drop and re-evaluate it.
/// </summary>
public sealed class RawHidBusDevice : ICapabilityDevice, IDisposable
{
    private const int ReadTimeoutMs = 250;

    private readonly HidDeviceInfo _info;
    private readonly object _writeLock = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private Device? _device;

    public RawHidBusDevice(HidDeviceInfo info)
    {
        _info = info;
        DisplayName = string.IsNullOrWhiteSpace(info.ProductString) ? "Raw HID device" : info.ProductString!;
        TransportKey = $"{info.VendorId:X4}:{info.ProductId:X4}:{info.Path}";
    }

    public string DisplayName { get; }
    public string TransportKey { get; }

    /// <summary>The hidapi path this device was opened on (bus dedup/exclusion key).</summary>
    public string Path => _info.Path;

    public event Action<ReadOnlyMemory<byte>>? ReportReceived;

    /// <summary>Raised once if the read loop dies unexpectedly (handle gone / read error).</summary>
    public event Action? Faulted;

    /// <summary>Opens the handle and starts the read loop. Throws if the device cannot be opened.</summary>
    public void Start()
    {
        if (_runTask is not null) return;
        // Serialize the open against any concurrent enumerate/open — hidapi's
        // macOS init is not thread-safe (see HidGlobalLock).
        lock (HidGlobalLock.Gate) _device = _info.ConnectToDevice();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _runTask = Task.Run(() => RunLoop(ct));
        LibLog.Info("RawHidBus", $"Opened {DisplayName} ({TransportKey})");
    }

    public ValueTask SendReportAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = _device ?? throw new InvalidOperationException("RawHidBusDevice: device is not open.");
        // hidapi expects a report-ID byte at index 0; the firmware uses report
        // ID 0, so prepend a leading 0x00 (same as RawHidLayerSource).
        var prefixed = new byte[report.Length + 1];
        report.Span.CopyTo(prefixed.AsSpan(1));
        lock (_writeLock)
        {
            device.Write(prefixed);
        }
        return ValueTask.CompletedTask;
    }

    private void RunLoop(CancellationToken ct)
    {
        var device = _device!;
        var buffer = new byte[HidConstants.ReportSize + 1];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = device.ReadTimeout(buffer, ReadTimeoutMs); }
            catch (Exception ex)
            {
                LibLog.Debug("RawHidBus", $"Read failed on {DisplayName}: {ex.Message}");
                break;
            }
            if (n == 0) continue;
            if (n < 0) break;

            int offset = (n == HidConstants.ReportSize + 1) ? 1 : 0;
            var copy = buffer.AsSpan(offset, n - offset).ToArray();
            ReportReceived?.Invoke(copy);
        }

        // Loop exited on its own (read error / EOF), not via Dispose → surface it.
        // Fired last, as this thread is ending, so the bus disposes us off-thread.
        if (!ct.IsCancellationRequested) Faulted?.Invoke();
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        // Disposing the device unblocks a pending ReadTimeout, like RawHidLayerSource.Stop.
        try { _device?.Dispose(); } catch { }
        try { _runTask?.Wait(1000); } catch { }
        _cts?.Dispose();
        _cts = null;
        _runTask = null;
        _device = null;
    }
}
