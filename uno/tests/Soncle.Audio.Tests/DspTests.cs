// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Audio.Dsp;
using Soncle.TestKit;

namespace Soncle.Audio.Tests;

/// <summary>Golden tests: the C# DSP must match the JS reference in <c>fixtures/audio</c>.</summary>
public class DspTests
{
    private static float[] ReadF32(string name)
    {
        var bytes = File.ReadAllBytes(Fixtures.Path("audio/" + name));
        var f = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
        return f;
    }

    private static JsonElement ReadJson(string name) =>
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("audio/" + name))).RootElement;

    [Fact]
    public void LimiterKeepsTruePeakUnderMinusOneAndMatchesTheJsOutput()
    {
        var meta = ReadJson("limiter-hot.json");
        var sr = meta.GetProperty("sampleRate").GetInt32();
        var frames = meta.GetProperty("frames").GetInt32();
        var delay = meta.GetProperty("delay").GetInt32();
        var input = ReadF32("limiter-hot-in.f32");
        var expected = ReadF32("limiter-hot-out.f32");

        var limiter = new TruePeakLimiter(sr);
        var buf = (float[])input.Clone();
        limiter.Process(buf, frames);

        Assert.Equal(delay, limiter.Delay);
        // sample peak must not exceed the −1 dBTP ceiling (10^(-1/20))
        var ceil = Math.Pow(10, -1.0 / 20);
        Assert.True(AudioAssert.PeakLinear(buf) <= ceil + 1e-6, $"sample peak {AudioAssert.PeakLinear(buf):F4}");
        // output true peak < −0.8 dBTP, measured the same way as the JS test (16×, skip the head)
        var tp = Math.Max(TruePeak16(buf, 0, frames, 1000), TruePeak16(buf, 1, frames, 1000));
        Assert.True(20 * Math.Log10(tp) < -0.8, $"true peak {20 * Math.Log10(tp):F3} dBTP");

        // RMS difference vs the JS output within 0.1 dB
        var gotRms = AudioAssert.RmsDb(buf);
        var expRms = AudioAssert.RmsDb(expected);
        Assert.True(Math.Abs(gotRms - expRms) <= 0.1, $"RMS {gotRms:F3} vs {expRms:F3} dB");
    }

    // 16× band-limited true peak, ported from test/worklets.test.mjs's truePeak()
    private static double TruePeak16(float[] interleaved, int channel, int frames, int skip)
    {
        var m = 0.0;
        for (var i = 8 + skip; i < frames - 8; i++)
            for (var f = 0; f < 16; f++)
            {
                var t = f / 16.0; var s = 0.0;
                for (var k = -8; k <= 8; k++)
                {
                    var d = k - t;
                    var sinc = d == 0 ? 1 : Math.Sin(Math.PI * d) / (Math.PI * d);
                    s += interleaved[(i + k) * 2 + channel] * sinc * (0.5 + 0.5 * Math.Cos(Math.PI * d / 9));
                }
                m = Math.Max(m, Math.Abs(s));
            }
        return m;
    }

    [Fact]
    public void LimiterIsTransparentBelowTheCeiling()
    {
        var meta = ReadJson("limiter-clean.json");
        var sr = meta.GetProperty("sampleRate").GetInt32();
        var frames = meta.GetProperty("frames").GetInt32();
        var delay = meta.GetProperty("delay").GetInt32();
        var input = ReadF32("limiter-clean-in.f32");
        var expected = ReadF32("limiter-clean-out.f32");

        var limiter = new TruePeakLimiter(sr);
        var buf = (float[])input.Clone();
        limiter.Process(buf, frames);

        // output equals the input delayed by the look-ahead, sample-exact within 1e-6
        var maxErr = 0.0;
        for (var i = delay * 2; i < frames * 2; i++)
            maxErr = Math.Max(maxErr, Math.Abs(buf[i] - input[i - delay * 2]));
        Assert.True(maxErr < 1e-6, $"max err {maxErr:E3}");
        // and it matches the JS output
        var jsErr = 0.0;
        for (var i = 0; i < frames * 2; i++) jsErr = Math.Max(jsErr, Math.Abs(buf[i] - expected[i]));
        Assert.True(jsErr < 1e-4, $"vs JS max err {jsErr:E3}");
    }

    [Fact]
    public void LoudnessMeterReadsTheReferenceTone()
    {
        var meta = ReadJson("lufs-ref.json");
        var sr = meta.GetProperty("sampleRate").GetInt32();
        var frames = meta.GetProperty("frames").GetInt32();
        var expected = meta.GetProperty("lufs").GetDouble();
        var input = ReadF32("lufs-ref-in.f32");

        var kf = new KFilter(sr);
        var integ = new Integrator(sr);
        for (var i = 0; i < frames; i++)
            integ.Add(kf.Square(input[i * 2], 0) + kf.Square(input[i * 2 + 1], 1));

        var lufs = integ.Value();
        Assert.NotNull(lufs);
        Assert.InRange(lufs!.Value, expected - 0.1, expected + 0.1);
    }

    [Fact]
    public void CrossfadeCurvesMatchTheJs()
    {
        var root = ReadJson("crossfade-curves.json").GetProperty("curves");
        foreach (var curve in root.EnumerateObject())
        {
            var points = curve.Value.EnumerateArray().ToArray();
            for (var i = 0; i < points.Length; i++)
            {
                var (inG, outG) = CrossfadeCurves.Gains(curve.Name, i / (double)(points.Length - 1));
                var pair = points[i].EnumerateArray().ToArray();
                Assert.True(Math.Abs(inG - pair[0].GetDouble()) < 1e-3, $"{curve.Name}[{i}] in");
                Assert.True(Math.Abs(outG - pair[1].GetDouble()) < 1e-3, $"{curve.Name}[{i}] out");
            }
        }
    }

    [Fact]
    public void NoCrossfadeCurveIsLouderThanEitherSong()
    {
        // the rule from test/crossfade.test.mjs: amplitudes sum to ≤ 1 for every curve
        foreach (var curve in new[] { "smooth", "equalpower", "linear" })
        {
            var peak = 0.0;
            for (var i = 0; i <= 1000; i++)
            {
                var (a, b) = CrossfadeCurves.Gains(curve, i / 1000.0);
                peak = Math.Max(peak, Math.Max(a, b));
            }
            Assert.True(peak <= 1.0 + 1e-9, $"{curve} peak {peak}");
        }
    }

    [Fact]
    public void EqPresetsMatchTheJs()
    {
        var eq = ReadJson("eq.json");
        var bands = eq.GetProperty("bands").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.Equal(new double[] { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }, bands);
        var flat = eq.GetProperty("presets").GetProperty("Flat").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.All(flat, g => Assert.Equal(0, g));
    }

    [Fact]
    public void BiquadMatchesRbjMagnitude()
    {
        // a +6 dB low shelf at 180 Hz: near-DC gain ~ +6 dB, well above the shelf ~ 0 dB
        var b = new Biquad();
        b.LowShelf(48000, 180, 6);
        Assert.InRange(b.MagnitudeDb(48000, 20), 5.5, 6.5);
        Assert.InRange(b.MagnitudeDb(48000, 16000), -0.5, 0.5);
        // a −6 dB peaking band at 1 kHz
        var p = new Biquad();
        p.Peaking(48000, 1000, -6, 1.0);
        Assert.InRange(p.MagnitudeDb(48000, 1000), -6.2, -5.8);
    }
}
