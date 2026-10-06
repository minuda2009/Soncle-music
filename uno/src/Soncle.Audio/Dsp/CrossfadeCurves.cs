// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Dsp;

/// <summary>
/// The crossfade gain curves, a direct port of <c>xfGains</c> in <c>renderer/engine.js</c>. Every
/// blend style goes through these, so no style can make the overlap louder than either song.
/// Returns [incoming, outgoing].
/// </summary>
public static class CrossfadeCurves
{
    public static (double In, double Out) Gains(string curve, double x)
    {
        x = Math.Max(0, Math.Min(1, x));
        if (curve == "equalpower") return (Math.Sin(x * Math.PI / 2), Math.Cos(x * Math.PI / 2));
        if (curve == "linear") return (x, 1 - x);
        var o = (1 - x) * (1 - x);
        return (1 - o, o);
    }
}

/// <summary>
/// The stereo width + headphone crossfeed 2×2 matrix, ported from <c>#applySpace</c> in
/// <c>renderer/engine.js</c>. The crossfeed paths are low-passed (700 Hz, Q 0.5) and delayed
/// 0.28 ms, like bs2b; <see cref="Process"/> runs one sample and writes the stereo pair.
/// </summary>
public sealed class StereoSpace
{
    private const double DelaySeconds = 0.00028;
    private const double LowPassHz = 700;
    private const double LowPassQ = 0.5;

    private readonly int _delaySamples;
    private readonly float[] _delayL, _delayR;
    private int _delayPos;

    private readonly Biquad _lpL = new();
    private readonly Biquad _lpR = new();

    private double _ll = 1, _rr = 1, _lr = 0, _rl = 0, _xl = 0, _xr = 0, _out = 1;

    public StereoSpace(int sampleRate)
    {
        _delaySamples = Math.Max(1, (int)Math.Round(sampleRate * DelaySeconds));
        _delayL = new float[_delaySamples];
        _delayR = new float[_delaySamples];
        _lpL.LowPass(sampleRate, LowPassHz, LowPassQ);
        _lpR.LowPass(sampleRate, LowPassHz, LowPassQ);
    }

    /// <summary>Sets width (0…2, 1 = original) and crossfeed (0, 0.35 light, 0.55 strong).</summary>
    public void Set(double width, double crossfeed)
    {
        var w = Math.Max(0, Math.Min(2, width));
        var direct = (1 + w) / 2;
        var cross = (1 - w) / 2;
        var c = Math.Max(0, crossfeed);
        _ll = direct; _rr = direct; _lr = cross; _rl = cross;
        _xl = c; _xr = c;
        _out = 1.0 / Math.Max(1, Math.Abs(direct) + Math.Abs(cross) + c);
    }

    public void Process(float l, float r, out float outL, out float outR)
    {
        // crossfeed paths: low-passed, delayed opposite channels
        var xfR = _lpR.Process(r);
        var xfL = _lpL.Process(l);
        var dL = _delayL[_delayPos];
        var dR = _delayR[_delayPos];
        _delayL[_delayPos] = (float)xfL;
        _delayR[_delayPos] = (float)xfR;
        _delayPos = _delayPos + 1 == _delaySamples ? 0 : _delayPos + 1;
        // matrix: out_L = ll*L + rl*R + xl*delayed_xfL ; out_R = rr*R + lr*L + xr*delayed_xfR
        var oL = _ll * l + _rl * r + _xl * dL;
        var oR = _rr * r + _lr * l + _xr * dR;
        outL = (float)(oL * _out);
        outR = (float)(oR * _out);
    }
}
