using ZmkHidProtocol.Protocol;

namespace ZmkHidProtocol.Building;

/// <summary>
/// Tracks the keyboard's active-layer bitmask over time and exposes a
/// save-slot for the app's "switch then later restore" flow.
///
/// <para>The app calls <see cref="ExpectAppState"/> with the bitmask it is
/// about to push via <c>CommandSender.SetLayerStateAsync</c>; the next
/// inbound 0xFF report that matches flips <see cref="IsAppControlled"/> to
/// <c>true</c>. Any subsequent change that doesn't match the expectation
/// (user pressed a layer key, momentary layer engaged, etc.) flips it back
/// to <c>false</c>.</para>
///
/// <para>Firmware quirk: layer 0 is always active on the wire. The
/// expected mask passed to <see cref="ExpectAppState"/> is OR'd with
/// bit 0 internally so callers don't have to think about it.</para>
/// </summary>
public sealed class LayerStateTracker
{
    private uint? _expectedAppBitmask;

    public uint CurrentBitmask { get; private set; }

    public int HighestActiveLayer => RawHidProtocol.HighestActiveLayer(CurrentBitmask);

    public uint? SavedBitmask { get; private set; }

    public bool IsAppControlled { get; private set; }

    /// <summary>Raised when <see cref="CurrentBitmask"/> changes value.</summary>
    public event Action<uint>? StateChanged;

    /// <summary>Feed the active-layer bitmask from a parsed 0xFF report.</summary>
    public void OnLayerState(uint bitmask)
    {
        if (bitmask == CurrentBitmask) return;
        CurrentBitmask = bitmask;
        if (_expectedAppBitmask is uint expected && bitmask == expected)
            IsAppControlled = true;
        else
            IsAppControlled = false;
        _expectedAppBitmask = null;
        StateChanged?.Invoke(bitmask);
    }

    /// <summary>Convenience: feed the raw 32-byte report from <c>ILayerSource.ReportReceived</c>.</summary>
    public void OnReport(ReadOnlyMemory<byte> report)
    {
        if (RawHidProtocol.TryParseLayerStateBitmask(report.Span) is uint bitmask)
            OnLayerState(bitmask);
    }

    /// <summary>Snapshots the current bitmask into <see cref="SavedBitmask"/>.</summary>
    public void SaveState() => SavedBitmask = CurrentBitmask;

    /// <summary>Clears the saved bitmask.</summary>
    public void ClearSavedState() => SavedBitmask = null;

    /// <summary>
    /// Arms the tracker so the next inbound state matching <paramref name="bitmask"/>
    /// is flagged as app-controlled. Bit 0 is OR'd in to mirror the firmware's
    /// always-active layer-0 quirk.
    /// </summary>
    public void ExpectAppState(uint bitmask) => _expectedAppBitmask = bitmask | 1u;
}
