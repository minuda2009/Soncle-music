// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Audio.Output;
using Soncle.TestKit;

namespace Soncle.Audio.Tests;

/// <summary>
/// The device bridge: <see cref="OutputPump"/> must hand a sound device exactly what the engine
/// renders, interleaved, for any buffer size, without allocating on the audio thread.
/// </summary>
public class OutputPumpTests
{
    private const int Sr = 48000;

    private static Engine NewEngine(IAudioSource source)
    {
        var engine = new Engine(Sr);
        engine.Load(source, lufs: null);
        return engine;
    }

    // the reference: the same engine setup rendered planar, in the same chunks the pump uses
    private static float[] PlanarReference(Engine engine, int frames, int callFrames)
    {
        var outBuf = new float[frames * 2];
        var l = new float[engine.MaxBlockFrames];
        var r = new float[engine.MaxBlockFrames];
        for (var call = 0; call < frames; call += callFrames)
        {
            var callN = Math.Min(callFrames, frames - call);
            for (var done = 0; done < callN; done += engine.MaxBlockFrames)
            {
                var n = Math.Min(engine.MaxBlockFrames, callN - done);
                engine.Render(l.AsSpan(0, n), r.AsSpan(0, n), n);
                for (var i = 0; i < n; i++)
                {
                    outBuf[(call + done + i) * 2] = l[i];
                    outBuf[(call + done + i) * 2 + 1] = r[i];
                }
            }
        }
        return outBuf;
    }

    [Fact]
    public void InterleavesExactlyWhatTheEngineRenders()
    {
        const int frames = 48_000, call = 480;
        var pump = new OutputPump(NewEngine(new GeneratedSource(Sr, 300, 1100, 0.4)));
        var got = new float[frames * 2];
        for (var done = 0; done < frames; done += call)
            Assert.Equal(call, pump.Fill(got.AsSpan(done * 2, call * 2)));

        var want = PlanarReference(NewEngine(new GeneratedSource(Sr, 300, 1100, 0.4)), frames, call);
        Assert.Equal(want, got);
        // left and right really differ, so a swapped or duplicated channel would fail above
        Assert.Contains(Enumerable.Range(0, frames), i => Math.Abs(got[2 * i] - got[2 * i + 1]) > 0.05f);
    }

    [Fact]
    public void FillsRequestsLargerThanOneEngineBlock()
    {
        var engine = NewEngine(new GeneratedSource(Sr, 440, 660, 0.3));
        var frames = 3 * engine.MaxBlockFrames + 17;
        var pump = new OutputPump(engine);
        var got = new float[frames * 2];
        Assert.Equal(frames, pump.Fill(got));
        Assert.Equal((long)frames, engine.Position);

        var want = PlanarReference(NewEngine(new GeneratedSource(Sr, 440, 660, 0.3)), frames, frames);
        Assert.Equal(want, got);
        // the tail past the last full block is real audio, not left-over zeros
        Assert.Contains(got.AsSpan(got.Length - 34).ToArray(), s => Math.Abs(s) > 0.01f);
    }

    [Fact]
    public void AnOddTrailingSampleIsSilenced()
    {
        var pump = new OutputPump(NewEngine(new GeneratedSource(Sr, 440, 660, 0.3)));
        var buf = new float[2 * 1000 + 1];
        buf[^1] = 1f;
        Assert.Equal(1000, pump.Fill(buf));
        Assert.Equal(0f, buf[^1]);
    }

    [Fact]
    public void FillDoesNotAllocateSteadily()
    {
        var engine = NewEngine(new GeneratedSource(Sr, 300, 300, 0.3));
        var pump = new OutputPump(engine);
        var small = new float[960];
        var large = new float[(2 * engine.MaxBlockFrames + 100) * 2];
        for (var i = 0; i < 100; i++) { pump.Fill(small); pump.Fill(large); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) { pump.Fill(small); pump.Fill(large); }
        var after = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(after - before < 100 * 1024, (after - before).ToString());
    }

    [Fact]
    public void AHotInputStaysUnderTheLimiterCeiling()
    {
        var engine = new Engine(Sr);
        engine.Master.SetEq(true, new double[] { 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 });
        engine.Load(new GeneratedSource(Sr, 300, 300, 0.9, 240_000), lufs: null);
        var pump = new OutputPump(engine);
        var got = new float[240_000 * 2];
        for (var done = 0; done < 240_000; done += 1920) pump.Fill(got.AsSpan(done * 2, 1920 * 2));
        Assert.True(AudioAssert.TruePeakDb(got) <= -0.8, AudioAssert.TruePeakDb(got).ToString());
    }

    [Fact]
    public void EndedFollowsTheSourceAndKeepsFillingWithSilence()
    {
        var src = new MemoryPcmSource(Ramp(1000), Sr);
        var pump = new OutputPump(NewEngine(src)) { Source = src };
        var buf = new float[800 * 2];
        pump.Fill(buf);
        Assert.False(pump.Ended);
        Assert.Equal(800, pump.Fill(buf));
        Assert.True(pump.Ended);
        Assert.Equal(800, pump.Fill(buf));
        Assert.True(pump.Ended);

        var looped = new MemoryPcmSource(Ramp(1000), Sr, loop: true);
        var loopPump = new OutputPump(NewEngine(looped)) { Source = looped };
        for (var i = 0; i < 5; i++) loopPump.Fill(buf);
        Assert.False(loopPump.Ended);
        Assert.False(new OutputPump(new Engine(Sr)).Ended);   // no source: nothing to end
    }

    [Fact]
    public void MemorySourceStopsAtItsEndWithoutLoop()
    {
        var src = new MemoryPcmSource(Ramp(10), Sr);
        var l = new float[25];
        var r = new float[25];
        Assert.Equal(10, src.Read(l, r, 25));
        Assert.Equal(9f, l[9]);
        Assert.Equal(-9f, r[9]);
        Assert.Equal(10L, src.Position);
        Assert.Equal(0, src.Read(l, r, 25));
    }

    [Fact]
    public void MemorySourceWrapsExactlyAtItsLengthWhenLooping()
    {
        var src = new MemoryPcmSource(Ramp(10), Sr, loop: true);
        Assert.Equal(10L, src.LengthFrames);
        var l = new float[25];
        var r = new float[25];
        Assert.Equal(25, src.Read(l, r, 25));
        for (var i = 0; i < 25; i++)
        {
            Assert.Equal(i % 10, l[i]);
            Assert.Equal(-(i % 10), r[i]);
        }
        Assert.Equal(5L, src.Position);

        // a read that ends exactly on the last frame wraps the position to 0
        src.Seek(7);
        Assert.Equal(3, src.Read(l, r, 3));
        Assert.Equal(0L, src.Position);

        // an empty clip never spins
        Assert.Equal(0, new MemoryPcmSource(Array.Empty<float>(), Sr, loop: true).Read(l, r, 25));
    }

    [Fact]
    public void MemorySourceMonoMixAveragesTheChannels()
    {
        var mono = new MemoryPcmSource(new float[] { 1f, 0f, 0.5f, 0.5f }, Sr).ToMono();
        Assert.Equal(new[] { 0.5f, 0.5f }, mono);
    }

    // frame i: left = i, right = -i
    private static float[] Ramp(int frames)
    {
        var data = new float[frames * 2];
        for (var i = 0; i < frames; i++) { data[2 * i] = i; data[2 * i + 1] = -i; }
        return data;
    }
}
