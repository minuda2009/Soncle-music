// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Core.Models;

namespace Soncle.Audio.Policy;

/// <summary>How to go from the playing song to the next: gapless, mix or fade.</summary>
public sealed record TransitionPlan(string Kind, double Dur = 0, double Bpm = 0);

/// <summary>Settings the transition plan reads (a slice of the library settings).</summary>
public sealed record TransitionSettings(double Crossfade, bool CrossfadeGapless = true, bool Gapless = true, string XfStyle = "smart", bool FlowXf = true);

/// <summary>
/// Pure ports of the two transition helpers in <c>renderer/app.js</c>: <c>lufsFor</c> and
/// <c>transitionPlan</c>. They take their inputs explicitly (no <c>DateTime.Now</c>, no globals),
/// so they are deterministic and unit-testable.
/// </summary>
public static class TransitionPolicy
{
    /// <summary>The song's loudness: YouTube/ReplayGain info first, else what this app measured.</summary>
    public static double? LufsFor(double? infoLufs, IReadOnlyDictionary<string, double>? measured, string id) =>
        infoLufs ?? (measured is not null && measured.TryGetValue(id, out var m) ? m : null);

    private static string? AlbumOf(Track? x)
    {
        if (x?.Album is not null) return x.Album;
        return null;
    }

    /// <summary>Album identity used to keep albums gapless.</summary>
    private static string? AlbumKey(Track x)
    {
        if (x.Album is not null) return x.Album;
        return null;
    }

    /// <summary>
    /// Pick the transition. <paramref name="feat"/> returns the analysed features of a song (or
    /// null); <paramref name="transition"/> is the Flow transition scorer.
    /// </summary>
    public static TransitionPlan? Plan(
        Track cur, Track next, TransitionSettings s,
        Func<string, FlowFeatures?> feat,
        Func<Track, FlowFeatures?, Track, FlowFeatures?, (double Score, double Sure)> transition)
    {
        var xf = s.Crossfade;
        var sameAlbum = AlbumKey(cur) is not null && AlbumKey(next) == AlbumKey(cur);
        if ((sameAlbum && s.CrossfadeGapless) || xf == 0)
            return s.Gapless ? new TransitionPlan("gapless") : null;

        var plan = new TransitionPlan("fade", xf);
        var fa = feat(cur.Id);
        var fb = feat(next.Id);
        if (fa is not null && fb is not null && s.FlowXf)
        {
            var (score, sure) = transition(cur, fa, next, fb);
            if (sure > 0.5 && score < 0.45) plan = new TransitionPlan("fade", Math.Min(xf, Math.Max(3, xf * 0.6)));
            else if (s.XfStyle != "classic" && sure > 0.5 && score >= 0.6 && (fa.Bpm ?? 0) > 60)
                plan = new TransitionPlan("mix", xf, fa.Bpm ?? 0);
        }
        return plan;
    }
}
