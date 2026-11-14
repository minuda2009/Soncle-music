// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Soncle.Services;

/// <summary>One LRCLIB record, as the engine reads it.</summary>
public sealed record LrcCandidate(
    long? Id, string? TrackName, string? Name, string? ArtistName, string? AlbumName,
    double? Duration, bool Instrumental, string? PlainLyrics, string? SyncedLyrics);

/// <summary>A version of a song, as the lyrics view lists it.</summary>
public sealed record LrcVariant(long? Id, string? Title, string? Artist, string Album, int Duration, bool Synced, double Score);

/// <summary>What <see cref="LyricsEngine.GetAsync"/> returns.</summary>
public sealed record LyricsEngineResult(
    string? Synced, string? Plain, string Source, long? Id, double Score, bool Confident, bool Chosen,
    IReadOnlyList<string> Issues, IReadOnlyList<LrcVariant> Variants);

/// <summary>
/// The lyrics engine: finds every LRCLIB version of a song, scores them against what is playing
/// (title, artist, album, length, live/remix tags, whether the timing fits the song), picks the
/// best and returns the others. A direct port of <c>src/lyrics-engine.mjs</c>.
/// </summary>
public static partial class LyricsEngine
{
    private static readonly IReadOnlyDictionary<string, string> UserAgentHeaders = new Dictionary<string, string>
    {
        ["User-Agent"] = "Soncle (https://github.com/minuda2009)",
        ["Lrclib-Client"] = "Soncle (https://github.com/minuda2009)",
    };

    private const string Api = "https://lrclib.net/api/";

    [GeneratedRegex(@"\s*[\(\[](official|lyric|audio|video|visualizer|hd|4k|mv|remaster|feat\.?|ft\.)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex CleanTitleRx();
    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex BracketsRx();
    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWordRx();

    /// <summary>Strips bracketed qualifiers like "(Official Video)"; used for comparing, never shown.</summary>
    public static string CleanTitle(string? s) => CleanTitleRx().Replace(s ?? "", "").Trim();

    /// <summary>"Café (Live) – Remastered" → "cafe remastered"; the JS <c>norm</c>.</summary>
    public static string Norm(string? s)
    {
        var t = (s ?? "").Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(t.Length);
        foreach (var c in t) if (c < '\u0300' || c > '\u036f') sb.Append(c);
        var lowered = sb.ToString().ToLowerInvariant();
        lowered = BracketsRx().Replace(lowered, " ");
        lowered = lowered.Replace("&", " and ");
        return NonWordRx().Replace(lowered, " ").Trim();
    }

    private static List<string> Toks(string? s) => Norm(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static readonly string[] FlagList = ["live", "remix", "acoustic", "instrumental", "karaoke", "demo", "radio edit", "sped up", "slowed", "cover"];

    /// <summary>What kind of version a title says it is.</summary>
    public static IReadOnlyList<string> FlagsOf(string? s)
    {
        var l = (s ?? "").ToLowerInvariant();
        return FlagList.Where(f => Regex.IsMatch(l, $@"(^|[^a-z]){Regex.Escape(f)}([^a-z]|$)")).ToList();
    }

    public static double TitleSimilarity(string? a, string? b)
    {
        var x = Toks(a); var y = Toks(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        if (string.Join(' ', x) == string.Join(' ', y)) return 1;
        var sy = new HashSet<string>(y);
        var common = x.Count(sy.Contains);
        var dice = (2.0 * common) / (x.Count + y.Count);
        var contained = (double)common / Math.Min(x.Count, y.Count);
        return Math.Max(dice, contained * 0.85);
    }

    private static List<string> ArtistsOf(LyricsTrack t) =>
        (t.Artists ?? new List<string>()).Select(Norm).Where(s => s.Length > 0).ToList();

    private static double LastLine(string? synced)
    {
        if (string.IsNullOrEmpty(synced)) return 0;
        try { var l = Lyrics.ParseLrc(synced); return l.Count > 0 ? l[^1].T : 0; } catch { return 0; }
    }

    // JS: Math.round(x) === floor(x + 0.5); C# Math.Round uses banker's rounding, so round the JS way.
    private static double JsRound(double x) => Math.Floor(x + 0.5);
    private static bool Truthy(double? d) => d.HasValue && d.Value != 0 && !double.IsNaN(d.Value);
    private static bool Truthy(string? s) => !string.IsNullOrEmpty(s);

    private static string? TitleOf(LrcCandidate c) => !string.IsNullOrEmpty(c.TrackName) ? c.TrackName : c.Name;

    /// <summary>Higher is better; about 100 is a perfect match.</summary>
    public static double ScoreCandidate(LyricsTrack t, LrcCandidate c)
    {
        double s = 0;
        s += TitleSimilarity(CleanTitle(t.Title), TitleOf(c)) * 40;
        var mine = ArtistsOf(t); var theirs = Norm(c.ArtistName);
        if (mine.Count > 0 && theirs.Length > 0)
            s += mine.Any(a => a == theirs || theirs.Contains(a) || a.Contains(theirs)) ? 25 : -10;
        if (Truthy(t.AlbumName) && Truthy(c.AlbumName))
            s += Norm(t.AlbumName) == Norm(c.AlbumName) ? 8 : 0;
        if (Truthy(t.Duration) && c.Duration.HasValue && c.Duration.Value != 0)
        {
            var d = Math.Abs(t.Duration!.Value - c.Duration.Value);
            s += d <= 1 ? 20 : d <= 3 ? 15 : d <= 6 ? 8 : d <= 12 ? 2 : d <= 20 ? -15 : -30;
        }
        var want = FlagsOf(t.Title); var got = FlagsOf(TitleOf(c));
        s -= got.Count(f => !want.Contains(f)) * 20 + want.Count(f => !got.Contains(f)) * 10;
        if (Truthy(c.SyncedLyrics))
        {
            s += 10;
            if (Truthy(t.Duration) && LastLine(c.SyncedLyrics) > t.Duration!.Value + 2) s -= 25;
        }
        else if (!Truthy(c.PlainLyrics)) s -= 30;
        if (c.Instrumental) s -= 50;
        return JsRound(s * 10) / 10;
    }

    public static List<(LrcCandidate C, double Score)> RankCandidates(LyricsTrack t, IEnumerable<LrcCandidate>? list)
    {
        var seen = new Dictionary<string, LrcCandidate>();
        var order = new List<string>();
        foreach (var c in list ?? Enumerable.Empty<LrcCandidate>())
        {
            if (c is null) continue;
            var keyed = c.Id.HasValue || !string.IsNullOrEmpty(c.TrackName);
            if (!keyed) continue;
            var key = c.Id.HasValue ? "i" + c.Id.Value : "n" + c.TrackName + c.ArtistName + c.Duration;
            if (seen.ContainsKey(key)) continue;
            seen[key] = c; order.Add(key);
        }
        var ranked = order.Select(k => (C: seen[k], Score: ScoreCandidate(t, seen[k]))).ToList();
        // stable sort, as Array.prototype.sort is: score desc, then synced desc, then |duration - song| asc
        var dur = t.Duration ?? 0;
        return ranked
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => Truthy(x.C.SyncedLyrics) ? 1 : 0)
            .ThenBy(x => Math.Abs(dur - (x.C.Duration ?? 0)))
            .ToList();
    }

    /// <summary>Sure enough to show without a hint.</summary>
    public static bool IsConfident(IReadOnlyList<(LrcCandidate C, double Score)> ranked) =>
        ranked.Count > 0 && ranked[0].Score >= 70 && (ranked.Count < 2 || ranked[0].Score - ranked[1].Score >= 8);

    public static LrcVariant VariantOf(LrcCandidate c, double score) => new(
        c.Id, c.TrackName, c.ArtistName, c.AlbumName ?? "", (int)JsRound(c.Duration ?? 0), Truthy(c.SyncedLyrics), score);

    /// <summary>Things wrong with a synced version for this song, so the app can say so.</summary>
    public static List<string> SyncIssues(string? synced, double? duration)
    {
        if (!Truthy(synced)) return new List<string>();
        var end = LastLine(synced);
        var outv = new List<string>();
        if (Truthy(duration) && end > duration!.Value + 2) outv.Add("runs-past-end");
        if (duration is > 90 && end != 0 && end < duration.Value * 0.35) outv.Add("ends-early");
        return outv;
    }

    private static async Task<JsonElement?> GetJsonAsync(string url, LyricsFetch fetch, Func<int, Task> wait)
    {
        for (var n = 0; ; n++)
        {
            try
            {
                var (status, body) = await fetch(url, UserAgentHeaders);
                if (status is >= 200 and < 300) return JsonDocument.Parse(body).RootElement.Clone();
                if (!(status == 429 || status >= 500)) return null;
            }
            catch { /* network: retry */ }
            if (n >= 2) return null;
            await wait(400 * (int)Math.Pow(3, n));
        }
    }

    private static LrcCandidate? ParseCandidate(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        long? id = e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt64() : null;
        string? Str(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        double? dur = e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;
        var inst = e.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True;
        return new LrcCandidate(id, Str("trackName"), Str("name"), Str("artistName"), Str("albumName"), dur, inst, Str("plainLyrics"), Str("syncedLyrics"));
    }

    private static string Enc(string s) => Uri.EscapeDataString(s);

    public static async Task<List<LrcCandidate>> FindCandidatesAsync(LyricsTrack t, LyricsFetch fetch, Func<int, Task> wait)
    {
        var artist = (t.Artists ?? new List<string>()).FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? "";
        var title = CleanTitle(t.Title);
        if (title.Length == 0) return new List<LrcCandidate>();
        var exact = new List<string> { "track_name=" + Enc(title), "artist_name=" + Enc(artist) };
        if (Truthy(t.AlbumName)) exact.Add("album_name=" + Enc(t.AlbumName!));
        if (Truthy(t.Duration)) exact.Add("duration=" + JsRound(t.Duration!.Value).ToString(CultureInfo.InvariantCulture));
        var byFields = "track_name=" + Enc(title) + "&artist_name=" + Enc(artist);
        var byText = "q=" + Enc($"{title} {artist}".Trim());

        var oneTask = GetJsonAsync(Api + "get?" + string.Join('&', exact), fetch, wait);
        var fieldsTask = GetJsonAsync(Api + "search?" + byFields, fetch, wait);
        var textTask = GetJsonAsync(Api + "search?" + byText, fetch, wait);
        await Task.WhenAll(oneTask, fieldsTask, textTask);

        var outv = new List<LrcCandidate>();
        var one = ParseCandidate(oneTask.Result ?? default);
        if (one is not null) outv.Add(one);
        foreach (var el in fieldsTask.Result is { ValueKind: JsonValueKind.Array } fa ? fa.EnumerateArray() : Enumerable.Empty<JsonElement>())
            if (ParseCandidate(el) is { } c) outv.Add(c);
        foreach (var el in textTask.Result is { ValueKind: JsonValueKind.Array } ta ? ta.EnumerateArray() : Enumerable.Empty<JsonElement>())
            if (ParseCandidate(el) is { } c) outv.Add(c);
        return outv;
    }

    /// <summary>
    /// Finds, ranks and picks the best version. <paramref name="preferId"/> is the version the person
    /// chose for this song. Returns null when nothing usable was found.
    /// </summary>
    public static async Task<LyricsEngineResult?> GetAsync(LyricsTrack t, LyricsFetch fetch, Func<int, Task> wait, long? preferId = null)
    {
        var ranked = RankCandidates(t, await FindCandidatesAsync(t, fetch, wait));
        (LrcCandidate C, double Score)? pick = ranked.Count > 0 ? ranked[0] : null;
        var chosen = false;
        if (preferId.HasValue)
        {
            var c = ranked.FirstOrDefault(x => x.C.Id == preferId.Value).C;
            if (c is null)
            {
                var j = await GetJsonAsync(Api + "get/" + Enc(preferId.Value.ToString(CultureInfo.InvariantCulture)), fetch, wait);
                c = ParseCandidate(j ?? default);
            }
            if (c is not null)
            {
                pick = (c, ScoreCandidate(t, c)); chosen = true;
                if (!ranked.Any(x => x.C.Id == c.Id)) ranked.Insert(0, pick.Value);
            }
        }
        if (pick is null) return null;
        var p = pick.Value;
        if (p.C.Instrumental && !Truthy(p.C.PlainLyrics) && !Truthy(p.C.SyncedLyrics)) return null;
        if (!Truthy(p.C.SyncedLyrics) && !Truthy(p.C.PlainLyrics)) return null;
        return new LyricsEngineResult(
            p.C.SyncedLyrics, p.C.PlainLyrics, "LRCLIB", p.C.Id, p.Score,
            chosen || IsConfident(ranked), chosen, SyncIssues(p.C.SyncedLyrics, t.Duration),
            ranked.Take(8).Select(x => VariantOf(x.C, x.Score)).ToList());
    }
}

/// <summary>The song being played, as the engine needs it.</summary>
public sealed class LyricsTrack
{
    public string Title { get; init; } = "";
    public IReadOnlyList<string>? Artists { get; init; }
    public string? AlbumName { get; init; }
    public double? Duration { get; init; }
}
