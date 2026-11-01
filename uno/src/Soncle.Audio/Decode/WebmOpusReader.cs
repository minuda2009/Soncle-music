// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using Soncle.Core.Media;

namespace Soncle.Audio.Decode;

/// <summary>The Opus identification header (RFC 7845 §5.1), from the track's CodecPrivate.</summary>
public sealed record OpusHead(int Version, int Channels, int PreSkip, int InputRate, double OutputGainDb, int MappingFamily);

/// <summary>One Opus packet from a WebM block.</summary>
/// <param name="Data">The packet bytes (a view into the file).</param>
/// <param name="TimeNs">Start time from the container, in nanoseconds (before the pre-skip is removed).</param>
/// <param name="Samples">Its length in samples per channel at 48 kHz (from the TOC byte).</param>
/// <param name="DiscardPaddingNs">Matroska DiscardPadding: how much to cut off the end of the song (0 = none). Normally only on the last packet.</param>
public sealed record OpusPacket(ReadOnlyMemory<byte> Data, long TimeNs, int Samples, long DiscardPaddingNs);

/// <summary>A WebM file's Opus audio track, demuxed.</summary>
public sealed record WebmOpusFile(OpusHead Head, IReadOnlyList<OpusPacket> Packets);

/// <summary>
/// Reads the Opus packets out of a WebM file (YouTube's audio format): the track's OpusHead, then
/// every block of that track, in file order. It does not decode anything. Segment and cluster sizes
/// may be "unknown" (the files <c>WebmCues.BuildFile</c> makes). Opus does not use lacing in WebM,
/// so a laced block is refused rather than guessed at. Checked against ffprobe's packet list
/// (<c>fixtures/flow/opus-oracle.json</c>).
/// </summary>
public static class WebmOpusReader
{
    // element ids, shared with the incremental WebmOpusStreamReader
    internal const long Segment = 0x18538067, Info = 0x1549a966, Tracks = 0x1654ae6b, TrackEntry = 0xae,
        TrackNumber = 0xd7, TrackType = 0x83, CodecId = 0x86, CodecPrivate = 0x63a2, TimecodeScale = 0x2ad7b1,
        Cluster = 0x1f43b675, Timecode = 0xe7, SimpleBlock = 0xa3, BlockGroup = 0xa0, Block = 0xa1, DiscardPadding = 0x75a2;

    /// <summary>The Opus track and its packets, or null if the file has no Opus audio track.</summary>
    /// <exception cref="NotSupportedException">Laced blocks, or an Opus stream with channel mapping (more than 2 channels).</exception>
    public static WebmOpusFile? Read(ReadOnlyMemory<byte> file)
    {
        var b = file;
        double scale = 1e6;
        long? track = null;
        OpusHead? head = null;
        var packets = new List<OpusPacket>();

        foreach (var seg in Ebml.Children(b, 0, b.Length).Where(e => e.Id == Segment))
        {
            foreach (var el in Ebml.Children(b, seg.DataStart, Math.Min(seg.DataEnd, b.Length)))
            {
                if (el.Id == Info)
                {
                    foreach (var c in Ebml.Children(b, el.DataStart, Math.Min(el.DataEnd, b.Length)))
                        if (c.Id == TimecodeScale) scale = Ebml.ReadUint(b.Span, c.DataStart, c.DataEnd - c.DataStart);
                }
                else if (el.Id == Tracks && head is null)
                {
                    foreach (var entry in Ebml.Children(b, el.DataStart, Math.Min(el.DataEnd, b.Length)).Where(e => e.Id == TrackEntry))
                    {
                        var (number, type, codec, priv) = ReadTrack(b, entry);
                        if (type != 2 || codec != "A_OPUS" || priv is null) continue;
                        head = ParseHead(priv.Value.Span);
                        track = number;
                        break;
                    }
                }
                else if (el.Id == Cluster && head is not null) ReadCluster(b, el, scale, track!.Value, packets);
            }
        }
        if (head is null) return null;
        CheckSupported(head);
        return new WebmOpusFile(head, packets);
    }

    /// <summary>Opus in WebM is mono or stereo here; anything with a channel mapping table is not handled.</summary>
    internal static void CheckSupported(OpusHead head)
    {
        if (head.MappingFamily != 0 || head.Channels is < 1 or > 2)
            throw new NotSupportedException($"Opus with {head.Channels} channels (mapping family {head.MappingFamily})");
    }

    internal static (long Number, long Type, string Codec, ReadOnlyMemory<byte>? Private) ReadTrack(ReadOnlyMemory<byte> b, EbmlElement entry)
    {
        long number = -1, type = -1; var codec = ""; ReadOnlyMemory<byte>? priv = null;
        foreach (var c in Ebml.Children(b, entry.DataStart, Math.Min(entry.DataEnd, b.Length)))
        {
            var len = c.DataEnd - c.DataStart;
            if (c.Id == TrackNumber) number = (long)Ebml.ReadUint(b.Span, c.DataStart, len);
            else if (c.Id == TrackType) type = (long)Ebml.ReadUint(b.Span, c.DataStart, len);
            else if (c.Id == CodecId) codec = Encoding.ASCII.GetString(Ebml.Sub(b, c.DataStart, c.DataEnd).Span).TrimEnd('\0');
            else if (c.Id == CodecPrivate) priv = Ebml.Sub(b, c.DataStart, c.DataEnd);
        }
        return (number, type, codec, priv);
    }

    /// <summary>RFC 7845 §5.1. Throws <see cref="InvalidDataException"/> if it is not an OpusHead.</summary>
    public static OpusHead ParseHead(ReadOnlySpan<byte> p)
    {
        if (p.Length < 19 || !p[..8].SequenceEqual("OpusHead"u8)) throw new InvalidDataException("not an OpusHead");
        return new OpusHead(
            Version: p[8],
            Channels: p[9],
            PreSkip: BinaryPrimitives.ReadUInt16LittleEndian(p[10..]),
            InputRate: (int)BinaryPrimitives.ReadUInt32LittleEndian(p[12..]),
            OutputGainDb: BinaryPrimitives.ReadInt16LittleEndian(p[16..]) / 256.0,   // Q7.8 dB
            MappingFamily: p[18]);
    }

    private static void ReadCluster(ReadOnlyMemory<byte> b, EbmlElement cluster, double scale, long track, List<OpusPacket> into)
    {
        long clusterTime = 0;
        foreach (var el in Ebml.Children(b, cluster.DataStart, Math.Min(cluster.DataEnd, b.Length)))
        {
            if (el.Id == Timecode) clusterTime = (long)Ebml.ReadUint(b.Span, el.DataStart, el.DataEnd - el.DataStart);
            else if (el.Id == SimpleBlock) AddBlock(b, el, clusterTime, scale, track, 0, into);
            else if (el.Id == BlockGroup)
            {
                EbmlElement? block = null; long discard = 0;
                foreach (var c in Ebml.Children(b, el.DataStart, Math.Min(el.DataEnd, b.Length)))
                {
                    if (c.Id == Block) block = c;
                    else if (c.Id == DiscardPadding) discard = Ebml.ReadInt(b.Span, c.DataStart, c.DataEnd - c.DataStart);
                }
                if (block is { } blk) AddBlock(b, blk, clusterTime, scale, track, discard, into);
            }
            // a cluster of unknown size is followed by the next cluster as if it were a child
            else if (el.Id == Cluster) ReadCluster(b, el, scale, track, into);
        }
    }

    private static void AddBlock(ReadOnlyMemory<byte> b, EbmlElement el, long clusterTime, double scale, long track, long discardNs, List<OpusPacket> into)
    {
        var end = Math.Min(el.DataEnd, b.Length);
        var tn = Ebml.ReadSize(b.Span, el.DataStart);   // the track number is a vint
        if (tn is null) return;
        var at = el.DataStart + tn.Value.Len;
        if (at + 3 > end || tn.Value.V != track) return;
        var rel = BinaryPrimitives.ReadInt16BigEndian(b.Span.Slice((int)at, 2));
        var flags = b.Span[(int)at + 2];
        if (((flags >> 1) & 3) != 0) throw new NotSupportedException("laced Opus blocks");
        var data = Ebml.Sub(b, at + 3, end);
        if (data.IsEmpty) return;
        var timeNs = (long)((clusterTime + rel) * scale);
        into.Add(new OpusPacket(data, timeNs, OpusToc.Samples(data.Span), discardNs));
    }
}
