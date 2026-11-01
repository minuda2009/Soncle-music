// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Buffers.Binary;
using Soncle.Core.Media;
using static Soncle.Audio.Decode.WebmOpusReader;

namespace Soncle.Audio.Decode;

/// <summary>One entry of a WebM Cues index: a cluster's start time (ns) and absolute byte position (<c>WebmCue</c> in Soncle.Flow, in ns).</summary>
public readonly record struct WebmOpusCue(long TimeNs, long Position);

/// <summary>Reads up to <c>buffer.Length</c> bytes; returns 0 at the end of the stream. May block (bytes still downloading).</summary>
public delegate int ByteRead(Memory<byte> buffer);

/// <summary>
/// <see cref="WebmOpusReader"/> one packet at a time, from bytes that are still arriving (a song's
/// download): the head (EBML header, Info, Tracks with the OpusHead, Cues when they come first, as
/// in YouTube's WebM) is read once, then each <see cref="Next"/> yields the next block of the Opus
/// track. Elements are read as a flat run, so a Segment or Cluster of unknown size works exactly
/// as in the whole-file reader. After <see cref="Reopen"/> at a cue's cluster position the same
/// reader continues from there (the head is kept). For a whole file the packets are the ones
/// <see cref="WebmOpusReader.Read"/> returns (data, time, samples, discard padding).
/// </summary>
/// <remarks>Not thread-safe: one decode thread owns it. Cues met later (at the end of the file) are picked up too.</remarks>
public sealed class WebmOpusStreamReader
{
    private const long EbmlHeader = 0x1a45dfa3, Duration = 0x4489, CuesId = 0x1c53bb6b, CuePoint = 0xbb, CueTime = 0xb3,
        CueTrackPositions = 0xb7, CueClusterPosition = 0xf1;
    private const int MaxHeader = 12;   // 4-byte id + 8-byte size

    private ByteRead _read;
    private byte[] _buf = new byte[64 * 1024];
    private long _bufStart;   // absolute file position of _buf[0]
    private int _off, _len;   // unread bytes are _buf[_off.._len)
    private bool _eof;
    private byte[] _packet = new byte[2048];
    private long _clusterTime;
    private long _track = -1;
    private long _segDataStart;

    /// <param name="read">The byte source.</param>
    /// <param name="position">The absolute file position its first byte is at (0 for the whole file).</param>
    public WebmOpusStreamReader(ByteRead read, long position = 0)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _bufStart = position;
    }

    /// <summary>A reader over a whole (or growing) <see cref="Stream"/> starting at byte 0.</summary>
    public WebmOpusStreamReader(Stream stream) : this(m => stream.Read(m.Span)) { }

    /// <summary>The Opus header, once <see cref="ReadHead"/> found the track.</summary>
    public OpusHead? Head { get; private set; }
    /// <summary>Nanoseconds per timecode tick (TimecodeScale).</summary>
    public double Scale { get; private set; } = 1e6;
    /// <summary>Info's Duration in ns, or null when the file doesn't say.</summary>
    public long? DurationNs { get; private set; }
    /// <summary>The Cues index (absolute positions), or null until one is read.</summary>
    public IReadOnlyList<WebmOpusCue>? Cues { get; private set; }
    /// <summary>Byte position of the first Cluster (-1 until seen).</summary>
    public long FirstCluster { get; private set; } = -1;
    /// <summary>The absolute byte position of the next unread byte.</summary>
    public long Position => _bufStart + _off;

    /// <summary>
    /// Reads up to the first Cluster: Info, Tracks and Cues if they come first.
    /// </summary>
    /// <exception cref="NotSupportedException">No Opus audio track, or one with a channel mapping.</exception>
    public OpusHead ReadHead()
    {
        while (FirstCluster < 0 && Step(out _, out _, out _)) { }
        var head = Head ?? throw new NotSupportedException("no Opus audio track in this WebM");
        CheckSupported(head);
        return head;
    }

    /// <summary>Continues from <paramref name="position"/> (a cluster start) with a new byte source; the head is kept.</summary>
    public void Reopen(ByteRead read, long position)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _bufStart = position;
        _off = _len = 0;
        _eof = false;
        _clusterTime = 0;
    }

    /// <summary>
    /// The next block of the Opus track: <paramref name="data"/> is valid until the next call. False
    /// at the end of the file (or where it is damaged, like the whole-file reader).
    /// </summary>
    /// <exception cref="NotSupportedException">A laced block.</exception>
    public bool Next(out ReadOnlyMemory<byte> data, out long timeNs, out long discardPaddingNs)
    {
        while (true)
        {
            if (!Step(out var packet, out timeNs, out discardPaddingNs))
            {
                data = default;
                return false;
            }
            if (packet > 0)
            {
                data = new ReadOnlyMemory<byte>(_packet, 0, packet);
                return true;
            }
        }
    }

    /// <summary>One element. <paramref name="packet"/> = bytes of a block copied to <c>_packet</c> (0 = none).</summary>
    private bool Step(out int packet, out long timeNs, out long discardNs)
    {
        packet = 0; timeNs = 0; discardNs = 0;
        var have = Fill(MaxHeader);
        if (have == 0) return false;
        var span = _buf.AsSpan(_off, have);
        var id = Ebml.ReadId(span, 0);
        if (id is null) return false;
        var sz = Ebml.ReadSize(span, id.Value.Len);
        if (sz is null) return false;
        var start = Position;
        var header = id.Value.Len + sz.Value.Len;
        var size = sz.Value.V;

        if (id.Value.V == Segment)
        {
            _off += header;
            _segDataStart = Position;
            return true;
        }
        if (id.Value.V == Cluster)
        {
            // entered, not read whole: its children follow as a flat run
            if (FirstCluster < 0) FirstCluster = start;
            _clusterTime = 0;
            _off += header;
            return true;
        }
        if (size < 0) return false;   // only Segment and Cluster may have an unknown size here
        var whole = id.Value.V is EbmlHeader or Info or Tracks or CuesId or Timecode or SimpleBlock or BlockGroup;
        if (!whole)
        {
            Skip(header + size);
            return true;
        }
        if (header + size > int.MaxValue / 2 || Fill((int)(header + size)) < header + size) return false;
        var el = new ReadOnlyMemory<byte>(_buf, _off + header, (int)size);
        _off += (int)(header + size);
        switch (id.Value.V)
        {
            case Info: ReadInfo(el); break;
            case Tracks when Head is null: ReadTracks(el); break;
            case CuesId: ReadCues(el); break;
            case Timecode: _clusterTime = (long)Ebml.ReadUint(el.Span, 0, el.Length); break;
            case SimpleBlock: packet = Block(el.Span, out timeNs); break;
            case BlockGroup:
                EbmlElement? block = null;
                foreach (var c in Ebml.Children(el, 0, el.Length))
                {
                    if (c.Id == WebmOpusReader.Block) block = c;
                    else if (c.Id == DiscardPadding) discardNs = Ebml.ReadInt(el.Span, c.DataStart, c.DataEnd - c.DataStart);
                }
                if (block is { } b) packet = Block(el.Span[(int)b.DataStart..(int)Math.Min(b.DataEnd, el.Length)], out timeNs);
                if (packet == 0) discardNs = 0;
                break;
        }
        return true;
    }

    private void ReadInfo(ReadOnlyMemory<byte> el)
    {
        double? duration = null;
        foreach (var c in Ebml.Children(el, 0, el.Length))
        {
            if (c.Id == TimecodeScale) Scale = Ebml.ReadUint(el.Span, c.DataStart, c.DataEnd - c.DataStart);
            else if (c.Id == Duration) duration = Ebml.ReadFloat(el.Span, c.DataStart, c.DataEnd - c.DataStart);
        }
        if (duration is { } d && double.IsFinite(d) && d > 0) DurationNs = (long)(d * Scale);
    }

    private void ReadTracks(ReadOnlyMemory<byte> el)
    {
        foreach (var entry in Ebml.Children(el, 0, el.Length))
        {
            if (entry.Id != TrackEntry) continue;
            var (number, type, codec, priv) = ReadTrack(el, entry);
            if (type != 2 || codec != "A_OPUS" || priv is null) continue;
            Head = ParseHead(priv.Value.Span);
            _track = number;
            return;
        }
    }

    private void ReadCues(ReadOnlyMemory<byte> el)
    {
        var cues = new List<WebmOpusCue>();
        foreach (var cp in Ebml.Children(el, 0, el.Length))
        {
            if (cp.Id != CuePoint) continue;
            double time = 0; double pos = -1;
            foreach (var c in Ebml.Children(el, cp.DataStart, cp.DataEnd))
            {
                if (c.Id == CueTime) time = Ebml.ReadUint(el.Span, c.DataStart, c.DataEnd - c.DataStart);
                if (c.Id == CueTrackPositions)
                    foreach (var d in Ebml.Children(el, c.DataStart, c.DataEnd))
                        if (d.Id == CueClusterPosition) pos = Ebml.ReadUint(el.Span, d.DataStart, d.DataEnd - d.DataStart);
            }
            if (pos >= 0) cues.Add(new WebmOpusCue((long)(time * Scale), _segDataStart + (long)pos));
        }
        cues.Sort((a, b) => a.TimeNs.CompareTo(b.TimeNs));
        if (cues.Count > 0) Cues = cues;
    }

    /// <summary>AddBlock of the whole-file reader: copies the packet of our track into <c>_packet</c>.</summary>
    private int Block(ReadOnlySpan<byte> b, out long timeNs)
    {
        timeNs = 0;
        var tn = Ebml.ReadSize(b, 0);   // the track number is a vint
        if (tn is null) return 0;
        var at = tn.Value.Len;
        if (at + 3 > b.Length || tn.Value.V != _track || Head is null) return 0;
        var rel = BinaryPrimitives.ReadInt16BigEndian(b.Slice(at, 2));
        var flags = b[at + 2];
        if (((flags >> 1) & 3) != 0) throw new NotSupportedException("laced Opus blocks");
        var data = b[(at + 3)..];
        if (data.IsEmpty) return 0;
        timeNs = (long)((_clusterTime + rel) * Scale);
        if (_packet.Length < data.Length) _packet = new byte[Math.Max(data.Length, _packet.Length * 2)];
        data.CopyTo(_packet);
        return data.Length;
    }

    /// <summary>Makes up to <paramref name="want"/> unread bytes available; returns how many are (fewer only at the end).</summary>
    private int Fill(int want)
    {
        if (_len - _off >= want || _eof) return Math.Min(want, _len - _off);
        if (_off > 0)
        {
            Buffer.BlockCopy(_buf, _off, _buf, 0, _len - _off);
            _len -= _off;
            _bufStart += _off;
            _off = 0;
        }
        if (_buf.Length < want) Array.Resize(ref _buf, Math.Max(want, _buf.Length * 2));
        while (_len < want)
        {
            var n = _read(_buf.AsMemory(_len));
            if (n <= 0) { _eof = true; break; }
            _len += n;
        }
        return Math.Min(want, _len);
    }

    private void Skip(long count)
    {
        while (count > 0)
        {
            var have = _len - _off;
            if (have == 0 && Fill(1) == 0) return;
            var n = (int)Math.Min(count, _len - _off);
            _off += n;
            count -= n;
        }
    }
}
