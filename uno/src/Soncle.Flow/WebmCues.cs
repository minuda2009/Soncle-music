// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Buffers.Binary;

namespace Soncle.Flow;

/// <summary>One entry of a WebM Cues index: the time (seconds) and the absolute byte position of a cluster.</summary>
public readonly record struct WebmCue(double T, long Pos);

/// <summary>
/// What <see cref="WebmCues.ParseHead"/> learns from the start of a WebM file: the bytes to keep
/// (EBML header, Info, Tracks) and where the clusters are. <c>parseHead</c>'s result object in
/// <c>renderer/flow/webm.js</c>.
/// </summary>
public sealed class WebmHead
{
    public ReadOnlyMemory<byte> Ebml { get; init; }
    public long SegDataStart { get; init; }
    /// <summary>Info and Tracks, whole elements, in file order.</summary>
    public List<ReadOnlyMemory<byte>> Parts { get; } = new();
    /// <summary>The Cues index, or null when it is not in the head (e.g. it is at the end of the file).</summary>
    public List<WebmCue>? Cues { get; set; }
    /// <summary>Byte position of the first Cluster, or -1.</summary>
    public long FirstCluster { get; set; } = -1;
    /// <summary>Nanoseconds per timecode tick (TimecodeScale), default 1,000,000.</summary>
    public double Scale { get; set; } = 1e6;
    /// <summary>Segment duration in timecode ticks (0 when absent).</summary>
    public double Duration { get; set; }
    /// <summary>The id of the first element that runs past the bytes we were given, if any.</summary>
    public long? Truncated { get; set; }
}

/// <summary>A byte range of whole clusters. <see cref="To"/> is a double because it may be unbounded (an unknown total).</summary>
public readonly record struct WebmRange(long From, double To, double T0, bool CutAtCluster);

/// <summary>
/// Cut a decodable excerpt out of a (YouTube DASH) WebM without downloading the whole song: keep
/// the EBML header, Info and Tracks, then append whole Clusters from the middle of the song
/// (located through the Cues index), with the Segment size set to "unknown". A port of
/// <c>renderer/flow/webm.js</c> (<c>parseHead</c>, <c>pickRange</c>, <c>wholeClusters</c>,
/// <c>buildFile</c>), checked against it on real Opus WebM files (<c>fixtures/flow/webm.json</c>).
/// </summary>
public static class WebmCues
{
    private const long Ebml = 0x1a45dfa3, Segment = 0x18538067, Info = 0x1549a966, Tracks = 0x1654ae6b,
        CuesId = 0x1c53bb6b, Cluster = 0x1f43b675, CuePoint = 0xbb, CueTime = 0xb3,
        CueTrackPositions = 0xb7, CueClusterPosition = 0xf1, TimecodeScale = 0x2ad7b1, Duration = 0x4489;

    private readonly record struct El(long Id, long Start, long DataStart, long DataEnd, bool Unknown);

    // ---------- EBML reading ----------
    private static (long V, int Len)? ReadId(ReadOnlySpan<byte> b, long p)
    {
        if (p < 0 || p >= b.Length) return null;
        var x = b[(int)p];
        var len = x >= 0x80 ? 1 : x >= 0x40 ? 2 : x >= 0x20 ? 3 : x >= 0x10 ? 4 : 0;
        if (len == 0 || p + len > b.Length) return null;
        long v = 0;
        for (var i = 0; i < len; i++) v = v * 256 + b[(int)p + i];
        return (v, len);
    }

    /// <summary>A size is -1 for "unknown" (all value bits set).</summary>
    private static (long V, int Len)? ReadSize(ReadOnlySpan<byte> b, long p)
    {
        if (p < 0 || p >= b.Length) return null;
        int x = b[(int)p];
        var len = 1; var mask = 0x80;
        while (len <= 8 && (x & mask) == 0) { len++; mask >>= 1; }
        if (len > 8 || p + len > b.Length) return null;
        long v = x & (mask - 1);
        var allOnes = v == mask - 1;
        for (var i = 1; i < len; i++)
        {
            v = v * 256 + b[(int)p + i];
            if (b[(int)p + i] != 0xff) allOnes = false;
        }
        return (allOnes ? -1 : v, len);
    }

    // doubles, as in JS (a Number); only values past 2^53 differ, and no real file has those
    private static double ReadUint(ReadOnlySpan<byte> b, long p, long n)
    {
        double v = 0;
        for (var i = 0; i < n && p + i < b.Length; i++) v = v * 256 + b[(int)(p + i)];
        return v;
    }

    private static double? ReadFloat(ReadOnlySpan<byte> b, long p, long n)
    {
        if (p + n > b.Length) return null;
        // the JS reads 4 bytes as float32 and anything else as float64; only 4 and 8 are valid EBML floats
        return n switch
        {
            4 => BinaryPrimitives.ReadSingleBigEndian(b.Slice((int)p, 4)),
            8 => BinaryPrimitives.ReadDoubleBigEndian(b.Slice((int)p, 8)),
            _ => null,
        };
    }

    private static IEnumerable<El> Children(ReadOnlyMemory<byte> b, long start, long end)
    {
        var p = start;
        while (p < end)
        {
            var id = ReadId(b.Span, p); if (id is null) yield break;
            var sz = ReadSize(b.Span, p + id.Value.Len); if (sz is null) yield break;
            var dataStart = p + id.Value.Len + sz.Value.Len;
            var dataEnd = sz.Value.V < 0 ? end : dataStart + sz.Value.V;
            yield return new El(id.Value.V, p, dataStart, dataEnd, sz.Value.V < 0);
            if (sz.Value.V < 0) yield break;
            p = dataEnd;
        }
    }

    // like Uint8Array.subarray: the end is clamped to the data we have
    private static ReadOnlyMemory<byte> Sub(ReadOnlyMemory<byte> b, long start, long end)
    {
        end = Math.Min(end, b.Length);
        return start >= end ? ReadOnlyMemory<byte>.Empty : b.Slice((int)start, (int)(end - start));
    }

    // ---------- the four functions ----------

    /// <summary>Parse the head of a WebM file. Returns what's needed to slice it, or null.</summary>
    public static WebmHead? ParseHead(ReadOnlyMemory<byte> b)
    {
        long segDataStart = -1, ebmlEnd = -1;
        foreach (var el in Children(b, 0, b.Length))
        {
            if (el.Id == Ebml) ebmlEnd = el.DataEnd;
            if (el.Id == Segment) { segDataStart = el.DataStart; break; }
        }
        if (segDataStart < 0 || ebmlEnd < 0) return null;

        var head = new WebmHead { Ebml = Sub(b, 0, ebmlEnd), SegDataStart = segDataStart };
        foreach (var el in Children(b, segDataStart, b.Length))
        {
            if (el.DataEnd > b.Length && el.Id != Cluster) { head.Truncated = el.Id; break; }
            if (el.Id == Info)
            {
                head.Parts.Add(Sub(b, el.Start, el.DataEnd));
                foreach (var c in Children(b, el.DataStart, el.DataEnd))
                {
                    if (c.Id == TimecodeScale) head.Scale = ReadUint(b.Span, c.DataStart, c.DataEnd - c.DataStart);
                    if (c.Id == Duration && ReadFloat(b.Span, c.DataStart, c.DataEnd - c.DataStart) is { } d) head.Duration = d;
                }
            }
            else if (el.Id == Tracks) head.Parts.Add(Sub(b, el.Start, el.DataEnd));
            else if (el.Id == CuesId)
            {
                var cues = new List<WebmCue>();
                foreach (var cp in Children(b, el.DataStart, el.DataEnd))
                {
                    if (cp.Id != CuePoint) continue;
                    double time = 0; double pos = -1;
                    foreach (var c in Children(b, cp.DataStart, cp.DataEnd))
                    {
                        if (c.Id == CueTime) time = ReadUint(b.Span, c.DataStart, c.DataEnd - c.DataStart);
                        if (c.Id == CueTrackPositions)
                            foreach (var d in Children(b, c.DataStart, c.DataEnd))
                                if (d.Id == CueClusterPosition) pos = ReadUint(b.Span, d.DataStart, d.DataEnd - d.DataStart);
                    }
                    if (pos >= 0) cues.Add(new WebmCue(time * head.Scale / 1e9, segDataStart + (long)pos));
                }
                head.Cues = cues;
            }
            else if (el.Id == Cluster) { head.FirstCluster = el.Start; break; }
        }
        return head.Parts.Count < 2 ? null : head;
    }

    /// <summary>
    /// Byte range [From, To) of whole clusters covering about <paramref name="secs"/> seconds
    /// starting near <paramref name="atFrac"/> of the song. <paramref name="total"/> may be
    /// <see cref="double.PositiveInfinity"/> when the file size is unknown.
    /// </summary>
    public static WebmRange? PickRange(WebmHead head, double total, double atFrac = 0.35, double secs = 30)
    {
        var cues = head.Cues;
        if (cues is { Count: > 2 })
        {
            var dur = head.Duration != 0 && !double.IsNaN(head.Duration) ? head.Duration * head.Scale / 1e9 : cues[^1].T;
            var t0 = dur * atFrac;
            var i = 0; while (i + 1 < cues.Count && cues[i + 1].T <= t0) i++;
            var j = i; while (j < cues.Count && cues[j].T < cues[i].T + secs) j++;
            return new WebmRange(cues[i].Pos, j < cues.Count ? cues[j].Pos : total, cues[i].T, false);
        }
        if (head.FirstCluster > 0)
            return new WebmRange(head.FirstCluster, Math.Min(total, head.FirstCluster + 600_000), 0, true);
        return null;
    }

    /// <summary>Keep only complete clusters from a byte run that starts on a cluster boundary.</summary>
    public static ReadOnlyMemory<byte> WholeClusters(ReadOnlyMemory<byte> b)
    {
        long end = 0;
        foreach (var el in Children(b, 0, b.Length))
        {
            if (el.Id != Cluster || el.DataEnd > b.Length) break;
            end = el.DataEnd;
        }
        return Sub(b, 0, end);
    }

    /// <summary>Assemble a standalone WebM: EBML header + Segment(unknown size){Info, Tracks, clusters…}.</summary>
    public static byte[] BuildFile(WebmHead head, ReadOnlyMemory<byte> clusters)
    {
        ReadOnlySpan<byte> segHead = new byte[] { 0x18, 0x53, 0x80, 0x67, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };
        var len = head.Ebml.Length + segHead.Length + head.Parts.Sum(p => p.Length) + clusters.Length;
        var o = new byte[len];
        var at = 0;
        head.Ebml.Span.CopyTo(o.AsSpan(at)); at += head.Ebml.Length;
        segHead.CopyTo(o.AsSpan(at)); at += segHead.Length;
        foreach (var p in head.Parts) { p.Span.CopyTo(o.AsSpan(at)); at += p.Length; }
        clusters.Span.CopyTo(o.AsSpan(at));
        return o;
    }
}
