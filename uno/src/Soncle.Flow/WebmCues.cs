// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Core.Media;

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
    private const long EbmlId = 0x1a45dfa3, Segment = 0x18538067, Info = 0x1549a966, Tracks = 0x1654ae6b,
        CuesId = 0x1c53bb6b, Cluster = 0x1f43b675, CuePoint = 0xbb, CueTime = 0xb3,
        CueTrackPositions = 0xb7, CueClusterPosition = 0xf1, TimecodeScale = 0x2ad7b1, Duration = 0x4489;

    // ---------- the four functions ----------

    /// <summary>Parse the head of a WebM file. Returns what's needed to slice it, or null.</summary>
    public static WebmHead? ParseHead(ReadOnlyMemory<byte> b)
    {
        long segDataStart = -1, ebmlEnd = -1;
        foreach (var el in Ebml.Children(b, 0, b.Length))
        {
            if (el.Id == EbmlId) ebmlEnd = el.DataEnd;
            if (el.Id == Segment) { segDataStart = el.DataStart; break; }
        }
        if (segDataStart < 0 || ebmlEnd < 0) return null;

        var head = new WebmHead { Ebml = Ebml.Sub(b, 0, ebmlEnd), SegDataStart = segDataStart };
        foreach (var el in Ebml.Children(b, segDataStart, b.Length))
        {
            if (el.DataEnd > b.Length && el.Id != Cluster) { head.Truncated = el.Id; break; }
            if (el.Id == Info)
            {
                head.Parts.Add(Ebml.Sub(b, el.Start, el.DataEnd));
                foreach (var c in Ebml.Children(b, el.DataStart, el.DataEnd))
                {
                    if (c.Id == TimecodeScale) head.Scale = Ebml.ReadUint(b.Span, c.DataStart, c.DataEnd - c.DataStart);
                    if (c.Id == Duration && Ebml.ReadFloat(b.Span, c.DataStart, c.DataEnd - c.DataStart) is { } d) head.Duration = d;
                }
            }
            else if (el.Id == Tracks) head.Parts.Add(Ebml.Sub(b, el.Start, el.DataEnd));
            else if (el.Id == CuesId)
            {
                var cues = new List<WebmCue>();
                foreach (var cp in Ebml.Children(b, el.DataStart, el.DataEnd))
                {
                    if (cp.Id != CuePoint) continue;
                    double time = 0; double pos = -1;
                    foreach (var c in Ebml.Children(b, cp.DataStart, cp.DataEnd))
                    {
                        if (c.Id == CueTime) time = Ebml.ReadUint(b.Span, c.DataStart, c.DataEnd - c.DataStart);
                        if (c.Id == CueTrackPositions)
                            foreach (var d in Ebml.Children(b, c.DataStart, c.DataEnd))
                                if (d.Id == CueClusterPosition) pos = Ebml.ReadUint(b.Span, d.DataStart, d.DataEnd - d.DataStart);
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
        foreach (var el in Ebml.Children(b, 0, b.Length))
        {
            if (el.Id != Cluster || el.DataEnd > b.Length) break;
            end = el.DataEnd;
        }
        return Ebml.Sub(b, 0, end);
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
