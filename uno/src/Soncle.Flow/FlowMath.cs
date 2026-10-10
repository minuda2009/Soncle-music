// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.RegularExpressions;
using Soncle.Core.Models;

namespace Soncle.Flow;

/// <summary>A candidate track for Flow radio (the fields the planner uses).</summary>
public sealed record FlowTrack(string Id, string Title = "", double Duration = 0, string? AlbumName = null, string? ArtistName = null);


/// <summary>Listening context, like Spotify's time-of-day moods (see <c>contextFor</c>).</summary>
public readonly record struct FlowContext(double Hour, bool Weekend, double Energy, string Label, int? MaxBpm);

/// <summary>Personal-taste callbacks for the planner (artist affinity, liked, recent, skips).</summary>
public sealed class FlowTaste
{
    public Func<string, double>? Artist { get; init; }
    public Func<string, bool>? Liked { get; init; }
    public Func<string, double>? Recent { get; init; }
    public Func<string, int>? Skips { get; init; }
}

public sealed record FlowOptions
{
    public double Band { get; init; } = 0.12;
    public FlowContext? Context { get; init; }
    public FlowTaste? Taste { get; init; }
    public bool Lookahead { get; init; } = true;
}

/// <summary>One planned step: the chosen track and the transition score/confidence.</summary>
public sealed record FlowStep(string TrackId, double? Score, double Sure);

/// <summary>
/// Flow radio ordering, a direct port of <c>renderer/flow/flow.js</c>: order a pool so each
/// transition mixes well — tempo in a comfortable band, keys harmonically compatible (Camelot),
/// with the time-of-day energy target and personal taste. Falls back to relevance order when
/// nothing is known.
/// </summary>
public static partial class FlowMath
{
    public readonly record struct Camelot(int N, char L);

    public static Camelot? ParseCamelot(string? c)
    {
        var m = CamelotRx().Match(c ?? string.Empty);
        if (!m.Success) return null;
        return new Camelot(int.Parse(m.Groups[1].Value), m.Groups[2].Value[0]);
    }

    [GeneratedRegex(@"^(\d{1,2})([AB])$")]
    private static partial Regex CamelotRx();

    /// <summary>0..1 harmonic compatibility of two Camelot keys (null if either is unknown).</summary>
    public static double? KeyCompat(string? a, string? b)
    {
        var x = ParseCamelot(a);
        var y = ParseCamelot(b);
        if (x is null || y is null) return null;
        var d = Math.Min((x.Value.N - y.Value.N + 12) % 12, (y.Value.N - x.Value.N + 12) % 12);
        if (x.Value.L == y.Value.L) return d switch { 0 => 1, 1 => 0.9, 2 => 0.55, 7 => 0.45, _ => 0.15 };
        return d switch { 0 => 0.85, 1 => 0.5, _ => 0.1 };
    }

    /// <summary>0..1 tempo compatibility, treating half/double time as related.</summary>
    public static double? TempoCompat(double? a, double? b)
    {
        if (a is null || b is null) return null;
        var av = a.Value; var bv = b.Value;
        var best = double.MaxValue;
        foreach (var m in new[] { 1.0, 2.0, 0.5 })
            best = Math.Min(best, Math.Abs(Math.Log2(bv * m / av)));
        var pct = (Math.Pow(2, best) - 1) * 100;                 // % difference after octave folding
        var oct = Math.Abs(Math.Log2(bv / av)) > 0.5 ? 0.85 : 1;  // half/double time is fine but not perfect
        var s = pct <= 3 ? 1 : pct <= 6 ? 0.85 : pct <= 10 ? 0.6 : pct <= 16 ? 0.3 : 0.05;
        return s * oct;
    }

    private static readonly (Regex Rx, double V)[] Hints =
    {
        (new Regex(@"\b(acoustic|unplugged|piano version|stripped|lullaby|sleep)\b", RegexOptions.IgnoreCase), -0.25),
        (new Regex(@"\b(slowed|reverb)\b", RegexOptions.IgnoreCase), -0.2),
        (new Regex(@"\b(remix|club mix|extended mix|edit|bass boosted|sped up|nightcore|phonk|dnb)\b", RegexOptions.IgnoreCase), 0.2),
        (new Regex(@"\blive\b", RegexOptions.IgnoreCase), 0),
    };

    private static double HintEnergy(FlowTrack t)
    {
        var e = 0.0;
        foreach (var (rx, v) in Hints) if (rx.IsMatch(t.Title)) e += v;
        return e;
    }

    /// <summary>Estimated energy 0..1 with a confidence.</summary>
    public static (double E, double C) EnergyOf(FlowTrack t, FlowFeatures? f)
    {
        if (f?.Energy is not null) return (f.Energy.Value, 0.8);
        var e = 0.5; var c = 0.15;
        var h = HintEnergy(t);
        if (h != 0) { e += h; c = Math.Max(c, 0.35); }
        return (Math.Max(0, Math.Min(1, e)), c);
    }

    /// <summary>Transition score a→b in 0..1 plus how sure we are (0..1).</summary>
    public static (double Score, double Sure) Transition(FlowTrack a, FlowFeatures? fa, FlowTrack b, FlowFeatures? fb)
    {
        var parts = new List<(double V, double W)>();
        var tc = TempoCompat(fa?.Bpm, fb?.Bpm);
        if (tc is not null) parts.Add((tc.Value, 0.4 * Math.Min(fa!.BpmConf, fb!.BpmConf) + 0.05));
        var kc = KeyCompat(fa?.Camelot, fb?.Camelot);
        if (kc is not null) parts.Add((kc.Value, 0.35 * Math.Min(fa!.KeyConf, fb!.KeyConf) + 0.05));
        var ea = EnergyOf(a, fa); var eb = EnergyOf(b, fb);
        parts.Add((1 - Math.Min(1, Math.Abs(ea.E - eb.E) * 1.6), 0.25 * Math.Min(ea.C, eb.C)));
        if (a.Duration > 0 && b.Duration > 0)
            parts.Add((1 - Math.Min(1, Math.Abs(Math.Log2(b.Duration / a.Duration))), 0.03));
        double w = 0, s = 0;
        foreach (var (v, wt) in parts) { s += v * wt; w += wt; }
        return (w != 0 ? s / w : 0.5, Math.Min(1, w / 0.8));
    }

    /// <summary>Listening context for a given time (no DateTime.Now inside, so tests are deterministic).</summary>
    public static FlowContext ContextFor(DateTime date)
    {
        var h = date.Hour + date.Minute / 60.0;
        var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var energy = 0.5 + 0.22 * Math.Sin((h - 9) / 24 * 2 * Math.PI) + (weekend && h > 11 ? 0.05 : 0);
        var label = h < 5 ? "late night" : h < 9 ? "morning" : h < 12 ? "late morning" : h < 17 ? "afternoon" : h < 21 ? "evening" : "night";
        int? maxBpm = h >= 23 || h < 6 ? 115 : null;
        return new FlowContext(h, weekend, Math.Max(0.2, Math.Min(0.85, energy)), label, maxBpm);
    }

    /// <summary>Order a pool after <paramref name="seed"/> for the best flow.</summary>
    public static List<FlowStep> PlanFlow(FlowTrack seed, IReadOnlyList<FlowTrack> pool, Func<string, FlowFeatures?> feat, FlowOptions? o = null)
    {
        o ??= new FlowOptions();
        var band = o.Band;
        var ctx = o.Context;
        var taste = o.Taste;
        var seedF = feat(seed.Id);
        var rel = new Dictionary<string, double>();
        for (var i = 0; i < pool.Count; i++) rel[pool[i].Id] = 1 - i / (double)Math.Max(1, pool.Count);
        var left = pool.Where(t => t.Id != seed.Id).ToList();
        var result = new List<FlowStep>();
        var prev = seed; var prevF = seedF;
        var recentArtists = new List<string>();
        string ArtistOf(FlowTrack t) => (t.ArtistName ?? string.Empty).ToLowerInvariant();
        bool Familiar(FlowTrack t) => taste is not null && ((taste.Liked?.Invoke(t.Id) ?? false) || (taste.Artist?.Invoke(ArtistOf(t)) ?? 0) > 0.35);
        var famRun = 0; var newRun = 0;

        (double S, (double Score, double Sure) Tr) StepScore(FlowTrack p, FlowFeatures? pf, FlowTrack c, int step)
        {
            var cf = feat(c.Id);
            var tr = Transition(p, pf, c, cf);
            var s = tr.Score * (0.45 + 0.4 * tr.Sure) + (rel.TryGetValue(c.Id, out var r) ? r : 0.5) * (0.35 - 0.2 * tr.Sure);
            if (seedF?.Bpm is not null && cf?.Bpm is not null)
            {
                var tcs = TempoCompat(seedF.Bpm, cf.Bpm);
                if (tcs is not null && tcs < 0.6) s -= (0.6 - tcs.Value) * band * 2.5;
            }
            if (ctx is not null)
            {
                var e = EnergyOf(c, cf);
                var pull = Math.Min(1, 0.4 + step / 10.0) * e.C;
                s -= Math.Abs(e.E - ctx.Value.Energy) * 0.3 * pull;
                if (ctx.Value.MaxBpm is not null && cf?.Bpm is not null && cf.BpmConf > 0.4 && cf.Bpm > ctx.Value.MaxBpm && !(cf.Bpm / 2 > 60)) s -= 0.06;
            }
            if (taste is not null)
            {
                var a = taste.Artist?.Invoke(ArtistOf(c)) ?? 0;
                s += a * 0.1 + (taste.Liked?.Invoke(c.Id) ?? false ? 0.06 : 0);
                var sk = taste.Skips?.Invoke(c.Id) ?? 0;
                if (sk != 0) s -= Math.Min(0.3, sk * 0.1);
                var rec = taste.Recent?.Invoke(c.Id) ?? 0;
                s -= rec * 0.25;
                var f = Familiar(c);
                if (f && famRun >= 2) s -= 0.08;
                if (!f && newRun >= 3 && a == 0) s -= 0.05;
            }
            var ar = ArtistOf(c);
            if (ar.Length > 0 && recentArtists.Contains(ar)) s -= 0.12;
            if (c.AlbumName is not null && p.AlbumName is not null && c.AlbumName == p.AlbumName) s -= 0.04;
            return (s, tr);
        }

        var step = 0;
        while (left.Count > 0)
        {
            var bi = 0; var bs = double.NegativeInfinity;
            (double Score, double Sure) btr = default;
            for (var i = 0; i < left.Count; i++)
            {
                var (s, tr) = StepScore(prev, prevF, left[i], step);
                var look = 0.0;
                if (o.Lookahead && left.Count > 2 && i < 40)
                {
                    var bestNext = 0.0;
                    for (var j = 0; j < Math.Min(left.Count, 25); j++)
                        if (j != i) bestNext = Math.Max(bestNext, Transition(left[i], feat(left[i].Id), left[j], feat(left[j].Id)).Score);
                    look = bestNext * 0.15;
                }
                if (s + look > bs) { bs = s + look; bi = i; btr = tr; }
            }
            var pick = left[bi];
            left.RemoveAt(bi);
            result.Add(new FlowStep(pick.Id, Math.Round(btr.Score * 100) / 100, Math.Round(btr.Sure * 100) / 100));
            recentArtists.Add(ArtistOf(pick)); if (recentArtists.Count > 3) recentArtists.RemoveAt(0);
            if (Familiar(pick)) { famRun++; newRun = 0; } else { newRun++; famRun = 0; }
            prev = pick; prevF = feat(pick.Id); step++;
        }
        return result;
    }

    /// <summary>Human-friendly label for chips: "124 BPM · 8A".</summary>
    public static string FeatLabel(FlowFeatures? f)
    {
        if (f is null) return string.Empty;
        var parts = new List<string>();
        if (f.Bpm is not null) parts.Add(Math.Round(f.Bpm.Value) + " BPM");
        if (!string.IsNullOrEmpty(f.Camelot) && f.KeyConf >= 0.15) parts.Add(f.Camelot!);
        return string.Join(" · ", parts);
    }
}
