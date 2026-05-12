using System.Diagnostics;

namespace ZmkHidProtocol.Diagnostics;

/// <summary>
/// Lightweight subsystem-tagged log shim. Routes through
/// <see cref="System.Diagnostics.Trace"/> so consumers can plug in their own
/// <c>TraceListener</c> without forcing this library to depend on a logging
/// framework. Messages are prefixed with <c>[ZmkHidProtocol:&lt;subsystem&gt;]</c>.
/// </summary>
internal static class LibLog
{
    public static void Info(string subsystem, string message) =>
        Trace.WriteLine($"[ZmkHidProtocol:{subsystem}] {message}");

    public static void Warn(string subsystem, string message) =>
        Trace.TraceWarning($"[ZmkHidProtocol:{subsystem}] {message}");

    public static void Debug(string subsystem, string message) =>
        Trace.WriteLine($"[ZmkHidProtocol:{subsystem}] {message}");
}
