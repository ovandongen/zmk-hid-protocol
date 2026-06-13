namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// A decoded <c>core.rgb.changed</c> (0xD0) report: the device's latched global
/// RGB state. When <see cref="Effect"/> is a per-key/layer mode index, the host
/// should treat <see cref="Hue"/>/<see cref="Sat"/> as dormant (sourced
/// per-binding on-device, not the live colour) — see the RGB capability notes.
/// </summary>
public readonly record struct RgbState(bool On, ushort Hue, byte Sat, byte Val, byte Effect);

/// <summary>
/// A <c>core.rgb.set</c> (0xD1) request. Every field is optional and absolute:
/// only the fields that are non-null are applied, the rest are left untouched
/// (the wire carries a presence mask). Set just the knobs you want to change.
/// </summary>
public readonly record struct RgbSet(
    bool? On = null,
    ushort? Hue = null,
    byte? Sat = null,
    byte? Val = null,
    byte? Effect = null);
