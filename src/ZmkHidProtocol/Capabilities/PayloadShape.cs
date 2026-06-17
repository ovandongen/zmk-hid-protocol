namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// The wire byte-layout of a capability's report payload (the bytes after
/// <c>[0] = action</c>). This is the closed vocabulary that "pins down" the
/// otherwise-open capability namespace: the set is small and finite, a capability
/// picks exactly one, and the builder/UI have one path per shape. Adding a
/// capability that reuses an existing shape is a registry row only; a genuinely
/// new layout is the rare case that adds a shape.
/// </summary>
/// <remarks>
/// Byte offsets are documented per member and mirror <see cref="ActionReportBuilder"/>.
/// </remarks>
public enum PayloadShape
{
    /// <summary>No payload — a bare momentary/trigger action.</summary>
    None,

    /// <summary><c>[1..4]</c> uint32 LE. Pointing dpi/snipe/drag-scroll; scalar meaning per <see cref="ValueKind"/>.</summary>
    Uint32LE,

    /// <summary><c>[1]</c> layer index. <c>core.layer.setBase</c> (0xF4).</summary>
    LayerIndex,

    /// <summary><c>[1..2]</c> ref uint16 LE, <c>[3]</c> layer index. <c>core.layer.activate</c>/<c>deactivate</c> — confirm-acked (host assigns ref).</summary>
    LayerRefIndex,

    /// <summary><c>[1..4]</c> active-layer bitmask uint32 LE. <c>core.layer.set</c> (0xFC) — the app's layer push.</summary>
    LayerSetBitmask,

    /// <summary><c>[1]</c> mask, <c>[2]</c> on, <c>[3..4]</c> hue uint16 LE, <c>[5]</c> sat, <c>[6]</c> val, <c>[7]</c> effect. <c>core.rgb.set</c> (0xD1) — only mask-flagged fields apply.</summary>
    RgbSetMask,

    /// <summary><c>[1]</c> key index, <c>[2..3]</c> hue uint16 LE, <c>[4]</c> sat, <c>[5]</c> val. <c>core.rgb.setKey</c> (0xD2, QMK).</summary>
    RgbSetKey,

    // ── Telemetry (device→host notify) byte layouts ──────────────────────────

    /// <summary>
    /// <c>[1]</c> format marker = <c>0x04</c>, <c>[2..5]</c> default-layer bitmask uint32 LE
    /// (a single bit), <c>[6..9]</c> active-layer bitmask uint32 LE. <c>core.layer.changed</c>
    /// (0xFF notify). Emitters MUST write the marker and a single-bit default mask: the host
    /// validates both to disambiguate this report from a VIA <c>id_unhandled</c> echo (which
    /// also leads with 0xFF) and drops the report if either is absent.
    /// </summary>
    LayerStateBitmask,

    /// <summary><c>[2]</c> matrix position, <c>[3]</c> pressed (0/1). <c>core.keyboard.key.event</c> (0xF1 notify).</summary>
    KeyEvent,

    /// <summary><c>[1]</c> on, <c>[2..3]</c> hue uint16 LE, <c>[4]</c> sat, <c>[5]</c> val, <c>[6]</c> effect. <c>core.rgb.changed</c> (0xD0 notify, full state — no mask).</summary>
    RgbState,

    /// <summary><c>[1]</c> opaque id. <c>signal.fire</c> (0xC0); meaning is host config.</summary>
    SignalId,
}
