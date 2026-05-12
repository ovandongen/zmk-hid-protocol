namespace ZmkHidProtocol.Building;

/// <summary>
/// Watches <c>ILayerSource.KeyPositionEvent</c> for a configured set of
/// physical positions and raises <see cref="ComboTriggered"/> when all of
/// them are held simultaneously within the configured timeout window.
///
/// <para>Combo positions are typically bound to <c>&amp;none</c> in the
/// keymap so the keys don't emit keycodes to the OS. The firmware still
/// surfaces them as 0xF1 key events, which is what this detector consumes.</para>
///
/// <para>Position-based: combos fire regardless of the current layer.
/// Stateless across timeouts: once fired, the detector arms again only after
/// all combo keys have been released.</para>
/// </summary>
public sealed class ComboDetector
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(50);

    private readonly Func<DateTime> _clock;
    private TimeSpan _timeout = DefaultTimeout;
    private HashSet<int> _comboPositions = new();
    private readonly Dictionary<int, DateTime> _pressedAt = new();
    private bool _firedThisRound;

    public ComboDetector(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public event Action? ComboTriggered;

    /// <summary>Replaces the set of positions that form the combo. Resets in-flight state.</summary>
    public void SetComboPositions(IEnumerable<int> positions)
    {
        _comboPositions = new HashSet<int>(positions ?? throw new ArgumentNullException(nameof(positions)));
        _pressedAt.Clear();
        _firedThisRound = false;
    }

    /// <summary>Sets the maximum spread between first and last combo-key press. Default 50 ms.</summary>
    public void SetTimeoutMs(int ms)
    {
        if (ms < 0) throw new ArgumentOutOfRangeException(nameof(ms));
        _timeout = TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>Feed key events from <c>ILayerSource.KeyPositionEvent</c>.</summary>
    public void OnKeyEvent(int position, bool pressed)
    {
        if (_comboPositions.Count == 0 || !_comboPositions.Contains(position))
            return;

        if (pressed)
        {
            _pressedAt[position] = _clock();
            if (!_firedThisRound && _pressedAt.Count == _comboPositions.Count)
            {
                var min = _pressedAt.Values.Min();
                var max = _pressedAt.Values.Max();
                if (max - min <= _timeout)
                {
                    _firedThisRound = true;
                    ComboTriggered?.Invoke();
                }
            }
        }
        else
        {
            _pressedAt.Remove(position);
            if (_pressedAt.Count == 0)
                _firedThisRound = false;
        }
    }
}
