namespace ZmkHidProtocol.Transport;

/// <summary>
/// Process-wide serialization for hidapi <c>enumerate</c> and device <c>open</c>.
///
/// <para>hidapi's macOS backend builds and schedules an <c>IOHIDManager</c> on a
/// run loop inside <c>hid_enumerate</c> / <c>hid_init</c>, and it is <b>not</b>
/// thread-safe: two threads enumerating (or initializing) concurrently abort the
/// whole process — <c>"Schedule failed queue …"</c> → SIGKILL. LViz runs two
/// independent discovery loops on separate threads — the keyboard
/// <see cref="RawHidLayerSource"/> (reconnect tick) and the capability
/// <see cref="RawHidDeviceBus"/> (hot-plug poll) — so every <c>Hid.Enumerate()</c>
/// and every device open must hold this gate to keep them from overlapping.</para>
///
/// <para>Reads and writes on an <em>already-open</em> handle are per-device and
/// must <b>not</b> take this lock — the read loop blocks for the read timeout and
/// would otherwise stall all discovery.</para>
/// </summary>
internal static class HidGlobalLock
{
    /// <summary>Hold around <c>Hid.Enumerate()</c> (materialize inside) and device open only.</summary>
    public static readonly object Gate = new();
}
