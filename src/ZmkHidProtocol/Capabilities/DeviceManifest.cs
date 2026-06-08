namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// A device's full "describe yourself" answer, reassembled from a 0xF8
/// manifest stream: identity (name plus optional configId) and the list of
/// capabilities it notifies / handles / triggers.
/// </summary>
public sealed record DeviceManifest(
    string? Name,
    string? ConfigId,
    IReadOnlyList<Capability> Capabilities);
