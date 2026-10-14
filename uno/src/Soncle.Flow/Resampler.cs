// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Flow;

/// <summary>
/// Stereo to mono and a sample-rate change, for the analysis path: what <c>OfflineAudioContext(1, 1, 22050)</c>
/// does for <c>renderer/flow/sampler.js</c> (average the channels, resample). A windowed-sinc low-pass
/// (Blackman, 16 zero-crossings each side) keeps aliasing out; the analysis only needs the numbers to be
/// close, not bit-identical to Chromium's resampler.
/// </summary>
public static class Resampler
{
    private const int Lobes = 16;

    public static float[] MonoFromStereo(ReadOnlySpan<float> interleaved)
    {
        var n = interleaved.Length / 2;
        var o = new float[n];
        for (var i = 0; i < n; i++) o[i] = (interleaved[2 * i] + interleaved[2 * i + 1]) / 2;
        return o;
    }

    public static float[] Resample(ReadOnlySpan<float> x, int srcRate, int dstRate)
    {
        if (srcRate <= 0 || dstRate <= 0) throw new ArgumentOutOfRangeException(nameof(srcRate));
        if (srcRate == dstRate) return x.ToArray();
        var ratio = (double)srcRate / dstRate;                 // source samples per output sample
        var outLen = (int)(x.Length / ratio);
        var o = new float[outLen];
        // cut-off at 0.95 of the lower Nyquist, in source samples
        var fc = Math.Min(1.0, 1.0 / ratio) * 0.95;            // cycles per source sample * 2
        var half = Lobes / fc;                                  // kernel half-width in source samples
        for (var i = 0; i < outLen; i++)
        {
            var c = i * ratio;
            var lo = Math.Max(0, (int)Math.Ceiling(c - half));
            var hi = Math.Min(x.Length - 1, (int)Math.Floor(c + half));
            double acc = 0, norm = 0;
            for (var k = lo; k <= hi; k++)
            {
                var d = k - c;
                var t = d * fc;
                var sinc = Math.Abs(t) < 1e-12 ? 1.0 : Math.Sin(Math.PI * t) / (Math.PI * t);
                var w = 0.42 + 0.5 * Math.Cos(Math.PI * d / half) + 0.08 * Math.Cos(2 * Math.PI * d / half);
                var g = sinc * w;
                acc += g * x[k]; norm += g;
            }
            o[i] = norm > 1e-9 ? (float)(acc / norm) : 0f;     // unity DC gain, also at the edges
        }
        return o;
    }
}
