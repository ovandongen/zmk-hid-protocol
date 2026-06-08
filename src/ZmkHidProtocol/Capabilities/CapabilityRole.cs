namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// What a device does with a capability, as advertised in its manifest
/// (byte 3 of a 0xF8 entry). Mirrors the firmware's ROLE_* constants.
/// </summary>
public enum CapabilityRole : byte
{
    /// <summary>Discovery metadata (board name, configId) rather than a capability.</summary>
    Identity = 0,

    /// <summary>Device publishes this state/event to the host.</summary>
    Notifies = 1,

    /// <summary>Device accepts this action from the host.</summary>
    Handles = 2,

    /// <summary>
    /// Device originates this action (fire-and-forget) for another device to
    /// handle. A trigger references a <see cref="Handles"/> id on a different
    /// device — it is never the device's own id.
    /// </summary>
    Triggers = 3,
}
