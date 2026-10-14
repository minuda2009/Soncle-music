// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Audio.Decode;
using Soncle.TestKit;

namespace Soncle.Audio.Tests;

/// <summary>
/// <see cref="OpusWebmDecoder"/> with a fake codec whose output is the running sample number, so
/// pre-skip, gain, end padding and mono handling can be checked to the sample. The real codec is
/// proven against ffmpeg in Soncle.Audio.Opus.Tests.
/// </summary>
public class OpusWebmDecoderTests
{
    private static byte[] Bytes(string name) => File.ReadAllBytes(Fixtures.Path("flow/" + name));

    /// <summary>Sample number n comes out as n / 1e6 on every channel (negated on the second of two).</summary>
    private sealed class CountingCodec : IOpusDecoderFactory
    {
        public int Channels, Created;
        public bool Disposed;
        public IOpusDecoder Create(int sampleRate, int channels) { Created++; Channels = channels; return new Counter(this, channels); }

        private sealed class Counter : IOpusDecoder
        {
            private readonly CountingCodec _o; private readonly int _ch; private long _n;
            public Counter(CountingCodec o, int ch) { _o = o; _ch = ch; }
            public int Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize)
            {
                var samples = OpusToc.Samples(packet);
                for (var i = 0; i < samples; i++, _n++)
                {
                    pcm[i * _ch] = _n / 1e6f;
                    if (_ch == 2) pcm[i * 2 + 1] = -_n / 1e6f;
                }
                return samples;
            }
            public void Dispose() => _o.Disposed = true;
        }
    }

    private sealed class RefusingCodec : IOpusDecoderFactory
    {
        public IOpusDecoder Create(int sampleRate, int channels) => new R();
        private sealed class R : IOpusDecoder { public int Decode(ReadOnlySpan<byte> p, Span<float> pcm, int n) => -4; public void Dispose() { } }
    }

    private static long OracleFrames(string name) =>
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("flow/opus-oracle.json"))).RootElement
            .GetProperty("files").GetProperty(name).GetProperty("decoded").GetProperty("frames").GetInt64();

    [Fact]
    public void TheLengthIsWhatFfmpegDecodes()
    {
        foreach (var name in new[] { "sample.webm", "sample-tailcues.webm" })
        {
            var codec = new CountingCodec();
            var a = OpusWebmDecoder.Decode(Bytes(name), codec);
            Assert.Equal(OracleFrames(name), a.Frames);
            Assert.Equal(48_000, a.SampleRate);
            Assert.Equal(2, codec.Channels);
            Assert.True(codec.Disposed);
        }
    }

    [Fact]
    public void ThePreSkipIsDroppedAndTheEndPaddingTrimmedToTheSample()
    {
        var a = OpusWebmDecoder.Decode(Bytes("sample.webm"), new CountingCodec());
        var pcm = a.InterleavedStereo;
        // sample number 312 (the pre-skip) is the first one heard
        Assert.Equal(312 / 1e6f, pcm[0]);
        Assert.Equal(-312 / 1e6f, pcm[1]);
        // 401 packets x 960 = 384,960 samples; 648 are padding at the end
        var lastN = 401L * 960 - 648 - 1;
        Assert.Equal(lastN / 1e6f, pcm[(a.Frames - 1) * 2]);
        Assert.Equal(384_000, a.Frames);
    }

    [Fact]
    public void TheHeaderOutputGainIsApplied()
    {
        var plain = OpusWebmDecoder.Decode(Bytes("sample.webm"), new CountingCodec());
        // OpusHead's gain (Q7.8 dB, little-endian, 16 bytes in): +6.0 dB = 1536 = 0x0600
        var b = Bytes("sample.webm");
        var at = IndexOf(b, "OpusHead"u8);
        b[at + 16] = 0x00; b[at + 17] = 0x06;
        var gained = OpusWebmDecoder.Decode(b, new CountingCodec());
        var k = (float)Math.Pow(10, 6.0 / 20);
        Assert.Equal(plain.Frames, gained.Frames);
        for (var i = 0; i < 10; i++)
        {
            var idx = (1000 + i) * 2;
            Assert.InRange(gained.InterleavedStereo[idx] / plain.InterleavedStereo[idx], k - 1e-4f, k + 1e-4f);
        }
    }

    [Fact]
    public void MonoBecomesDualMono()
    {
        var codec = new CountingCodec();
        var a = OpusWebmDecoder.Decode(Bytes("mono.webm"), codec);
        Assert.Equal(1, codec.Channels);
        Assert.InRange(a.Frames, 47_000, 48_100);   // about one second
        for (var i = 0; i < a.Frames; i++) Assert.Equal(a.InterleavedStereo[i * 2], a.InterleavedStereo[i * 2 + 1]);
        Assert.Equal(312 / 1e6f, a.InterleavedStereo[0]);
    }

    [Fact]
    public void AFileCutShortGivesWhatWasDecoded()
    {
        var b = Bytes("sample.webm");
        var a = OpusWebmDecoder.Decode(b.AsMemory(0, b.Length * 6 / 10), new CountingCodec());
        Assert.InRange(a.Frames, 100_000, 383_999);
    }

    [Fact]
    public void ACodecErrorIsReported()
    {
        var ex = Assert.Throws<InvalidDataException>(() => OpusWebmDecoder.Decode(Bytes("sample.webm"), new RefusingCodec()));
        Assert.Contains("-4", ex.Message);
    }

    [Fact]
    public void NoOpusTrackIsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => OpusWebmDecoder.Decode(new byte[100], new CountingCodec()));
    }

    private static int IndexOf(byte[] b, ReadOnlySpan<byte> needle)
    {
        var i = b.AsSpan().IndexOf(needle);
        Assert.True(i >= 0);
        return i;
    }
}
