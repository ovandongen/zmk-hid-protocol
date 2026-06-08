namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// A 0xF7 confirmation: the ref token the host assigned to a confirm-flagged
/// action, echoed back by the handler, plus whether the action succeeded.
/// </summary>
public sealed record ConfirmAck(ushort Reference, bool Ok);
