// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.TestKit;

/// <summary>
/// Level helpers for <c>float[]</c> PCM. True peak is estimated by 4× oversampling with a
/// windowed-sinc interpolator (12 taps per phase), matching the factor the limiter meters use.
/// </summary>
public static class AudioAssert
{
    private const int Oversample = 4;   // 4× true peak, as the limiter measures
    private const int TapsPerPhase = 12;

    private const double FloorDb = -200.0;

    /// <summary>Sample peak of the signal in dBFS.</summary>
    public static double PeakDb(ReadOnlySpan<float> pcm)
    {
        var peak = 0.0;
        foreach (var s in pcm) peak = Math.Max(peak, Math.Abs(s));
        return ToDb(peak);
    }

    /// <summary>RMS level of the signal in dBFS.</summary>
    public static double RmsDb(ReadOnlySpan<float> pcm)
    {
        if (pcm.Length == 0) return FloorDb;
        var sum = 0.0;
        foreach (var s in pcm) sum += (double)s * s;
        return ToDb(Math.Sqrt(sum / pcm.Length));
    }

    /// <summary>
    /// True peak (dBTP) estimated by 4× oversampling. The signal is treated as a single channel;
    /// pass an interleaved signal only when the channel count is irrelevant to the measurement.
    /// </summary>
    public static double TruePeakDb(ReadOnlySpan<float> pcm) => ToDb(TruePeakLinear(pcm));

    /// <summary>Linear true-peak magnitude (before the dB conversion).</summary>
    public static double TruePeakLinear(ReadOnlySpan<float> pcm)
    {
        if (pcm.Length == 0) return 0;
        var kernel = PolyphaseKernel.Value;
        var peak = 0.0;
        for (var k = 0; k < pcm.Length; k++)
        {
            for (var p = 0; p < Oversample; p++)
            {
                var acc = 0.0;
                for (var t = 0; t < TapsPerPhase; t++)
                {
                    var idx = k - t;
                    if (idx < 0 || idx >= pcm.Length) continue;
                    acc += kernel[t * Oversample + p] * pcm[idx];
                }
                peak = Math.Max(peak, Math.Abs(acc));
            }
        }
        return peak;
    }

    private static double ToDb(double linear) =>
        linear <= 0 ? FloorDb : 20.0 * Math.Log10(linear);

    // Hann-windowed sinc lowpass, normalised per phase so each phase has unity DC gain.
    private static readonly Lazy<double[]> PolyphaseKernel = new(() =>
    {
        var length = TapsPerPhase * Oversample;
        var center = (length - 1) / 2.0;
        var h = new double[length];
        for (var n = 0; n < length; n++)
        {
            var x = (n - center) / Oversample;
            var sinc = x == 0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
            var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (length - 1));
            h[n] = sinc * window;
        }
        for (var p = 0; p < Oversample; p++)
        {
            var sum = 0.0;
            for (var t = 0; t < TapsPerPhase; t++) sum += h[t * Oversample + p];
            if (sum == 0) continue;
            for (var t = 0; t < TapsPerPhase; t++) h[t * Oversample + p] /= sum;
        }
        return h;
    });
}
