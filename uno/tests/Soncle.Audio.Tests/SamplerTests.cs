// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Decode;
using Soncle.Flow;
using Soncle.TestKit;

namespace Soncle.Audio.Tests;

public class SamplerTests
{
    private sealed class FileReader : IRangeReader
    {
        private readonly byte[] _b; private readonly bool _knownTotal;
        public readonly List<(long From, long To)> Calls = new();
        public FileReader(byte[] b, bool knownTotal = true) { _b = b; _knownTotal = knownTotal; }
        public Task<(byte[], long)> ReadAsync(long from, long to, CancellationToken ct)
        {
            Calls.Add((from, to));
            var end = Math.Min(to, _b.Length);
            return Task.FromResult((_b.AsSpan((int)from, (int)(end - from)).ToArray(), _knownTotal ? (long)_b.Length : 0L));
        }
    }

    /// <summary>Sine of 440 Hz at 48 kHz, one packet's worth per call, so the resampler and the middle cut can be checked.</summary>
    private sealed class SineCodec : IOpusDecoderFactory
    {
        public IOpusDecoder Create(int sampleRate, int channels) => new D(channels);
        private sealed class D : IOpusDecoder
        {
            private readonly int _ch; private long _n;
            public D(int ch) => _ch = ch;
            public int Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize)
            {
                var s = OpusToc.Samples(packet);
                for (var i = 0; i < s; i++, _n++)
                    for (var c = 0; c < _ch; c++) pcm[i * _ch + c] = 0.5f * (float)Math.Sin(2 * Math.PI * 440 * _n / 48000.0);
                return s;
            }
            public void Dispose() { }
        }
    }

    private static byte[] Song(string n = "sample.webm") => File.ReadAllBytes(Fixtures.Path("flow/" + n));

    [Fact]
    public async Task SamplesAWebmToMonoAt22kHz()
    {
        var r = new FileReader(Song());
        var pcm = await Sampler.SampleAsync(r, new SineCodec(), secs: 3, minBytes: 1000);
        Assert.NotNull(pcm);
        // three seconds of clusters, less the stream's 312-sample pre-skip (the rebuilt file still starts with it)
        Assert.InRange(pcm!.Value.Length, 3 * Sampler.SampleRate - 200, 3 * Sampler.SampleRate);
        // a 440 Hz sine at amplitude 0.5 survives the resampling: RMS 0.5/sqrt(2)
        var rms = Math.Sqrt(pcm.Value.Span.ToArray().Skip(2000).Take(40000).Average(x => (double)x * x));
        Assert.InRange(rms, 0.35, 0.36);
        Assert.Equal(0L, HeadFirst(r));
    }

    private static long HeadFirst(FileReader r) => r.Calls[0].From;

    [Fact]
    public async Task ReadsTheHeadThenOneRunOfClusters()
    {
        var r = new FileReader(Song());
        await Sampler.SampleAsync(r, new SineCodec(), secs: 3, minBytes: 1000);
        Assert.Equal(2, r.Calls.Count);
        Assert.Equal((0L, (long)Sampler.HeadBytes), r.Calls[0]);
        Assert.True(r.Calls[1].From > 0);
    }

    [Fact]
    public async Task AnUnknownTotalStillWorks()
    {
        var pcm = await Sampler.SampleAsync(new FileReader(Song(), knownTotal: false), new SineCodec(), secs: 3, minBytes: 1000);
        Assert.NotNull(pcm);
    }

    [Fact]
    public async Task TooLittleAudioGivesNull()
    {
        // the fixture's clusters are far below the JS default of 20 000 bytes
        Assert.Null(await Sampler.SampleAsync(new FileReader(Song()), new SineCodec(), secs: 3));
    }

    [Fact]
    public async Task NotWebmGivesNull()
    {
        var junk = Enumerable.Range(0, 500).Select(i => (byte)(i * 31)).ToArray();
        Assert.Null(await Sampler.SampleAsync(new FileReader(junk), new SineCodec(), minBytes: 1));
    }

    [Fact]
    public void MiddleKeepsAShortRunAndCutsALongOneFromThirtyFivePercent()
    {
        var shortRun = new float[Sampler.SampleRate * 10];
        Assert.Equal(shortRun.Length, Sampler.Middle(shortRun).Length);
        var longRun = Enumerable.Range(0, Sampler.SampleRate * 60).Select(i => (float)i).ToArray();
        var m = Sampler.Middle(longRun);
        Assert.Equal(Sampler.SampleRate * 15, m.Length);
        Assert.Equal((float)Math.Floor(longRun.Length * 0.35), m.Span[0]);
        // 35% + 15 s would pass the end of a 20 s run: pulled back to fit
        var twenty = Enumerable.Range(0, Sampler.SampleRate * 20).Select(i => (float)i).ToArray();
        Assert.Equal((float)(twenty.Length - Sampler.SampleRate * 15), Sampler.Middle(twenty).Span[0]);
    }

    [Fact]
    public void ResamplingKeepsDcAndDropsWhatIsAboveTheNewNyquist()
    {
        var dc = Resampler.Resample(Enumerable.Repeat(0.25f, 48000).ToArray(), 48000, 22050);
        Assert.All(dc, v => Assert.InRange(v, 0.2499f, 0.2501f));
        // 15 kHz is above 22.05 kHz / 2: it must be gone
        var hi = Enumerable.Range(0, 48000).Select(i => (float)Math.Sin(2 * Math.PI * 15000 * i / 48000.0)).ToArray();
        var o = Resampler.Resample(hi, 48000, 22050);
        Assert.InRange(Math.Sqrt(o.Skip(500).Take(20000).Average(v => (double)v * v)), 0, 0.01);
    }

    [Fact]
    public void SameRateIsACopy() => Assert.Equal(new[] { 1f, 2f }, Resampler.Resample(new[] { 1f, 2f }, 44100, 44100));
}
