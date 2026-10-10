// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Dsp;

/// <summary>
/// A single biquad section, Direct Form I in float, with coefficients from the RBJ Audio EQ
/// Cookbook — the formulas Web Audio's <c>BiquadFilterNode</c> uses, so a filter configured the
/// same way sounds the same. Ported from the <c>createBiquadFilter</c> usage in
/// <c>renderer/engine.js</c>.
/// </summary>
public sealed class Biquad
{
    private float _b0, _b1, _b2, _a1, _a2;
    private float _x1, _x2, _y1, _y2;

    public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;

    /// <summary>Low shelf (RBJ), used for the 180 Hz bass shelf and the 100 Hz loudness lift.</summary>
    public void LowShelf(double sampleRate, double frequency, double gainDb, double q = 0.7071067811865476)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var twoSqrtAAlpha = 2 * Math.Sqrt(a) * alpha;
        var b0 = a * ((a + 1) - (a - 1) * cos + twoSqrtAAlpha);
        var b1 = 2 * a * ((a - 1) - (a + 1) * cos);
        var b2 = a * ((a + 1) - (a - 1) * cos - twoSqrtAAlpha);
        var a0 = (a + 1) + (a - 1) * cos + twoSqrtAAlpha;
        var a1 = -2 * ((a - 1) + (a + 1) * cos);
        var a2 = (a + 1) + (a - 1) * cos - twoSqrtAAlpha;
        Set(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>High shelf (RBJ), used for the 9 kHz loudness lift.</summary>
    public void HighShelf(double sampleRate, double frequency, double gainDb, double q = 0.7071067811865476)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var twoSqrtAAlpha = 2 * Math.Sqrt(a) * alpha;
        var b0 = a * ((a + 1) + (a - 1) * cos + twoSqrtAAlpha);
        var b1 = -2 * a * ((a - 1) + (a + 1) * cos);
        var b2 = a * ((a + 1) + (a - 1) * cos - twoSqrtAAlpha);
        var a0 = (a + 1) - (a - 1) * cos + twoSqrtAAlpha;
        var a1 = 2 * ((a - 1) - (a + 1) * cos);
        var a2 = (a + 1) - (a - 1) * cos - twoSqrtAAlpha;
        Set(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>Peaking EQ (RBJ), used for the 10 EQ bands.</summary>
    public void Peaking(double sampleRate, double frequency, double gainDb, double q = 1.0)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var b0 = 1 + alpha * a;
        var b1 = -2 * cos;
        var b2 = 1 - alpha * a;
        var a0 = 1 + alpha / a;
        var a1 = -2 * cos;
        var a2 = 1 - alpha / a;
        Set(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>Low pass (RBJ), used by the crossfeed paths (700 Hz, Q 0.5).</summary>
    public void LowPass(double sampleRate, double frequency, double q = 0.7071067811865476)
    {
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var b0 = (1 - cos) / 2;
        var b1 = 1 - cos;
        var b2 = (1 - cos) / 2;
        var a0 = 1 + alpha;
        var a1 = -2 * cos;
        var a2 = 1 - alpha;
        Set(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>High pass (RBJ).</summary>
    public void HighPass(double sampleRate, double frequency, double q = 0.7071067811865476)
    {
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var b0 = (1 + cos) / 2;
        var b1 = -(1 + cos);
        var b2 = (1 + cos) / 2;
        var a0 = 1 + alpha;
        var a1 = -2 * cos;
        var a2 = 1 - alpha;
        Set(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    private void Set(double b0, double b1, double b2, double a1, double a2)
    {
        _b0 = (float)b0; _b1 = (float)b1; _b2 = (float)b2; _a1 = (float)a1; _a2 = (float)a2;
        Reset();
    }

    public float Process(float x)
    {
        var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
        return y;
    }

    /// <summary>Magnitude response in dB at <paramref name="frequency"/> (for the EQ-response fixture).</summary>
    public double MagnitudeDb(double sampleRate, double frequency)
    {
        var w = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w);
        var sin = Math.Sin(w);
        // H(e^jw) = (b0 + b1 e^-jw + b2 e^-2jw) / (1 + a1 e^-jw + a2 e^-2jw)
        var numRe = _b0 + _b1 * cos + _b2 * Math.Cos(2 * w);
        var numIm = -(_b1 * sin + _b2 * Math.Sin(2 * w));
        var denRe = 1 + _a1 * cos + _a2 * Math.Cos(2 * w);
        var denIm = -(_a1 * sin + _a2 * Math.Sin(2 * w));
        var num = Math.Sqrt(numRe * numRe + numIm * numIm);
        var den = Math.Sqrt(denRe * denRe + denIm * denIm);
        return 20 * Math.Log10(num / den);
    }
}
