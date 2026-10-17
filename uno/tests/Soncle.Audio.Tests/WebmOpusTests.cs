// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Audio.Decode;
using Soncle.TestKit;

namespace Soncle.Audio.Tests;

/// <summary>
/// The WebM Opus demuxer against ffprobe's view of the two real files in <c>fixtures/flow/</c>
/// (<c>opus-oracle.json</c>, made by <c>tools/fixtures/export-opus-oracle.mjs</c>).
/// </summary>
public class WebmOpusTests
{
    private static readonly JsonElement Oracle =
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("flow/opus-oracle.json"))).RootElement.GetProperty("files");

    private static byte[] Bytes(string name) => File.ReadAllBytes(Fixtures.Path("flow/" + name));
    private static readonly string[] Names = { "sample.webm", "sample-tailcues.webm" };

    [Fact]
    public void TheOpusHeadMatchesFfprobe()
    {
        foreach (var name in Names)
        {
            var o = Oracle.GetProperty(name);
            var f = WebmOpusReader.Read(Bytes(name));
            Assert.NotNull(f);
            Assert.Equal(1, f!.Head.Version);
            Assert.Equal(o.GetProperty("channels").GetInt32(), f.Head.Channels);
            Assert.Equal(o.GetProperty("initialPadding").GetInt32(), f.Head.PreSkip);
            Assert.Equal(o.GetProperty("sampleRate").GetInt32(), f.Head.InputRate);
            Assert.Equal(0, f.Head.MappingFamily);
            Assert.Equal(0.0, f.Head.OutputGainDb);
        }
    }

    [Fact]
    public void ThePacketsMatchFfprobe()
    {
        foreach (var name in Names)
        {
            var o = Oracle.GetProperty(name);
            var f = WebmOpusReader.Read(Bytes(name))!;
            var want = o.GetProperty("packets").EnumerateArray().ToList();
            Assert.Equal(o.GetProperty("packetCount").GetInt32(), f.Packets.Count);
            Assert.Equal(want.Count, f.Packets.Count);
            var preSkipMs = f.Head.PreSkip / 48.0;
            for (var i = 0; i < want.Count; i++)
            {
                Assert.Equal(want[i].GetProperty("size").GetInt32(), f.Packets[i].Data.Length);
                // 20 ms packets: 960 samples at 48 kHz
                Assert.Equal(want[i].GetProperty("durationMs").GetInt32() * 48, f.Packets[i].Samples);
                // ffprobe's time has the pre-skip taken off (and rounded to a millisecond)
                var ms = f.Packets[i].TimeNs / 1e6 - preSkipMs;
                Assert.InRange(ms, want[i].GetProperty("ptsMs").GetInt32() - 1.0, want[i].GetProperty("ptsMs").GetInt32() + 1.0);
            }
        }
    }

    [Fact]
    public void OnlyTheLastPacketCarriesEndPaddingAndItMatches()
    {
        foreach (var name in Names)
        {
            var o = Oracle.GetProperty(name);
            var f = WebmOpusReader.Read(Bytes(name))!;
            var last = o.GetProperty("packets").EnumerateArray().Last();
            var paddingSamples = last.GetProperty("discardPadding").GetInt32();      // ffprobe: samples at 48 kHz
            Assert.All(f.Packets.Take(f.Packets.Count - 1), p => Assert.Equal(0, p.DiscardPaddingNs));
            Assert.Equal(paddingSamples * 1_000_000_000L / 48_000, f.Packets[^1].DiscardPaddingNs);
        }
    }

    [Fact]
    public void PreSkipAndPaddingLeaveExactlyTheFramesFfmpegDecodes()
    {
        foreach (var name in Names)
        {
            var f = WebmOpusReader.Read(Bytes(name))!;
            var total = f.Packets.Sum(p => (long)p.Samples);
            var padding = f.Packets.Sum(p => p.DiscardPaddingNs) * 48_000 / 1_000_000_000L;
            Assert.Equal(Oracle.GetProperty(name).GetProperty("decoded").GetProperty("frames").GetInt64(), total - f.Head.PreSkip - padding);
        }
    }

    [Fact]
    public void ASegmentOfUnknownSizeStillReads()
    {
        // WebmCues.BuildFile writes the Segment size as "unknown" (0x01 followed by seven 0xff)
        var b = Bytes("sample.webm");
        var at = FindTopLevel(b, 0x18, 0x53, 0x80, 0x67);
        Assert.Equal(0x01, b[at + 4]);
        for (var i = 5; i < 12; i++) b[at + i] = 0xff;
        var f = WebmOpusReader.Read(b);
        Assert.NotNull(f);
        Assert.Equal(401, f!.Packets.Count);
    }

    [Fact]
    public void FilesThatAreNotOpusWebmGiveNull()
    {
        Assert.Null(WebmOpusReader.Read(Enumerable.Range(0, 200).Select(i => (byte)(i * 7)).ToArray()));
        Assert.Null(WebmOpusReader.Read(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void ALyingCodecPrivateIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => WebmOpusReader.ParseHead(new byte[40]));
        Assert.Throws<InvalidDataException>(() => WebmOpusReader.ParseHead("OpusHea"u8.ToArray()));
    }

    [Fact]
    public void TheHeadIsReadLittleEndianWithQ78Gain()
    {
        // OpusHead v1, 2 ch, pre-skip 0x0138 = 312, 44100 Hz, gain -0x0100 = -1 dB, mapping 0
        var raw = new byte[] { (byte)'O', (byte)'p', (byte)'u', (byte)'s', (byte)'H', (byte)'e', (byte)'a', (byte)'d', 1, 2, 0x38, 0x01, 0x44, 0xAC, 0, 0, 0x00, 0xFF, 0 };
        var h = WebmOpusReader.ParseHead(raw);
        Assert.Equal(new OpusHead(1, 2, 312, 44100, -1.0, 0), h);
    }

    [Fact]
    public void TheTocByteGivesThePacketLength()
    {
        Assert.Equal(960, OpusToc.Samples(new byte[] { 0xF8 }));            // CELT 20 ms, one frame
        Assert.Equal(1920, OpusToc.Samples(new byte[] { 0xF9, 0, 0 }));     // two frames
        Assert.Equal(1920, OpusToc.Samples(new byte[] { 0xFA, 0, 0 }));     // two frames, different sizes
        Assert.Equal(2880, OpusToc.Samples(new byte[] { 0xFB, 3 }));        // arbitrary count: 3 frames
        Assert.Equal(2880, OpusToc.Samples(new byte[] { 0x18 }));           // SILK 60 ms
        Assert.Equal(480, OpusToc.Samples(new byte[] { 0x60 }));            // hybrid 10 ms
        Assert.Equal(120, OpusToc.Samples(new byte[] { 0x80 }));            // CELT 2.5 ms
        Assert.Equal(0, OpusToc.Samples(new byte[] { 0xFB, 48 }));          // 48 x 20 ms is over the 120 ms limit
        Assert.Equal(0, OpusToc.Samples(new byte[] { 0xFB }));              // code 3 needs its count byte
        Assert.Equal(0, OpusToc.Samples(ReadOnlySpan<byte>.Empty));
    }

    private static int FindTopLevel(byte[] b, params byte[] id)
    {
        for (var i = 0; i + id.Length <= b.Length; i++)
            if (b.AsSpan(i, id.Length).SequenceEqual(id)) return i;
        throw new InvalidOperationException("id not found");
    }
}
