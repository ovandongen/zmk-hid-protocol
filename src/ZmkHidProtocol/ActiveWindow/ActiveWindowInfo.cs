namespace ZmkHidProtocol.ActiveWindow;

/// <summary>
/// Snapshot of the currently focused application. The app uses this
/// alongside its own app→layer mapping to decide whether to switch layers.
/// </summary>
/// <param name="ProcessName">
/// Short, OS-style executable name without extension or path
/// (e.g. <c>"Code"</c>, <c>"firefox"</c>, <c>"excel"</c>).
/// </param>
/// <param name="BundleId">
/// macOS reverse-DNS bundle identifier (e.g. <c>"com.microsoft.VSCode"</c>).
/// <c>null</c> on Windows and Linux.
/// </param>
/// <param name="WindowTitle">
/// Title-bar text of the active window. May be <c>null</c> when the
/// platform doesn't expose it without elevated permissions (macOS).
/// </param>
public sealed record ActiveWindowInfo(string ProcessName, string? BundleId, string? WindowTitle);
