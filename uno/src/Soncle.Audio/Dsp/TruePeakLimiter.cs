// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Dsp;

/// <summary>The 4× oversampling interpolator (12 taps/phase) used for true-peak estimation.</summary>
public static class TruePeakKernel
{
    public const int Taps = 12;
    public const int Phases = 4;

    /// <summary>Per-phase tap weights, each phase normalised to unity DC gain.</summary>
    public static readonly double[][] Poly = Build();

    private static double[][] Build()
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
            for (var k = 0; k < Taps; k++) { f[k] = h[p + k * Phases]; sum += f[k]; }
            for (var k = 0; k < Taps; k++) f[k] /= sum;
            phases[p] = f;
        }
        return phases;
    }
}

/// <summary>Meter values the limiter reports, read without locking.</summary>
public readonly record struct MeterSnapshot(double Momentary, double Short, double TruePeakDb, double ReductionDb);

/// <summary>
/// True-peak look-ahead limiter, a sample-for-sample port of <c>MasterProcessor</c> in
/// <c>renderer/audio/worklets.js</c>: 2 ms look-ahead, a sliding minimum of the required gain plus
/// a box average, an 80 ms release, a −1 dBTP ceiling, and optional BS.1770 meters. It is
/// allocation-free inside <see cref="Process"/>.
/// </summary>
public sealed class TruePeakLimiter
{
    private const int Quantum = 128;   // the AudioWorklet block size, kept so metering matches the JS

    private readonly int _taps = TruePeakKernel.Taps;
    private readonly int _fd;
    private readonly int _d;
    private readonly int _l;
    private readonly double _rel;
    private readonly double _ceiling;

    private readonly double[] _h0, _h1;
    private int _hpos;
    private readonly float[] _dl0, _dl1;
    private readonly int _dsize, _mask;
    private int _dpos;

    private readonly double[] _qv, _qi;
    private readonly int _qsize, _qm;
    private int _qh, _qt;
    private long _t;

    private readonly double[] _box;
    private int _bpos;
    private double _bsum;
    private double _env = 1;

    private bool _metering;
    private readonly KFilter _kf;
    private readonly double[] _mom, _short;
    private int _momPos, _shortPos;
    private double _momAcc, _shortAcc;
    private double _tpHold, _grMin = 1;
    private int _frames;
    private readonly int _reportEvery;

    private double _lastMom = double.NegativeInfinity, _lastShort = double.NegativeInfinity, _lastTp = double.NegativeInfinity, _lastGr;

    public TruePeakLimiter(int sampleRate)
    {

        _ceiling = Math.Pow(10, -1.0 / 20);            // −1 dBTP
        _l = Math.Max(16, (int)Math.Ceiling(sampleRate * 0.002));
        _fd = _taps / 2;
        _d = _l - 1 + _fd;
        _rel = 1 - Math.Exp(-1.0 / (sampleRate * 0.08));

        _h0 = new double[_taps * 2]; _h1 = new double[_taps * 2];
        _dsize = 1; while (_dsize < _d + 256) _dsize <<= 1;
        _mask = _dsize - 1;
        _dl0 = new float[_dsize]; _dl1 = new float[_dsize];
        _qsize = 1; while (_qsize < _l + 2) _qsize <<= 1;
        _qm = _qsize - 1;
        _qv = new double[_qsize]; _qi = new double[_qsize];
        _box = new double[_l];
        Array.Fill(_box, 1);
        _bsum = _l;
        _kf = new KFilter(sampleRate);
        _mom = new double[(int)Math.Round(sampleRate * 0.4 / Quantum) + 1];
        _short = new double[(int)Math.Round(sampleRate * 3.0 / Quantum) + 1];
        _reportEvery = Math.Max(1, (int)Math.Round(sampleRate / 15.0 / Quantum));
    }

    /// <summary>Audio delay (samples) the limiter adds; also the true-peak interpolator's delay.</summary>
    public int Delay => _d;

    public void SetMetering(bool on) { _metering = on; _tpHold = 0; }

    /// <summary>The last meter report (momentary, short-term, true peak, gain reduction).</summary>
    public MeterSnapshot Meters => new(_lastMom, _lastShort, _lastTp, _lastGr);

    /// <summary>
    /// Processes <paramref name="frames"/> interleaved stereo samples in place. Runs in 128-frame
    /// quanta so the meters read the same as the JS worklet.
    /// </summary>
    public void Process(Span<float> interleaved, int frames)
    {
        var done = 0;
        while (done < frames)
        {
            var n = Math.Min(Quantum, frames - done);
            ProcessQuantum(interleaved.Slice(done * 2, n * 2), n);
            done += n;
        }
    }

    private void ProcessQuantum(Span<float> buf, int n)
    {
        var ceil = _ceiling;
        var gate = ceil * 0.6;   // inter-sample overs only matter within ~4.4 dB
        var h0 = _h0; var h1 = _h1; var dl0 = _dl0; var dl1 = _dl1; var mask = _mask; var d = _d;
        var rel = _rel; var l = _l; var qv = _qv; var qi = _qi; var qm = _qm; var box = _box;
        var p1 = TruePeakKernel.Poly[1]; var p2 = TruePeakKernel.Poly[2]; var p3 = TruePeakKernel.Poly[3];
        var fd = _fd;
        var hp = _hpos; var dpos = _dpos; var qh = _qh; var qt = _qt; var t = _t; var bpos = _bpos;
        var bsum = _bsum; var env = _env;
        double momE = 0, tpBlock = 0, grBlock = 1;

        for (var i = 0; i < n; i++)
        {
            var xl = buf[i * 2];
            var xr = buf[i * 2 + 1];
            h0[hp] = xl; h0[hp + _taps] = xl; h1[hp] = xr; h1[hp + _taps] = xr;
            var baseIdx = hp + 1;
            var c0 = baseIdx + _taps - fd; var c1 = c0 + 1;
            double a0 = h0[c0], a1 = h0[c1], b0 = h1[c0], b1 = h1[c1];
            var peak = Math.Abs(a0);
            if (Math.Abs(a1) > peak) peak = Math.Abs(a1);
            if (Math.Abs(b0) > peak) peak = Math.Abs(b0);
            if (Math.Abs(b1) > peak) peak = Math.Abs(b1);
            if (peak > gate)
            {
                double s1 = 0, s2 = 0, s3 = 0, r1 = 0, r2 = 0, r3 = 0;
                for (var k = 0; k < _taps; k++)
                {
                    var j = baseIdx + _taps - 1 - k;
                    double x0 = h0[j], x1 = h1[j];
                    s1 += p1[k] * x0; s2 += p2[k] * x0; s3 += p3[k] * x0;
                    r1 += p1[k] * x1; r2 += p2[k] * x1; r3 += p3[k] * x1;
                }
                var m = Math.Max(Math.Max(Math.Abs(s1), Math.Abs(s2)), Math.Abs(s3));
                m = Math.Max(m, Math.Max(Math.Abs(r1), Math.Abs(r2)));
                m = Math.Max(m, Math.Abs(r3));
                if (m > peak) peak = m;
            }
            hp = hp + 1 == _taps ? 0 : hp + 1;
            if (peak > tpBlock) tpBlock = peak;
            var need = peak > ceil ? ceil / peak : 1.0;
            while (qt != qh && qv[(qt - 1) & qm] >= need) qt = (qt - 1) & qm;
            qv[qt] = need; qi[qt] = t; qt = (qt + 1) & qm;
            if (qi[qh] <= t - l) qh = (qh + 1) & qm;
            t++;
            var mn = qv[qh];
            bsum += mn - box[bpos]; box[bpos] = mn; bpos = bpos + 1 == l ? 0 : bpos + 1;
            var g = bsum / l;
            if (g < env) env = g;
            else if (env < 1) { env += (g - env) * rel; if (env > 0.99999) env = 1; }
            dl0[dpos] = xl; dl1[dpos] = xr;
            var r = (dpos - d) & mask; dpos = (dpos + 1) & mask;
            var yl = dl0[r] * env; var yr = dl1[r] * env;
            buf[i * 2] = (float)yl; buf[i * 2 + 1] = (float)yr;
            if (env < grBlock) grBlock = env;
            if (_metering) momE += _kf.Square(yl, 0) + _kf.Square(yr, 1);
        }

        if ((t & 0xffff) < n) { bsum = 0; for (var i = 0; i < l; i++) bsum += box[i]; }   // float drift guard
        _hpos = hp; _dpos = dpos; _qh = qh; _qt = qt; _t = t; _bpos = bpos; _bsum = bsum; _env = env;

        if (_metering)
        {
            var e = momE / n;
            _momAcc += e - _mom[_momPos]; _mom[_momPos] = e; _momPos = (_momPos + 1) % _mom.Length;
            _shortAcc += e - _short[_shortPos]; _short[_shortPos] = e; _shortPos = (_shortPos + 1) % _short.Length;
            if (tpBlock > _tpHold) _tpHold = tpBlock;
            if (grBlock < _grMin) _grMin = grBlock;
            if (++_frames >= _reportEvery)
            {
                _frames = 0;
                _lastMom = KFilter.LufsOf(_momAcc / _mom.Length);
                _lastShort = KFilter.LufsOf(_shortAcc / _short.Length);
                _lastTp = _tpHold > 0 ? 20 * Math.Log10(_tpHold) : double.NegativeInfinity;
                _lastGr = 20 * Math.Log10(_grMin);
                _grMin = 1;
            }
        }
    }
}
