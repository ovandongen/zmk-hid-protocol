namespace ZmkHidProtocol.ActiveWindow;

/// <summary>
/// Tracks which application has OS focus and raises <see cref="FocusChanged"/>
/// when it changes. Implementations poll their OS focus API on a background
/// thread; <see cref="FocusChanged"/> fires on that thread, so subscribers
/// must marshal to their UI thread.
/// </summary>
public interface IActiveWindowMonitor : IDisposable
{
    /// <summary>Raised when the focused window/app changes.</summary>
    event Action<ActiveWindowInfo>? FocusChanged;

    /// <summary>Most recently observed focus, or <c>null</c> before the first poll.</summary>
    ActiveWindowInfo? Current { get; }

    /// <summary>Start the polling loop. Idempotent.</summary>
    void Start();

    /// <summary>Stop the polling loop. Idempotent; the monitor can be re-started.</summary>
    void Stop();
}
