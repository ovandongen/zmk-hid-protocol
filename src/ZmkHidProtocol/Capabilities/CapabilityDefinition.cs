namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// One row of the <see cref="CapabilityRegistry"/>: the canonical definition of a
/// <em>known</em> capability id. The wire stays open and string-based (an unknown
/// id still degrades to discovered-but-inert via <see cref="ManifestAssembler"/>);
/// this record is the contract for the ids the host actually encodes/decodes, so
/// the router decode table, the originate UI, and the firmware-facing doc all
/// derive from one source instead of parallel hand-maintained tables.
/// </summary>
/// <param name="Id">The wire string, e.g. <c>core.rgb.set</c>.</param>
/// <param name="Roles">
/// Which manifest roles are legal for this id. <see cref="CapabilityRole.Triggers"/>
/// present ⇒ a device can originate it for forwarding (routable); its absence (e.g.
/// the layer actions) means app-originate-only.
/// </param>
/// <param name="Tier">
/// The canonical/expected availability tier, for the generated doc. The host never
/// overrides a device's advertised tier with this — <see cref="ManifestAssembler"/>
/// records whatever the device sends; this is the implementor-facing recommendation.
/// </param>
/// <param name="WireByte">The report opcode the host encodes/decodes; <c>null</c> = no host byte (pure telemetry / not yet wired).</param>
/// <param name="Payload">The report byte-layout (closed vocabulary).</param>
/// <param name="Value">Scalar interpretation for <see cref="PayloadShape.Uint32LE"/>; <see cref="ValueKind.None"/> for structured/empty payloads.</param>
/// <param name="Confirm">Whether a handled invocation expects a 0xF7 ack (only the ref-carrying layer actions today).</param>
/// <param name="Description">The human contract, surfaced in the generated firmware doc.</param>
public sealed record CapabilityDefinition(
    string Id,
    CapabilityRole[] Roles,
    CapabilityTier Tier,
    byte? WireByte,
    PayloadShape Payload,
    ValueKind Value,
    bool Confirm,
    string Description)
{
    /// <summary>
    /// True if the capability router forwards this id device→device: it has a wire
    /// byte and both a <see cref="CapabilityRole.Triggers"/> source and a
    /// <see cref="CapabilityRole.Handles"/> target among devices. <c>signal.fire</c>
    /// is <see cref="CapabilityRole.Triggers"/>-only (the <em>host</em> handles it,
    /// out of band via the signal dispatcher), so it is correctly excluded.
    /// </summary>
    public bool IsRoutable =>
        WireByte is not null && Roles.Contains(CapabilityRole.Triggers) && Roles.Contains(CapabilityRole.Handles);

    /// <summary>True if the host accepts this id as a <see cref="CapabilityRole.Handles"/> target it can originate.</summary>
    public bool IsHandled => Roles.Contains(CapabilityRole.Handles);
}
