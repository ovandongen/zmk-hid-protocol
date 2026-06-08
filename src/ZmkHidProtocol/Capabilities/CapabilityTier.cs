namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Availability/stability of a capability (byte 4 of a non-identity 0xF8
/// entry). Metadata only — the tier never appears in the capability id, so
/// promoting a capability across tiers never renames it.
/// </summary>
/// <remarks>
/// On identity rows (<see cref="CapabilityRole.Identity"/>) byte 4 is reused as
/// an identity-kind discriminator (0 = name, 1 = configId), not a tier;
/// <see cref="ManifestAssembler"/> interprets it per-role.
/// </remarks>
public enum CapabilityTier : byte
{
    Core = 0,
    FwSpecific = 1,
    Profile = 2,
    Optional = 3,
}
