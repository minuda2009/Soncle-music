// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Dsp;

/// <summary>
/// K-weighting (ITU-R BS.1770) for any sample rate — coefficients as in libebur128, a direct port
/// of <c>kWeighting</c>/<c>KFilter</c> in <c>renderer/audio/worklets.js</c>. Two cascaded biquads
/// per channel (transposed direct form II); <see cref="Square"/> returns the squared K-weighted
/// sample, which is what the loudness meter sums.
/// </summary>
public sealed class KFilter
{
    private readonly double _pb0, _pb1, _pb2, _pa1, _pa2;
    private readonly double _rb0, _rb1, _rb2, _ra1, _ra2;
    private readonly double[] _s = new double[8];

    public KFilter(double sampleRate)
    {
        double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
        var K = Math.Tan(Math.PI * f0 / sampleRate);
        var Vh = Math.Pow(10, G / 20);
        var Vb = Math.Pow(Vh, 0.4996667741545416);
        var a0 = 1 + K / Q + K * K;
        _pb0 = (Vh + Vb * K / Q + K * K) / a0;
        _pb1 = 2 * (K * K - Vh) / a0;
        _pb2 = (Vh - Vb * K / Q + K * K) / a0;
        _pa1 = 2 * (K * K - 1) / a0;
        _pa2 = (1 - K / Q + K * K) / a0;

        f0 = 38.13547087602444; Q = 0.5003270373238773;
        K = Math.Tan(Math.PI * f0 / sampleRate);
        a0 = 1 + K / Q + K * K;
        _rb0 = 1; _rb1 = -2; _rb2 = 1;
        _ra1 = 2 * (K * K - 1) / a0;
        _ra2 = (1 - K / Q + K * K) / a0;
    }

    /// <summary>Squared K-weighted sample for channel <paramref name="ch"/> (0 or 1).</summary>
    public double Square(double x, int ch)
    {
        var o = ch * 4;
        var y1 = _pb0 * x + _s[o];
        _s[o] = _pb1 * x - _pa1 * y1 + _s[o + 1];
        _s[o + 1] = _pb2 * x - _pa2 * y1;
        var y2 = _rb0 * y1 + _s[o + 2];
        _s[o + 2] = _rb1 * y1 - _ra1 * y2 + _s[o + 3];
        _s[o + 3] = _rb2 * y1 - _ra2 * y2;
        return y2 * y2;
    }

    /// <summary>LUFS from a mean square (the worklet's <c>lufsOf</c>).</summary>
    public static double LufsOf(double meanSquare) =>
        meanSquare > 0 ? -0.691 + 10 * Math.Log10(meanSquare) : double.NegativeInfinity;
}

/// <summary>
/// Gated integrated loudness from 100 ms sub-blocks (400 ms blocks, 75 % overlap), a port of
/// <c>Integrator</c> in <c>worklets.js</c>. <c>Value()</c> returns the integrated LUFS, or null.
/// </summary>
public sealed class Integrator
{
    private readonly int _hop;
    private readonly double[] _sub = new double[4];
    private readonly List<double> _blocks = new();
    private double _acc;
    private int _n;
    private int _subN;

    public Integrator(double sampleRate) => _hop = (int)Math.Round(sampleRate * 0.1);

    public void Reset()
    {
        _acc = 0; _n = 0; _subN = 0; _blocks.Clear();
        Array.Clear(_sub);
    }

    /// <summary>Add one sample's summed channel energy.</summary>
    public void Add(double e)
    {
        _acc += e;
        if (++_n < _hop) return;
        _sub[_subN++ % 4] = _acc / _n;
        _acc = 0; _n = 0;
        if (_subN >= 4)
        {
            var ms = (_sub[0] + _sub[1] + _sub[2] + _sub[3]) / 4;
            if (KFilter.LufsOf(ms) > -70) _blocks.Add(ms);   // absolute gate
        }
    }

    public double? Value()
    {
        if (_blocks.Count == 0) return null;
        var sum = 0.0;
        foreach (var b in _blocks) sum += b;
        var rel = KFilter.LufsOf(sum / _blocks.Count) - 10;   // relative gate
        var s2 = 0.0; var n2 = 0;
        foreach (var b in _blocks) if (KFilter.LufsOf(b) > rel) { s2 += b; n2++; }
        return n2 > 0 ? KFilter.LufsOf(s2 / n2) : null;
    }

    /// <summary>How many 100 ms sub-blocks have been integrated (the meter's <c>seconds</c> × 10).</summary>
    public int SubBlocks => _subN;
}
