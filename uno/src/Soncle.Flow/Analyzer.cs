// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Flow;

/// <summary>The analysis result, matching <c>analyze()</c> in <c>renderer/flow/analyze.js</c>.</summary>
public sealed record FlowAnalysis(double? Bpm, double BpmConf, string? Key, string? Camelot, double KeyConf, double Energy, double RmsDb, int V);

/// <summary>
/// Tempo (BPM), musical key (→ Camelot) and energy from ~30 s of mono PCM, a port of
/// <c>renderer/flow/analyze.js</c>. Uses the same radix-2 FFT and the Krumhansl–Kessler profiles
/// so the answers match the JS. Pure, allocation-light, no external data.
/// </summary>
public static class Analyzer
{
    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // Krumhansl–Kessler key profiles (the default 'kk' profile)
    private static readonly double[] Major = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] Minor = { 6.33, 2.68, 3.52, 5.38, 2.6, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

    private static readonly int[] CamelotMajor = { 8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1 };
    private static readonly int[] CamelotMinor = { 5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10 };

    public static string CamelotOf(int tonic, bool minor) =>
        (minor ? CamelotMinor[tonic] : CamelotMajor[tonic]) + (minor ? "A" : "B");

    public static string KeyName(int tonic, bool minor) => Names[tonic] + (minor ? "m" : "");

    /// <summary>Radix-2 in-place FFT, the same as the JS <c>fft</c>.</summary>
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wr = Math.Cos(ang); var wi = Math.Sin(ang);
            var half = len >> 1;
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < half; k++)
                {
                    var a = i + k; var b = a + half;
                    var xr = re[b] * cr - im[b] * ci;
                    var xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    var ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }

    /// <summary>Average chroma from a 4096-point FFT pass, whitened (the JS <c>keyChroma</c>).</summary>
    private static double[]? KeyChroma(ReadOnlySpan<float> pcm, int sr)
    {
        const int n = 4096, hop = 2048;
        const int half = n / 2;
        var frames = (pcm.Length - n) / hop;
        if (frames < 8) return null;
        var win = new double[n];
        for (var i = 0; i < n; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
        var re = new double[n]; var im = new double[n];
        var mag = new double[half]; var env = new double[half];
        var binHz = sr / (double)n;
        var lo = (int)Math.Floor(100 / binHz);
        var hi = Math.Min(half - 2, (int)Math.Ceiling(2500 / binHz));
        var outv = new double[12];
        for (var f = 0; f < frames; f++)
        {
            var o = f * hop;
            double e = 0;
            for (var i = 0; i < n; i++) { var x = pcm[o + i]; re[i] = x * win[i]; im[i] = 0; e += x * x; }
            if (e / n < 1e-6) continue;
            Fft(re, im);
            for (var k = 1; k < half; k++) mag[k] = Math.Sqrt(Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
            for (var k = lo; k <= hi; k++)
            {
                var w = Math.Max(2, (int)Math.Round(k * 0.12));
                double a = 0; var c = 0;
                for (var j = Math.Max(1, k - w); j <= Math.Min(half - 1, k + w); j++) { a += mag[j]; c++; }
                env[k] = a / c;
            }
            var fc = new double[12];
            for (var k = lo + 1; k < hi; k++)
            {
                var m = mag[k];
                if (m <= mag[k - 1] || m < mag[k + 1]) continue;   // spectral peaks only
                var a = mag[k - 1]; var b = m; var c = mag[k + 1]; var den = a - 2 * b + c;
                var p = den != 0 ? 0.5 * (a - c) / den : 0;
                var st0 = 12 * Math.Log2((k + p) * binHz / 440) + 69;
                var amp = m;
                for (var hN = 1; hN <= 1; hN++)   // opts.harmonics = 1
                {
                    var st = st0 - 12 * Math.Log2(hN);
                    var nearest = (int)Math.Round(st);
                    var dev = st - nearest;
                    if (Math.Abs(dev) > 0.5) continue;
                    var wgt = Math.Pow(Math.Cos(Math.Abs(dev) / 0.5 * Math.PI / 2), 2) * Math.Pow(0.6, hN - 1);
                    fc[((nearest % 12) + 12) % 12] += amp * wgt;
                }
            }
            var mx = 0.0; for (var i = 0; i < 12; i++) mx = Math.Max(mx, fc[i]);
            if (mx > 0) for (var i = 0; i < 12; i++) outv[i] += fc[i] / mx;
        }
        return outv;
    }

    private static double Corr(double[] a, double[] b)
    {
        var n = a.Length;
        double ma = 0, mb = 0;
        for (var i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
        ma /= n; mb /= n;
        double num = 0, da = 0, db = 0;
        for (var i = 0; i < n; i++) { var x = a[i] - ma; var y = b[i] - mb; num += x * y; da += x * x; db += y * y; }
        return num / Math.Sqrt(da * db != 0 ? da * db : 1);
    }

    public static FlowAnalysis? Analyze(ReadOnlySpan<float> pcm, int sr)
    {
        const int n = 2048, hop = 512;
        const int half = n / 2;
        var frames = (pcm.Length - n) / hop;
        if (frames < 40) return null;
        var win = new float[n];
        for (var i = 0; i < n; i++) win[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
        var re = new double[n]; var im = new double[n];
        var pc = new sbyte[half];
        for (var k = 0; k < half; k++) pc[k] = -1;
        for (var k = 1; k < half; k++)
        {
            var f = k * sr / (double)n;
            if (f < 55 || f > 4200) continue;
            var midi = 69 + 12 * Math.Log2(f / 440);
            pc[k] = (sbyte)((((int)Math.Round(midi) % 12) + 12) % 12);
        }
        var binCount = new double[12];
        for (var k = 1; k < half; k++) if (pc[k] >= 0) binCount[pc[k]]++;
        var chroma = new double[12];
        var flux = new float[frames];
        var prev = new float[half]; var cur = new float[half];
        double rmsSum = 0; var loudFrames = 0; double centroidSum = 0;
        for (var f = 0; f < frames; f++)
        {
            var o = f * hop;
            double e = 0;
            for (var i = 0; i < n; i++) { var x = pcm[o + i]; re[i] = x * win[i]; im[i] = 0; e += (double)x * x; }
            var rms = Math.Sqrt(e / n);
            Fft(re, im);
            double fl = 0, magSum = 0, cen = 0;
            var fc = new double[12];
            for (var k = 1; k < half; k++)
            {
                var m = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                var lm = Math.Log(1 + 100 * m);
                cur[k] = (float)lm;
                var d = lm - prev[k];
                if (d > 0) fl += d;
                magSum += m; cen += m * k;
                if (pc[k] >= 0) fc[pc[k]] += m;
            }
            flux[f] = (float)fl;
            if (rms > 0.003)
            {
                for (var i = 0; i < 12; i++) fc[i] /= binCount[i] != 0 ? binCount[i] : 1;
                var mx = 0.0; for (var i = 0; i < 12; i++) mx = Math.Max(mx, fc[i]);
                if (mx > 0) for (var i = 0; i < 12; i++) chroma[i] += fc[i] / mx;
                rmsSum += rms; loudFrames++;
                centroidSum += magSum != 0 ? cen / magSum * (sr / (double)n) : 0;
            }
            (prev, cur) = (cur, prev);
        }

        // tempo: autocorrelation of the onset envelope
        var fps = sr / (double)hop;
        double mean = 0; for (var i = 0; i < frames; i++) mean += flux[i]; mean /= frames;
        var env = new float[frames];
        var w = (int)Math.Round(fps * 0.25);
        var pre = new float[frames + 1];
        double acc = 0;
        for (var i = 0; i < frames; i++) { acc += flux[i]; pre[i + 1] = (float)acc; }
        for (var i = 0; i < frames; i++)
        {
            var a = Math.Max(0, i - w); var b = Math.Min(frames, i + w + 1);
            env[i] = (float)Math.Max(0, flux[i] - (pre[b] - pre[a]) / (b - a));
        }
        var minLag = (int)Math.Floor(fps * 60 / 200);
        var maxLag = (int)Math.Ceiling(fps * 60 / 55);
        var ac = new double[maxLag + 2];
        for (var lag = minLag - 1; lag <= maxLag + 1; lag++)
        {
            double s = 0;
            for (var i = 0; i + lag < frames; i++) s += (double)env[i] * env[i + lag];
            ac[lag] = s / (frames - lag);
        }
        double best = -1, bestS = double.NegativeInfinity, sumS = 0; var cnt = 0;
        double Score(double lag)
        {
            double V(double l) { var li = (int)Math.Round(l); return li >= minLag - 1 && li <= maxLag + 1 ? ac[li] : 0; }
            var bpm = 60 * fps / lag;
            var prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 120) / 0.9, 2));
            return (V(lag) + 0.5 * V(lag * 2) + 0.25 * V(lag / 2)) * (0.5 + 0.5 * prior);
        }
        for (var lag = minLag; lag <= maxLag; lag++) { var s = Score(lag); sumS += s; cnt++; if (s > bestS) { bestS = s; best = lag; } }
        double? bpmOut = null; var bpmConf = 0.0;
        if (best > 0 && bestS > 0)
        {
            var y0 = ac[(int)best - 1]; var y1 = ac[(int)best]; var y2 = ac[(int)best + 1];
            var den = y0 - 2 * y1 + y2;
            var shift = den != 0 ? Math.Max(-0.5, Math.Min(0.5, 0.5 * (y0 - y2) / den)) : 0;
            var bpm = 60 * fps / (best + shift);
            while (bpm < 70) bpm *= 2;
            while (bpm > 180) bpm /= 2;
            double ac0 = 0; for (var i = 0; i < frames; i++) ac0 += (double)env[i] * env[i]; ac0 /= frames;
            var periodic = ac0 != 0 ? ac[(int)best] / ac0 : 0;
            var peaky = bestS / (sumS / cnt);
            bpmConf = Math.Max(0, Math.Min(1, (periodic - 0.12) / 0.35 * Math.Min(1, (peaky - 1) / 1.5)));
            bpmOut = bpm;
        }

        var chroma2 = KeyChroma(pcm, sr);
        if (chroma2 is not null) chroma = chroma2;

        string? keyName = null; string? camelot = null; var keyConf = 0.0;
        if (loudFrames > 20)
        {
            var scores = new List<(int T, bool Minor, double R)>();
            for (var t = 0; t < 12; t++)
            {
                var rotM = new double[12]; var rotN = new double[12];
                for (var i = 0; i < 12; i++) { rotM[i] = Major[(i - t + 12) % 12]; rotN[i] = Minor[(i - t + 12) % 12]; }
                scores.Add((t, false, Corr(chroma, rotM)));
                scores.Add((t, true, Corr(chroma, rotN)));
            }
            scores.Sort((a, b) => b.R.CompareTo(a.R));
            var k = scores[0];
            keyName = KeyName(k.T, k.Minor);
            camelot = CamelotOf(k.T, k.Minor);
            double cm = 0; for (var i = 0; i < 12; i++) cm += chroma[i]; cm /= 12;
            double cv = 0; for (var i = 0; i < 12; i++) cv += Math.Pow(chroma[i] - cm, 2); cv = Math.Sqrt(cv / 12) / (cm != 0 ? cm : 1);
            var tonal = Math.Max(0, Math.Min(1, (cv - 0.08) / 0.3));
            keyConf = Math.Max(0, Math.Min(1, ((k.R - scores[1].R) * 8 + (k.R - 0.5)) * tonal));
        }

        var rmsDb = loudFrames != 0 ? 20 * Math.Log10(rmsSum / loudFrames) : -60;
        var bright = loudFrames != 0 ? centroidSum / loudFrames : 0;
        double onsetRate = 0; for (var i = 0; i < frames; i++) onsetRate += env[i] > 0 ? env[i] : 0; onsetRate /= frames;
        var energy = Math.Max(0, Math.Min(1, 0.45 * Math.Min(1, Math.Max(0, (rmsDb + 30) / 22)) + 0.3 * Math.Min(1, bright / 3500) + 0.25 * Math.Min(1, onsetRate / (mean != 0 ? mean : 1) * 0.5)));
        return new FlowAnalysis(
            bpmOut is not null ? Math.Round(bpmOut.Value * 10) / 10 : null,
            Math.Round(bpmConf, 2),
            keyName, camelot,
            Math.Round(keyConf, 2),
            Math.Round(energy, 3),
            Math.Round(rmsDb, 1),
            2);
    }
}
