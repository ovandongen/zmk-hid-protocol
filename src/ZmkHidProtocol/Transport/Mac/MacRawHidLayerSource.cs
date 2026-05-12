using System.Runtime.InteropServices;
using ZmkHidProtocol.Diagnostics;
using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Transport.Mac;

/// <summary>
/// Raw-HID layer source for macOS. Uses Apple's IOHIDManager via direct
/// P/Invoke into <c>IOKit.framework</c> + <c>CoreFoundation.framework</c>.
///
/// <para>HidSharp's macOS backend only sees the legacy <c>IOHIDDevice</c>
/// USB-attached class. BLE-HoGP devices live under <c>AppleUserHIDDevice</c>
/// and are invisible there. The IOHIDManager API enumerates both transports
/// uniformly.</para>
///
/// <para>Threading: a dedicated background thread owns a CFRunLoop;
/// matching/removal/input-report callbacks fire on it. <see cref="LayerChanged"/>,
/// <see cref="KeyPositionEvent"/>, <see cref="ReportReceived"/>, and
/// <see cref="ConnectionChanged"/> are raised on the same thread.</para>
///
/// <para><see cref="SendReportAsync"/> calls <c>IOHIDDeviceSetReport</c>
/// synchronously under a lock; Apple documents the API as thread-safe.</para>
/// </summary>
public sealed class MacRawHidLayerSource : ILayerSource, ICommandSink
{
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static readonly IntPtr kCFRunLoopDefaultMode = CFStringCreate("kCFRunLoopDefaultMode");
    private const uint kIOReturnSuccess = 0;
    private const uint kIOHIDOptionsTypeNone = 0;
    private const uint kIOHIDReportTypeOutput = 1;

    [DllImport(IOKit)] private static extern IntPtr IOHIDManagerCreate(IntPtr allocator, uint options);
    [DllImport(IOKit)] private static extern void IOHIDManagerSetDeviceMatching(IntPtr manager, IntPtr matchingDict);
    [DllImport(IOKit)] private static extern uint IOHIDManagerOpen(IntPtr manager, uint options);
    [DllImport(IOKit)] private static extern uint IOHIDManagerClose(IntPtr manager, uint options);
    [DllImport(IOKit)] private static extern void IOHIDManagerScheduleWithRunLoop(IntPtr manager, IntPtr runLoop, IntPtr mode);
    [DllImport(IOKit)] private static extern void IOHIDManagerUnscheduleFromRunLoop(IntPtr manager, IntPtr runLoop, IntPtr mode);
    [DllImport(IOKit)] private static extern void IOHIDManagerRegisterDeviceMatchingCallback(IntPtr manager, IOHIDDeviceCallback callback, IntPtr context);
    [DllImport(IOKit)] private static extern void IOHIDManagerRegisterDeviceRemovalCallback(IntPtr manager, IOHIDDeviceCallback callback, IntPtr context);

    [DllImport(IOKit)] private static extern uint IOHIDDeviceOpen(IntPtr device, uint options);
    [DllImport(IOKit)] private static extern uint IOHIDDeviceClose(IntPtr device, uint options);
    [DllImport(IOKit)] private static extern void IOHIDDeviceRegisterInputReportCallback(IntPtr device, IntPtr report, nint reportLength, IOHIDReportCallback callback, IntPtr context);
    [DllImport(IOKit)] private static extern IntPtr IOHIDDeviceGetProperty(IntPtr device, IntPtr key);
    [DllImport(IOKit)] private static extern IntPtr IOHIDManagerCopyDevices(IntPtr manager);
    [DllImport(IOKit)] private static extern uint IOHIDDeviceSetReport(IntPtr device, uint reportType, nint reportID, byte[] report, nint reportLength);

    [DllImport(CoreFoundation)] private static extern nint CFSetGetCount(IntPtr theSet);
    [DllImport(CoreFoundation)] private static extern void CFSetGetValues(IntPtr theSet, IntPtr[] values);

    [DllImport(CoreFoundation)] private static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)] private static extern void CFRunLoopRun();
    [DllImport(CoreFoundation)] private static extern void CFRunLoopStop(IntPtr runLoop);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFNumberCreate(IntPtr alloc, int type, ref int value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreate(IntPtr alloc, IntPtr[] keys, IntPtr[] values, nint numValues, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
    [DllImport(CoreFoundation)] private static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, nint bufferSize, uint encoding);
    [DllImport(CoreFoundation)] private static extern nint CFStringGetLength(IntPtr theString);
    [DllImport(CoreFoundation)] private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);

    private const int kCFNumberSInt32Type = 3;
    private const uint kCFStringEncodingUTF8 = 0x08000100;
    private static readonly IntPtr kCFTypeDictionaryKeyCallBacks = DlsymCF("kCFTypeDictionaryKeyCallBacks");
    private static readonly IntPtr kCFTypeDictionaryValueCallBacks = DlsymCF("kCFTypeDictionaryValueCallBacks");

    [DllImport("libdl.dylib")] private static extern IntPtr dlopen(string path, int flags);
    [DllImport("libdl.dylib")] private static extern IntPtr dlsym(IntPtr handle, string symbol);
    private const int RTLD_NOW = 2;

    private static IntPtr DlsymCF(string symbol)
    {
        var h = dlopen(CoreFoundation, RTLD_NOW);
        return h == IntPtr.Zero ? IntPtr.Zero : dlsym(h, symbol);
    }

    private static IntPtr CFStringCreate(string s) =>
        CFStringCreateWithCString(IntPtr.Zero, s, kCFStringEncodingUTF8);

    private delegate void IOHIDDeviceCallback(IntPtr context, uint result, IntPtr sender, IntPtr device);
    private delegate void IOHIDReportCallback(IntPtr context, uint result, IntPtr sender, uint reportType, uint reportID, IntPtr report, nint reportLength);

    private volatile IDeviceMatcher? _matcher;
    private volatile bool _connected;
    private volatile int _currentLayer;
    private string _sourceName = "Raw HID";
    private Thread? _thread;
    private IntPtr _runLoop;
    private IntPtr _manager;
    private IOHIDDeviceCallback? _matchCb;
    private IOHIDDeviceCallback? _removalCb;
    private IOHIDReportCallback? _reportCb;
    private readonly Dictionary<IntPtr, IntPtr> _deviceBuffers = new();
    private readonly object _gate = new();
    private IntPtr _writeDevice;
    private readonly object _writeLock = new();

    public MacRawHidLayerSource(IDeviceMatcher? matcher = null)
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
        var rl = _runLoop;
        if (rl != IntPtr.Zero) PerformOnRunLoop(ReevaluateOpenDevices);
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(RunLoopThread) { IsBackground = true, Name = "MacRawHid" };
        _thread.Start();
        LibLog.Info("MacRawHid", "MacRawHidLayerSource started");
    }

    public void Stop()
    {
        var t = _thread;
        if (t is null) return;
        _thread = null;
        if (_runLoop != IntPtr.Zero) CFRunLoopStop(_runLoop);
        try { t.Join(2000); } catch { }
        SetConnected(false);
    }

    public void Dispose() => Stop();

    public ValueTask SendReportAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IntPtr device = _writeDevice;
        if (device == IntPtr.Zero) throw new InvalidOperationException("MacRawHidLayerSource: device is not open.");
        // IOHIDDeviceSetReport is documented thread-safe and synchronous;
        // serialise to avoid interleaving partial reports.
        lock (_writeLock)
        {
            var buf = report.ToArray();
            // reportID 0 = no report ID prefix (matches firmware's no-id raw HID).
            var rc = IOHIDDeviceSetReport(device, kIOHIDReportTypeOutput, 0, buf, buf.Length);
            if (rc != kIOReturnSuccess)
                throw new IOException($"IOHIDDeviceSetReport returned 0x{rc:X8}");
        }
        return ValueTask.CompletedTask;
    }

    private void RunLoopThread()
    {
        try
        {
            _runLoop = CFRunLoopGetCurrent();
            _manager = IOHIDManagerCreate(IntPtr.Zero, kIOHIDOptionsTypeNone);
            if (_manager == IntPtr.Zero)
            {
                LibLog.Warn("MacRawHid", "IOHIDManagerCreate returned NULL");
                return;
            }

            IntPtr matchDict = BuildUsageMatchDictionary(HidConstants.UsagePage, HidConstants.UsageId);
            IOHIDManagerSetDeviceMatching(_manager, matchDict);
            CFRelease(matchDict);

            _matchCb = OnDeviceMatching;
            _removalCb = OnDeviceRemoval;
            _reportCb = OnInputReport;
            IOHIDManagerRegisterDeviceMatchingCallback(_manager, _matchCb, IntPtr.Zero);
            IOHIDManagerRegisterDeviceRemovalCallback(_manager, _removalCb, IntPtr.Zero);
            IOHIDManagerScheduleWithRunLoop(_manager, _runLoop, kCFRunLoopDefaultMode);

            var openResult = IOHIDManagerOpen(_manager, kIOHIDOptionsTypeNone);
            if (openResult != kIOReturnSuccess)
                LibLog.Warn("MacRawHid", $"IOHIDManagerOpen returned 0x{openResult:X8}");

            CFRunLoopRun();
        }
        catch (Exception ex)
        {
            LibLog.Warn("MacRawHid", $"Run loop thread crashed: {ex}");
        }
        finally
        {
            try
            {
                CleanupOpenDevices();
                if (_manager != IntPtr.Zero)
                {
                    IOHIDManagerUnscheduleFromRunLoop(_manager, _runLoop, kCFRunLoopDefaultMode);
                    IOHIDManagerClose(_manager, kIOHIDOptionsTypeNone);
                    CFRelease(_manager);
                    _manager = IntPtr.Zero;
                }
            }
            catch { }
            _runLoop = IntPtr.Zero;
            _matchCb = null;
            _removalCb = null;
            _reportCb = null;
        }
    }

    private static IntPtr BuildUsageMatchDictionary(int usagePage, int usage)
    {
        var pageKey = CFStringCreate("DeviceUsagePage");
        var usageKey = CFStringCreate("DeviceUsage");
        var pageVal = CFNumberCreate(IntPtr.Zero, kCFNumberSInt32Type, ref usagePage);
        var usageVal = CFNumberCreate(IntPtr.Zero, kCFNumberSInt32Type, ref usage);
        var keys = new[] { pageKey, usageKey };
        var values = new[] { pageVal, usageVal };
        var dict = CFDictionaryCreate(IntPtr.Zero, keys, values, 2,
            kCFTypeDictionaryKeyCallBacks, kCFTypeDictionaryValueCallBacks);
        CFRelease(pageKey); CFRelease(usageKey);
        CFRelease(pageVal); CFRelease(usageVal);
        return dict;
    }

    private void OnDeviceMatching(IntPtr context, uint result, IntPtr sender, IntPtr device)
    {
        try
        {
            int vid = ReadIntProperty(device, "VendorID");
            int pid = ReadIntProperty(device, "ProductID");
            string? product = ReadStringProperty(device, "Product");

            var matcher = _matcher;
            if (matcher is not null && !matcher.Matches(vid, pid, product))
            {
                LibLog.Debug("MacRawHid",
                    $"Skipping non-matching device VID={vid:X4} PID={pid:X4} Product='{product}'");
                return;
            }

            var openResult = IOHIDDeviceOpen(device, kIOHIDOptionsTypeNone);
            if (openResult != kIOReturnSuccess)
            {
                LibLog.Warn("MacRawHid",
                    $"IOHIDDeviceOpen failed (0x{openResult:X8}) for VID={vid:X4} PID={pid:X4} Product='{product}'");
                return;
            }

            var buffer = Marshal.AllocHGlobal(HidConstants.ReportSize);
            lock (_gate) _deviceBuffers[device] = buffer;
            IOHIDDeviceRegisterInputReportCallback(device, buffer, HidConstants.ReportSize, _reportCb!, device);
            _writeDevice = device;

            _sourceName = string.IsNullOrWhiteSpace(product) ? "Raw HID" : $"Raw HID ({product})";
            LibLog.Info("MacRawHid", $"Connected: {_sourceName}");
            SetConnected(true);
        }
        catch (Exception ex)
        {
            LibLog.Warn("MacRawHid", $"OnDeviceMatching threw: {ex}");
        }
    }

    private void OnDeviceRemoval(IntPtr context, uint result, IntPtr sender, IntPtr device)
    {
        try
        {
            ClosePerDevice(device);
            bool any;
            lock (_gate) any = _deviceBuffers.Count > 0;
            if (!any)
            {
                LibLog.Info("MacRawHid", "Disconnected");
                SetConnected(false);
            }
        }
        catch (Exception ex)
        {
            LibLog.Warn("MacRawHid", $"OnDeviceRemoval threw: {ex}");
        }
    }

    private readonly byte[] _reportScratch = new byte[64];

    private void OnInputReport(IntPtr context, uint result, IntPtr sender, uint reportType, uint reportID, IntPtr report, nint reportLength)
    {
        if (reportLength <= 0) return;
        try
        {
            int len = Math.Min((int)reportLength, _reportScratch.Length);
            Marshal.Copy(report, _reportScratch, 0, len);
            DispatchReport(new ReadOnlySpan<byte>(_reportScratch, 0, len));
        }
        catch (Exception ex)
        {
            LibLog.Warn("MacRawHid", $"OnInputReport: {ex.Message}");
        }
    }

    private void DispatchReport(ReadOnlySpan<byte> payload)
    {
        var copy = payload.ToArray();
        ReportReceived?.Invoke(copy);

        var layer = RawHidProtocol.TryParseLayerState(payload);
        if (layer is int l)
        {
            _currentLayer = l;
            LayerChanged?.Invoke(l);
            return;
        }
        var key = RawHidProtocol.TryParseKeyEvent(payload);
        if (key is { } k) KeyPositionEvent?.Invoke(k.Position, k.Pressed);
    }

    private void ReevaluateOpenDevices()
    {
        var matcher = _matcher;
        IntPtr[] toClose;
        lock (_gate) toClose = _deviceBuffers.Keys.ToArray();
        foreach (var device in toClose)
        {
            int vid = ReadIntProperty(device, "VendorID");
            int pid = ReadIntProperty(device, "ProductID");
            string? product = ReadStringProperty(device, "Product");
            if (matcher is not null && !matcher.Matches(vid, pid, product))
            {
                LibLog.Info("MacRawHid",
                    $"Closing stale device after matcher change: Product='{product}'");
                ClosePerDevice(device);
            }
        }

        if (_manager != IntPtr.Zero)
        {
            var set = IOHIDManagerCopyDevices(_manager);
            if (set != IntPtr.Zero)
            {
                try
                {
                    int count = (int)CFSetGetCount(set);
                    if (count > 0)
                    {
                        var devices = new IntPtr[count];
                        CFSetGetValues(set, devices);
                        foreach (var device in devices)
                        {
                            bool alreadyOpen;
                            lock (_gate) alreadyOpen = _deviceBuffers.ContainsKey(device);
                            if (!alreadyOpen)
                                OnDeviceMatching(IntPtr.Zero, kIOReturnSuccess, IntPtr.Zero, device);
                        }
                    }
                }
                finally { CFRelease(set); }
            }
        }

        bool any;
        lock (_gate) any = _deviceBuffers.Count > 0;
        if (!any) SetConnected(false);
    }

    private void ClosePerDevice(IntPtr device)
    {
        IntPtr buffer = IntPtr.Zero;
        lock (_gate)
        {
            if (_deviceBuffers.TryGetValue(device, out buffer))
                _deviceBuffers.Remove(device);
        }
        if (_writeDevice == device) _writeDevice = IntPtr.Zero;
        try { IOHIDDeviceClose(device, kIOHIDOptionsTypeNone); } catch { }
        if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
    }

    private void CleanupOpenDevices()
    {
        IntPtr[] all;
        lock (_gate) all = _deviceBuffers.Keys.ToArray();
        foreach (var d in all) ClosePerDevice(d);
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        ConnectionChanged?.Invoke();
    }

    private static void PerformOnRunLoop(Action action) => action();

    private static int ReadIntProperty(IntPtr device, string key)
    {
        var keyRef = CFStringCreate(key);
        try
        {
            var val = IOHIDDeviceGetProperty(device, keyRef);
            if (val == IntPtr.Zero) return 0;
            return CFNumberGetValue(val, kCFNumberSInt32Type, out int n) ? n : 0;
        }
        finally { CFRelease(keyRef); }
    }

    private static string? ReadStringProperty(IntPtr device, string key)
    {
        var keyRef = CFStringCreate(key);
        try
        {
            var val = IOHIDDeviceGetProperty(device, keyRef);
            if (val == IntPtr.Zero) return null;
            var len = (int)CFStringGetLength(val);
            if (len <= 0) return "";
            var buf = new byte[len * 4 + 1];
            return CFStringGetCString(val, buf, buf.Length, kCFStringEncodingUTF8)
                ? System.Text.Encoding.UTF8.GetString(buf, 0, Array.IndexOf(buf, (byte)0))
                : null;
        }
        finally { CFRelease(keyRef); }
    }
}
