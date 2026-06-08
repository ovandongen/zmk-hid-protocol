using System.Text;

namespace ZmkHidProtocol.Capabilities;

/// <summary>
/// Reassembles a 0xF8 manifest stream into a <see cref="DeviceManifest"/>.
/// Reports are grouped by sequence index; a logical entry whose string spans
/// multiple reports (a 36-char configId GUID, say) is stitched by concatenating
/// the chunks of same-seq reports in arrival order. Role/tier/confirm are taken
/// from the first report seen for a sequence; continuation reports' copies are
/// ignored — firmwares disagree on them (a Ploopy Bean repeats them, a Glove80
/// zeroes them), and the first report is authoritative for both. Mirrors the
/// reference host's reassembly (hid_viz_hub.py <c>query_manifest</c>).
/// </summary>
public sealed class ManifestAssembler
{
    private sealed class Pending
    {
        public byte Role;
        public byte Tier;
        public byte Confirm;
        public readonly List<byte> Buffer = new();
    }

    private readonly Dictionary<byte, Pending> _bySeq = new();

    /// <summary>True once a report carrying the LAST flag has been added.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Feeds one parsed 0xF8 report into the assembler.</summary>
    public void Add(ManifestEntry entry)
    {
        if (!_bySeq.TryGetValue(entry.Seq, out var pending))
        {
            pending = new Pending
            {
                Role = entry.Role,
                Tier = entry.Tier,
                Confirm = entry.Confirm,
            };
            _bySeq[entry.Seq] = pending;
        }

        pending.Buffer.AddRange(entry.Chunk);
        if (entry.IsLast) IsComplete = true;
    }

    /// <summary>
    /// Builds the manifest from everything added so far. Identity rows
    /// (role 0) populate Name (tier 0) and ConfigId (tier 1); every other row
    /// becomes a <see cref="Capability"/>, in ascending sequence order.
    /// </summary>
    public DeviceManifest Build()
    {
        string? name = null;
        string? configId = null;
        var caps = new List<Capability>();

        foreach (var seq in _bySeq.Keys.OrderBy(k => k))
        {
            var p = _bySeq[seq];
            var text = DecodeString(p.Buffer);
            if (p.Role == (byte)CapabilityRole.Identity)
            {
                if (p.Tier == 0) name = text;
                else if (p.Tier == 1) configId = text;
            }
            else
            {
                caps.Add(new Capability(
                    (CapabilityRole)p.Role,
                    (CapabilityTier)p.Tier,
                    p.Confirm != 0,
                    text));
            }
        }

        return new DeviceManifest(name, configId, caps);
    }

    private static string DecodeString(List<byte> buffer)
    {
        var arr = buffer.ToArray();
        int end = Array.IndexOf(arr, (byte)0);
        int len = end < 0 ? arr.Length : end;
        return Encoding.UTF8.GetString(arr, 0, len);
    }
}
