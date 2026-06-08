namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// A single parsed 0xF8 manifest report. One logical manifest entry may span
/// several reports sharing a <see cref="Seq"/> when its string doesn't fit in
/// one report (the <see cref="Continues"/> flag marks the non-final chunks);
/// <see cref="ManifestAssembler"/> stitches them back together. Role/tier/
/// confirm are authoritative only on the first report of a sequence;
/// continuation reports' copies vary by firmware (some repeat the fields, some
/// zero them) and are ignored by the assembler, so they are kept as raw bytes
/// here rather than pre-interpreted.
/// </summary>
public sealed record ManifestEntry(
    byte Seq,
    byte Flags,
    byte Role,
    byte Tier,
    byte Confirm,
    byte[] Chunk)
{
    /// <summary>Header bytes before the string chunk: type, seq, flags, role, tier, confirm.</summary>
    public const int HeaderSize = 6;

    /// <summary>Final logical entry of the stream.</summary>
    public const byte FlagLast = 0x01;

    /// <summary>This entry's string continues in the next same-seq report.</summary>
    public const byte FlagContinues = 0x02;

    public bool IsLast => (Flags & FlagLast) != 0;
    public bool Continues => (Flags & FlagContinues) != 0;
}
