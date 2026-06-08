namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// One non-identity row of a device manifest: a namespaced capability id
/// (e.g. <c>"core.pointing.dpi.set"</c>) tagged with the role the device plays
/// for it, its tier, and whether handled invocations are confirm-acknowledged.
/// </summary>
public sealed record Capability(CapabilityRole Role, CapabilityTier Tier, bool Confirm, string Id);
