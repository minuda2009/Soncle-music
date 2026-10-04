// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.TestKit;

/// <summary>
/// Level helpers for <c>float[]</c> PCM. True peak uses the same 4× oversampling interpolator the
/// app's limiter uses (<c>renderer/audio/worklets.js</c>: <c>TAPS = 12</c>, <c>PHASES = 4</c>), so
/// the numbers here match what the app measures — the sound-engine tasks (M04, M10) compare against
/// these.
/// </summary>
public static class AudioAssert
{
    private const int Taps = 12;    // taps per phase, as in worklets.js
    private const int Phases = 4;   // 4× true peak, as the limiter measures

    private const double FloorDb = -200.0;

    /// <summary>Sample peak of the signal in dBFS.</summary>
    public static double PeakDb(ReadOnlySpan<float> pcm) => ToDb(PeakLinear(pcm));

    /// <summary>Sample peak of the signal (linear).</summary>
    public static double PeakLinear(ReadOnlySpan<float> pcm)
    {
        var peak = 0.0;
        foreach (var s in pcm) peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }

    /// <summary>RMS level of the signal in dBFS.</summary>
    public static double RmsDb(ReadOnlySpan<float> pcm)
    {
        if (pcm.Length == 0) return FloorDb;
        var sum = 0.0;
        foreach (var s in pcm) sum += (double)s * s;
        return ToDb(Math.Sqrt(sum / pcm.Length));
    }

    /// <summary>True peak (dBTP), 4× oversampled with the limiter's interpolator.</summary>
    public static double TruePeakDb(ReadOnlySpan<float> pcm) => ToDb(TruePeakLinear(pcm));

    /// <summary>
    /// Linear true-peak magnitude: the sample peak and the three interpolated phases, exactly as
    /// the worklet's meter takes it. (The worklet also includes the FD-1/FD-2 samples, which are
    /// ≤ the sample peak, so the sample peak covers them.)
    /// </summary>
    public static double TruePeakLinear(ReadOnlySpan<float> pcm)
    {
        var peak = PeakLinear(pcm);
        var kernel = Polyphase.Value;
        for (var n = 0; n < pcm.Length; n++)
        {
            for (var p = 1; p < Phases; p++)
            {
                var phase = kernel[p];
                var acc = 0.0;
                for (var k = 0; k < Taps; k++)
                {
                    var idx = n - k;
                    if (idx < 0) continue;
                    acc += phase[k] * pcm[idx];
                }
                peak = Math.Max(peak, Math.Abs(acc));
            }
        }
        return peak;
    }

    private static double ToDb(double linear) =>
        linear <= 0 ? FloorDb : 20.0 * Math.Log10(linear);

    // Hann-windowed sinc, 12 taps × 4 phases, each phase normalised to unity DC gain — a direct
    // port of the POLY table in renderer/audio/worklets.js.
    private static readonly Lazy<double[][]> Polyphase = new(() =>
    {
        const int n = Taps * Phases;
        var h = new double[n];
        var center = (n - 1) / 2.0;
        for (var i = 0; i < n; i++)
        {
            var x = (i - center) / Phases;
            var sinc = x == 0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
            var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 0.5) / n);
            h[i] = sinc * window;
        }
        var phases = new double[Phases][];
        for (var p = 0; p < Phases; p++)
        {
            var f = new double[Taps];
            var sum = 0.0;
            for (var k = 0; k < Taps; k++)
            {
                f[k] = h[p + k * Phases];
                sum += f[k];
            }
            for (var k = 0; k < Taps; k++) f[k] /= sum;
            phases[p] = f;
        }
        return phases;
    });
}
