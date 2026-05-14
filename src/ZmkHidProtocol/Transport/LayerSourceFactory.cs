namespace ZmkHidProtocol.Transport;

/// <summary>
/// Constructs the unified <see cref="RawHidLayerSource"/> for callers. Kept
/// as a factory (rather than a direct <c>new</c>) so future substitution —
/// tests, alternate transports — has a single seam.
/// </summary>
public static class LayerSourceFactory
{
    public static (ILayerSource Source, ICommandSink Sink) Create(IDeviceMatcher? matcher = null)
    {
        var src = new RawHidLayerSource(matcher);
        return (src, src);
    }
}
