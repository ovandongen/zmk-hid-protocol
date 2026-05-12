using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;
using ZmkHidProtocol.Diagnostics;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Transport.Windows;

/// <summary>
/// Raw-HID layer source for Windows over Bluetooth LE via WinRT GATT.
///
/// <para>Talks to the vendor GATT service exposed by the
/// <a href="https://github.com/ovandongen/zmk-hid-viz">zmk-hid-viz</a>
/// firmware. Notifications come down the TX characteristic
/// (<see cref="HidConstants.BleVendorService.TxCharacteristicUuid"/>); host
/// writes go up the RX characteristic
/// (<see cref="HidConstants.BleVendorService.RxCharacteristicUuid"/>).</para>
///
/// <para>Why a custom service: Windows' HoGP kernel driver claims the
/// standard BLE HID service (0x1812) exclusively. The firmware sidesteps
/// HoGP by exposing the same raw-HID reports under a vendor UUID Windows
/// cannot claim. macOS and Linux are unaffected and use the standard service.</para>
///
/// <para>Matching uses <see cref="IDeviceMatcher.MatchesName"/> only —
/// BluetoothLEDevice does not expose USB-style VID/PID.</para>
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsBleRawHidLayerSource : ILayerSource, ICommandSink
{
    private const int RescanDelayMs = 3000;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private volatile bool _connected;
    private volatile int _currentLayer;
    private volatile IDeviceMatcher? _matcher;
    private string _sourceName = "Raw HID (BLE)";
    private readonly ManualResetEventSlim _rescan = new(false);
    private GattCharacteristic? _rxCharacteristic;
    private readonly object _writeLock = new();

    public WindowsBleRawHidLayerSource(IDeviceMatcher? matcher = null)
    {
        _matcher = matcher;
    }

    public event Action<int>? LayerChanged;
    public event Action<int, bool>? KeyPositionEvent;
    public event Action<ReadOnlyMemory<byte>>? ReportReceived;
    public event Action? ConnectionChanged;

    public bool IsConnected => _connected;
    public int CurrentLayer => _currentLayer;
    public string SourceName => _sourceName;

    public void SetMatcher(IDeviceMatcher? matcher)
    {
        _matcher = matcher;
        _rescan.Set();
    }

    public void Start()
    {
        if (_runTask is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _runTask = Task.Run(() => RunLoop(ct));
        LibLog.Info("WinBleHid", "WindowsBleRawHidLayerSource started");
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
        SetConnected(false);
    }

    public void Dispose()
    {
        Stop();
        _rescan.Dispose();
    }

    public async ValueTask SendReportAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rx = _rxCharacteristic;
        if (rx is null) throw new InvalidOperationException("WindowsBleRawHidLayerSource: device is not open.");

        using var writer = new DataWriter();
        writer.WriteBytes(report.ToArray());
        var buf = writer.DetachBuffer();

        // GATT writes are inherently sequential; the lock protects against
        // multiple SendReportAsync calls racing on the same characteristic.
        IAsyncOperation<GattCommunicationStatus> op;
        lock (_writeLock)
        {
            op = rx.WriteValueAsync(buf, GattWriteOption.WriteWithResponse);
        }
        var status = await op.AsTask(cancellationToken).ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
            throw new IOException($"BLE RX WriteValueAsync returned {status}");
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            BluetoothLEDevice? device = null;
            GattDeviceService? vendorService = null;
            var subscribed = new List<GattCharacteristic>();
            TypedEventHandler<BluetoothLEDevice, object>? onConn = null;

            try
            {
                device = await TryFindMatchingDevice(ct);
                if (device is null)
                {
                    await WaitForRescanOrTimeout(RescanDelayMs, ct);
                    continue;
                }

                var sr = await device.GetGattServicesForUuidAsync(
                    HidConstants.BleVendorService.ServiceUuid, BluetoothCacheMode.Uncached);
                if (sr.Status != GattCommunicationStatus.Success || sr.Services.Count == 0)
                {
                    LibLog.Info("WinBleHid",
                        $"Device '{device.Name}' does not expose the zmk-hid-viz vendor GATT service " +
                        $"(status={sr.Status}); the firmware may not include the hid-viz module. Skipping.");
                    await WaitForRescanOrTimeout(RescanDelayMs, ct);
                    continue;
                }
                vendorService = sr.Services[0];

                var openStatus = await vendorService.OpenAsync(GattSharingMode.SharedReadAndWrite);
                if (openStatus != GattOpenStatus.Success && openStatus != GattOpenStatus.AlreadyOpened)
                {
                    LibLog.Warn("WinBleHid", $"vendorService.OpenAsync → {openStatus}; will retry.");
                    await WaitForRescanOrTimeout(RescanDelayMs, ct);
                    continue;
                }

                var txResult = await vendorService.GetCharacteristicsForUuidAsync(
                    HidConstants.BleVendorService.TxCharacteristicUuid, BluetoothCacheMode.Uncached);
                if (txResult.Status != GattCommunicationStatus.Success)
                {
                    LibLog.Warn("WinBleHid", $"GetCharacteristics for TX failed: {txResult.Status}");
                    await WaitForRescanOrTimeout(RescanDelayMs, ct);
                    continue;
                }

                foreach (var ch in txResult.Characteristics)
                {
                    if (!ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)) continue;
                    var status = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    if (status != GattCommunicationStatus.Success)
                    {
                        LibLog.Debug("WinBleHid", $"CCCD write failed on TX char: {status}");
                        continue;
                    }
                    ch.ValueChanged += OnReportNotification;
                    subscribed.Add(ch);
                }

                if (subscribed.Count == 0)
                {
                    LibLog.Warn("WinBleHid",
                        $"Device '{device.Name}' exposes the vendor service but no Notify-capable TX characteristic.");
                    await WaitForRescanOrTimeout(RescanDelayMs, ct);
                    continue;
                }

                // RX characteristic for writes — optional; without it
                // SendReportAsync will throw, but the read-only path keeps working.
                var rxResult = await vendorService.GetCharacteristicsForUuidAsync(
                    HidConstants.BleVendorService.RxCharacteristicUuid, BluetoothCacheMode.Uncached);
                if (rxResult.Status == GattCommunicationStatus.Success && rxResult.Characteristics.Count > 0)
                {
                    _rxCharacteristic = rxResult.Characteristics[0];
                }
                else
                {
                    LibLog.Debug("WinBleHid", $"RX characteristic not present (status={rxResult.Status}); writes will be unavailable.");
                }

                _sourceName = string.IsNullOrWhiteSpace(device.Name)
                    ? "Raw HID (BLE)"
                    : $"Raw HID ({device.Name}, BLE)";
                LibLog.Info("WinBleHid",
                    $"Connected: {_sourceName} — subscribed to TX, RX {(_rxCharacteristic is null ? "absent" : "ready")}");
                SetConnected(true);

                var disconnected = new TaskCompletionSource();
                onConn = (s, _) =>
                {
                    if (s.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                        disconnected.TrySetResult();
                };
                device.ConnectionStatusChanged += onConn;

                while (!ct.IsCancellationRequested && !disconnected.Task.IsCompleted)
                {
                    _rescan.Reset();
                    var rescanTask = Task.Run(() => _rescan.Wait(ct));
                    var completed = await Task.WhenAny(disconnected.Task, rescanTask);
                    if (completed == rescanTask) break;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                LibLog.Warn("WinBleHid", $"BLE loop error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (onConn is not null && device is not null)
                    device.ConnectionStatusChanged -= onConn;
                foreach (var ch in subscribed)
                    ch.ValueChanged -= OnReportNotification;
                _rxCharacteristic = null;
                vendorService?.Dispose();
                device?.Dispose();
                if (_connected)
                {
                    SetConnected(false);
                    LibLog.Info("WinBleHid", "Disconnected");
                }
            }

            await WaitForRescanOrTimeout(RescanDelayMs, ct);
        }
    }

    private async Task<BluetoothLEDevice?> TryFindMatchingDevice(CancellationToken ct)
    {
        var matcher = _matcher;
        DeviceInformationCollection devInfos;
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            devInfos = await DeviceInformation.FindAllAsync(selector);
        }
        catch (Exception ex)
        {
            LibLog.Debug("WinBleHid", $"Paired-BLE enumeration failed: {ex.Message}");
            return null;
        }

        foreach (var info in devInfos)
        {
            if (ct.IsCancellationRequested) return null;
            BluetoothLEDevice? dev;
            try { dev = await BluetoothLEDevice.FromIdAsync(info.Id); }
            catch { dev = null; }
            if (dev is null) continue;

            if (matcher is not null && !matcher.MatchesName(dev.Name))
            {
                dev.Dispose();
                continue;
            }

            return dev;
        }

        return null;
    }

    private void OnReportNotification(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var buf = args.CharacteristicValue;
            if (buf is null || buf.Length == 0) return;
            var reader = DataReader.FromBuffer(buf);
            var bytes = new byte[buf.Length];
            reader.ReadBytes(bytes);

            ReportReceived?.Invoke(bytes);

            var layer = RawHidProtocol.TryParseLayerState(bytes);
            if (layer is int l)
            {
                _currentLayer = l;
                LayerChanged?.Invoke(l);
                return;
            }
            var key = RawHidProtocol.TryParseKeyEvent(bytes);
            if (key is { } k)
                KeyPositionEvent?.Invoke(k.Position, k.Pressed);
        }
        catch (Exception ex)
        {
            LibLog.Warn("WinBleHid", $"OnReportNotification: {ex.Message}");
        }
    }

    private async Task WaitForRescanOrTimeout(int ms, CancellationToken ct)
    {
        _rescan.Reset();
        try { await Task.Run(() => _rescan.Wait(ms, ct), ct); }
        catch (OperationCanceledException) { }
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        ConnectionChanged?.Invoke();
    }
}
