namespace ZmkHidProtocol.Building;

/// <summary>
/// Watches <c>ILayerSource.KeyPositionEvent</c> for a single configured
/// physical position and raises <see cref="Triggered"/> when the user
/// taps it the required number of times in a row, with each successive
/// press arriving within the configured window of the previous one.
///
/// <para>Typical use: a host-side "exit gesture" the firmware does not
/// suppress. Pick a position whose binding doesn't emit OS keystrokes
/// (e.g. Moergo's <c>&amp;magic</c>) so the host sees the position
/// events but no spurious text.</para>
///
/// <para>Releases of the configured key are tracked so a firmware-side
/// repeat press (same position, no intervening release) doesn't count
/// as a second tap. Sequences time out when the gap between presses
/// exceeds the window; the next press then starts a fresh count.</para>
/// </summary>
public sealed class MultiTapDetector
{
    private const int DefaultTapCount = 2;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(500);

    private readonly Func<DateTime> _clock;
    private int? _position;
    private int _tapCount = DefaultTapCount;
    private TimeSpan _window = DefaultWindow;

    private int _consecutivePresses;
    private DateTime? _lastPressAt;
    private bool _isDown;

    public MultiTapDetector(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public event Action? Triggered;

    /// <summary>Sets the position to watch. Pass <c>null</c> to disable.
    /// Resets in-flight tap state.</summary>
    public void SetPosition(int? position)
    {
        _position = position;
        ResetState();
    }

    /// <summary>Sets how many taps are required to fire. Default 2.</summary>
    public void SetTapCount(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        _tapCount = count;
        ResetState();
    }

    /// <summary>Sets the maximum gap between successive taps. Default 500 ms.</summary>
    public void SetWindowMs(int ms)
    {
        if (ms < 0) throw new ArgumentOutOfRangeException(nameof(ms));
        _window = TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>Feed key events from <c>ILayerSource.KeyPositionEvent</c>.</summary>
    public void OnKeyEvent(int position, bool pressed)
    {
        if (_position is not int watched || position != watched) return;

        if (pressed)
        {
            // Firmware can re-emit a "pressed" event without an intervening
            // release in retrigger / matrix-scan edge cases. A double-tap
            // requires a real release between presses, so ignore this.
            if (_isDown) return;
            _isDown = true;

            var now = _clock();
            if (_lastPressAt is { } prev && now - prev > _window)
                _consecutivePresses = 0;

            _consecutivePresses++;
            _lastPressAt = now;

            if (_consecutivePresses >= _tapCount)
            {
                ResetState();
                Triggered?.Invoke();
            }
        }
        else
        {
            _isDown = false;
        }
    }

    private void ResetState()
    {
        _consecutivePresses = 0;
        _lastPressAt = null;
        _isDown = false;
    }
}
