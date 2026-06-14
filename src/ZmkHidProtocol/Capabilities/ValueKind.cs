namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Semantic interpretation of a capability's <em>scalar</em> payload, for a host
/// that originates the action (it picks an apt control and bounds the value).
/// Orthogonal to <see cref="PayloadShape"/>: the shape is the wire byte-layout,
/// the value-kind is what a scalar in that layout <em>means</em>. Several actions
/// share one shape (<see cref="PayloadShape.Uint32LE"/>) but differ only by kind.
/// Structured shapes (RGB, layer-ref) carry their meaning in the shape itself and
/// use <see cref="None"/>.
/// </summary>
public enum ValueKind
{
    /// <summary>Payload isn't a single interpreted scalar (structured or no payload).</summary>
    None,

    /// <summary>On/off mode carried as 0 or 1; firmware treats any nonzero as on. → checkbox.</summary>
    Toggle,

    /// <summary>Absolute magnitude, e.g. DPI 400/800/1600. → numeric field.</summary>
    Count,

    /// <summary>Zero-based slot into a device preset list (e.g. DPI presets). → numeric field.</summary>
    Index,
}
