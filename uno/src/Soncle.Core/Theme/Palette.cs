// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;

namespace Soncle.Core.Theme;

/// <summary>
/// Soncle's colours: the Material-style palette the apps and the browser extension are tinted with,
/// from one hue and saturation (an accent, or the dominant colour of the cover art). A faithful,
/// DOM-free port of <c>renderer/palette.js</c>; the output is pinned by <c>fixtures/palette/golden.json</c>.
/// JS function and variable names are kept (in C# casing); the order of operations, the clamp
/// expressions and the constants are preserved verbatim so the colours match byte-for-byte.
/// </summary>
public static class Palette
{
    /// <summary>
    /// <c>rgbToHsl(r, g, b)</c>: RGB (0–255) to <c>[hue°, saturation%, lightness%]</c>.
    /// </summary>
    public static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        r /= 255; g /= 255; b /= 255;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double hh = 0, s = 0;
        double l = (max + min) / 2;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            hh = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            hh *= 60;
        }
        return (hh, s * 100, l * 100);
    }

    /// <summary>
    /// <c>hslToRgb(hh, s, l)</c>: HSL (hue°, s%, l%) to three RGB bytes. JS <c>Math.round</c> is
    /// <c>floor(x + 0.5)</c> (round half toward +infinity), reproduced here so bytes match exactly.
    /// </summary>
    public static (int R, int G, int B) HslToRgb(double hh, double s, double l)
    {
        s /= 100; l /= 100;
        double a = s * Math.Min(l, 1 - l);
        double K(double n) => (n + hh / 30) % 12;
        double F(double n) => l - a * Math.Max(-1, Math.Min(K(n) - 3, Math.Min(9 - K(n), 1)));
        return (Round(F(0) * 255), Round(F(8) * 255), Round(F(4) * 255));
    }

    /// <summary>
    /// <c>hexToHsl(hex)</c>: a <c>#rrggbb</c> string to <c>[hue°, saturation%, lightness%]</c>.
    /// </summary>
    public static (double H, double S, double L) HexToHsl(string hex) => RgbToHsl(
        int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    /// <summary>
    /// <c>hsl(hh, s, l)</c>: the CSS string, e.g. <c>hsl(240.0 35.0% 80.0%)</c> — each number
    /// formatted with one decimal via <see cref="CultureInfo.InvariantCulture"/> (<c>toFixed(1)</c>).
    /// </summary>
    public static string Hsl(double hh, double s, double l) =>
        $"hsl({F1(hh)} {F1(s)}% {F1(l)}%)";

    /// <summary>
    /// <c>themeVars(hue, sat, mode)</c>: the CSS custom-property map for a theme.
    /// <paramref name="mode"/> is <c>"dark"</c>, <c>"black"</c> or <c>"light"</c>. The returned
    /// dictionary preserves insertion order, matching the object the JS builds.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ThemeVars(double hue, double sat, string mode)
    {
        double s = Math.Min(sat, 80);
        var v = new Dictionary<string, string>();
        if (mode == "light")
        {
            v["--primary"] = Hsl(hue, Math.Max(Math.Min(s, 70), 35), 40);
            v["--on-primary"] = "#ffffff";
            v["--primary-container"] = Hsl(hue, Math.Max(Math.Min(s, 80), 40), 88);
            v["--on-primary-container"] = Hsl(hue, Math.Max(s, 40), 14);
            double ts = Math.Min(s, 28);
            v["--bg"] = Hsl(hue, ts, 94);
            v["--surface"] = Hsl(hue, ts, 98.5);
            v["--surface-rgb"] = Join(HslToRgb(hue, ts, 98.5));
            v["--surface-1"] = Hsl(hue, ts, 95.5);
            v["--surface-2"] = Hsl(hue, ts, 93.5);
            v["--surface-3"] = Hsl(hue, ts, 91);
            v["--surface-4"] = Hsl(hue, ts, 86);
            v["--on-surface"] = Hsl(hue, 12, 11);
            v["--on-surface-var"] = Hsl(hue, 8, 32);
            v["--outline"] = Hsl(hue, 6, 48);
            v["--outline-var"] = Hsl(hue, 10, 80);
            v["--error"] = "#ba1a1a";
            v["--ov"] = "0 0 0";
            v["--scrim"] = "60 60 80";
        }
        else
        {
            bool black = mode == "black";
            v["--primary"] = Hsl(hue, Math.Max(s, 35), 80);
            v["--on-primary"] = Hsl(hue, Math.Max(s, 30), 18);
            v["--primary-container"] = Hsl(hue, Math.Min(s, 42), 28);
            v["--on-primary-container"] = Hsl(hue, Math.Max(s, 40), 90);
            double ts = Math.Min(s, 22);
            double[] L = black ? new double[] { 0, 3.5, 7, 9, 12.5, 17 } : new double[] { 5.5, 8, 11, 13, 17, 22 };
            v["--bg"] = black ? "#000" : Hsl(hue, ts, L[0]);
            v["--surface"] = Hsl(hue, ts, L[1]);
            v["--surface-rgb"] = Join(HslToRgb(hue, ts, L[1]));
            v["--surface-1"] = Hsl(hue, ts, L[2]);
            v["--surface-2"] = Hsl(hue, ts, L[3]);
            v["--surface-3"] = Hsl(hue, ts, L[4]);
            v["--surface-4"] = Hsl(hue, ts, L[5]);
            v["--on-surface"] = Hsl(hue, 12, 90);
            v["--on-surface-var"] = Hsl(hue, 10, 79);
            v["--outline"] = Hsl(hue, 6, 58);
            v["--outline-var"] = Hsl(hue, Math.Min(s, 10), 29);
            v["--error"] = "#ffb4ab";
            v["--ov"] = "255 255 255";
            v["--scrim"] = "0 0 0";
        }
        return v;
    }

    /// <summary>
    /// <c>dominantHue(d)</c>: the dominant <c>[hue, saturation]</c> of an image (RGBA bytes of a
    /// small copy, e.g. 48×48), or <c>null</c> for a grey one. Favours saturated mid-tones so a
    /// mostly dark cover still gives its colour.
    /// </summary>
    public static (double Hue, double Sat)? DominantHue(ReadOnlySpan<byte> d)
    {
        var buckets = new Dictionary<int, Bucket>();
        for (int i = 0; i < d.Length; i += 4)
        {
            var (hh, s, l) = RgbToHsl(d[i], d[i + 1], d[i + 2]);
            if (l < 12 || l > 92) continue;
            int key = Round(hh / 15);
            double w = Math.Pow(s / 100, 1.5) * (1 - Math.Abs(l - 50) / 60);
            var b = buckets.TryGetValue(key, out var existing) ? existing : new Bucket();
            b.W += w; b.H += hh * w; b.S += s * w;
            buckets[key] = b;
        }
        Bucket? best = null;
        foreach (var b in buckets.Values) if (best is null || b.W > best.Value.W) best = b;
        return best is { } bb && bb.W > 2 ? (bb.H / bb.W, bb.S / bb.W) : null;
    }

    private struct Bucket
    {
        public double W;
        public double H;
        public double S;
    }

    /// <summary>JS <c>Math.round</c>: <c>floor(x + 0.5)</c> (round half toward +infinity).</summary>
    private static int Round(double x) => (int)Math.Floor(x + 0.5);

    private static string F1(double x) => x.ToString("F1", CultureInfo.InvariantCulture);

    private static string Join((int R, int G, int B) rgb) => $"{rgb.R} {rgb.G} {rgb.B}";
}
