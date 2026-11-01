// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Audio.Output;
using Soncle.TestKit;

namespace Soncle.Audio.Tests;

/// <summary>
/// The start/stop ramp a sound device applies to its first and last block: it must reach silence
/// exactly, join the neighbouring blocks without a step, treat both channels alike and never boost.
/// </summary>
public class GainRampTests
{
    private const int Sr = 48000;

    private static float[] Constant(int frames, float l, float r)
    {
        var data = new float[frames * 2];
        for (var i = 0; i < frames; i++) { data[2 * i] = l; data[2 * i + 1] = r; }
        return data;
    }

    [Fact]
    public void FadeOutEndsInSilenceAndStartsNextToUnity()
    {
        const int n = 480;
        var buf = Constant(n, 0.5f, -0.25f);
        GainRamp.Apply(buf, 2, 1f, 0f);

        Assert.Equal(0f, buf[^2]);
        Assert.Equal(0f, buf[^1]);
        // the first frame is one step below the unity-gain block before it, not a jump
        Assert.Equal(0.5f * (1f - 1f / n), buf[0], 5);
        Assert.Equal(-0.25f * (1f - 1f / n), buf[1], 5);
        for (var i = 1; i < n; i++)
        {
            Assert.True(buf[2 * i] <= buf[2 * (i - 1)], $"left rose at frame {i}");
            Assert.True(Math.Abs(buf[2 * i] - buf[2 * (i - 1)]) <= 0.5f / n + 1e-6f, $"left stepped at frame {i}");
        }
    }

    [Fact]
    public void FadeInStartsOneStepAboveSilenceAndEndsAtUnity()
    {
        const int n = 1000;
        var buf = Constant(n, 0.8f, 0.8f);
        GainRamp.Apply(buf, 2, 0f, 1f);

        Assert.Equal(0.8f / n, buf[0], 6);
        Assert.Equal(0.8f, buf[^2]);
        Assert.Equal(0.8f, buf[^1]);
        for (var i = 1; i < n; i++) Assert.True(buf[2 * i] >= buf[2 * (i - 1)], $"rose then fell at frame {i}");
    }

    [Fact]
    public void BothChannelsOfAFrameGetTheSameGain()
    {
        var buf = Constant(300, 0.6f, -0.6f);
        GainRamp.Apply(buf, 2, 1f, 0f);
        for (var i = 0; i < 300; i++) Assert.Equal(buf[2 * i], -buf[2 * i + 1]);
    }

    [Fact]
    public void UnityToUnityLeavesTheBlockAloneAndPartialFramesAreUntouched()
    {
        var buf = Constant(64, 0.3f, -0.7f);
        var want = (float[])buf.Clone();
        GainRamp.Apply(buf, 2, 1f, 1f);
        Assert.Equal(want, buf);

        var odd = new float[] { 1f, 1f, 1f, 1f, 1f };
        GainRamp.Apply(odd, 2, 1f, 0f);
        Assert.Equal(0f, odd[2]);
        Assert.Equal(0f, odd[3]);
        Assert.Equal(1f, odd[4]);   // not a whole frame

        GainRamp.Apply(Span<float>.Empty, 2, 1f, 0f);
        GainRamp.Apply(new float[1], 2, 1f, 0f);
    }

    [Theory]
    [InlineData(1.01f, 0f)]
    [InlineData(0f, 2f)]
    [InlineData(-0.1f, 1f)]
    [InlineData(float.NaN, 1f)]
    public void RejectsGainsOutsideZeroToOne(float from, float to)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GainRamp.Apply(new float[8], 2, from, to));
    }

    [Fact]
    public void RejectsAChannelCountBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GainRamp.Apply(new float[8], 0, 1f, 0f));
    }

    [Fact]
    public void ApplyDoesNotAllocate()
    {
        var buf = Constant(960, 0.5f, 0.5f);
        for (var i = 0; i < 10; i++) GainRamp.Apply(buf, 2, 1f, 1f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { GainRamp.Apply(buf, 2, 0f, 1f); GainRamp.Apply(buf, 2, 1f, 1f); }
        var after = GC.GetAllocatedBytesForCurrentThread();
        // 2000 calls: any per-call allocation would be far above this
        Assert.True(after - before < 1024, (after - before).ToString());
    }

    // The way a device uses it: pump blocks, ramp the first one up and the last one down. The ramp
    // only attenuates, so the limiter's ceiling still holds, and the stream joins without a step.
    [Fact]
    public void RampedPumpOutputStaysUnderTheCeilingAndJoinsSmoothly()
    {
        const int block = 1920, blocks = 40;
        var engine = new Engine(Sr);
        engine.Master.SetEq(true, new double[] { 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 });
        engine.Load(new GeneratedSource(Sr, 300, 300, 0.9, block * blocks), lufs: null);
        var pump = new OutputPump(engine);
        var got = new float[block * blocks * 2];
        for (var b = 0; b < blocks; b++)
        {
            var span = got.AsSpan(b * block * 2, block * 2);
            pump.Fill(span);
            if (b == 0) GainRamp.Apply(span, 2, 0f, 1f);
            if (b == blocks - 1) GainRamp.Apply(span, 2, 1f, 0f);
        }

        Assert.True(AudioAssert.TruePeakDb(got) <= -0.8, AudioAssert.TruePeakDb(got).ToString());
        Assert.Equal(0f, got[^1]);
        Assert.Equal(0f, got[^2]);
        // no sample-to-sample jump larger than a 300 Hz tone can make at this level
        var maxStep = 0f;
        for (var i = 2; i < got.Length; i += 2) maxStep = Math.Max(maxStep, Math.Abs(got[i] - got[i - 2]));
        Assert.True(maxStep < 0.06f, maxStep.ToString());
    }
}
