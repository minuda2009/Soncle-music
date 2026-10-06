// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Soncle.Services;

public sealed record LrcWord(double T, string Text, double? End);
public sealed record LrcLine(double T, string Text, double End, IReadOnlyList<LrcWord>? Words);

public sealed record LyricsFetchResult(string? Synced, string? Plain, string? Source);

/// <summary>How a lyrics request is made (so tests replay a fake LRCLIB). Returns status and body.</summary>
public delegate Task<(int Status, string Text)> LyricsFetch(string url, IReadOnlyDictionary<string, string> headers);

/// <summary>
/// Lyrics: LRCLIB (get then search with the ±5 s duration rule), then the YouTube Music fallback.
/// A port of the lyrics code in <c>src/main.mjs</c> (and <c>mobile/src/backend.js</c>), plus the
/// LRC parser from <c>renderer/app.js</c>.
/// </summary>
public static partial class Lyrics
{
    private const string UserAgent = "Soncle (https://github.com/minuda2009)";

    [GeneratedRegex(@"\s*[\(\[](official|lyric|audio|video|visualizer|hd|4k|mv|remaster|feat\.?|ft\.)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex CleanTitleRx();

    public static string CleanTitle(string? s) => CleanTitleRx().Replace(s ?? "", "").Trim();

    /// <summary>LRCLIB lookup: get, then search with the duration-match rule.</summary>
    public static async Task<LyricsFetchResult?> LrclibAsync(string title, string? album, IReadOnlyList<string> artists, double? duration, LyricsFetch fetch)
    {
        var artist = artists.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
        var cleanTitle = CleanTitle(title);
        var headers = new Dictionary<string, string> { ["User-Agent"] = UserAgent };
        var q = new List<string> { "track_name=" + Uri.EscapeDataString(cleanTitle), "artist_name=" + Uri.EscapeDataString(artist) };
        if (!string.IsNullOrEmpty(album)) q.Add("album_name=" + Uri.EscapeDataString(album!));
        if (duration is not null) q.Add("duration=" + Math.Round(duration.Value).ToString(CultureInfo.InvariantCulture));
        try
        {
            var (status, body) = await fetch("https://lrclib.net/api/get?" + string.Join('&', q), headers);
            if (status is >= 200 and < 300)
            {
                using var doc = JsonDocument.Parse(body);
                var synced = doc.RootElement.TryGetProperty("syncedLyrics", out var s) ? s.GetString() : null;
                var plain = doc.RootElement.TryGetProperty("plainLyrics", out var p) ? p.GetString() : null;
                if (!string.IsNullOrEmpty(synced) || !string.IsNullOrEmpty(plain)) return new LyricsFetchResult(synced, plain, "LRCLIB");
            }
        }
        catch { /* fall through to search */ }
        try
        {
            var (status, body) = await fetch("https://lrclib.net/api/search?" + $"track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(artist)}", headers);
            if (status is >= 200 and < 300)
            {
                using var doc = JsonDocument.Parse(body);
                var arr = doc.RootElement.EnumerateArray().ToList();
                JsonElement? best = null;
                foreach (var x in arr)
                {
                    if (HasSynced(x) && (duration is null || (x.TryGetProperty("duration", out var d) && Math.Abs(d.GetDouble() - duration.Value) < 5))) { best = x; break; }
                }
                best ??= arr.FirstOrDefault(HasSynced);
                if (best is null && arr.Count > 0) best = arr[0];
                if (best is not null)
                {
                    var b = best.Value;
                    return new LyricsFetchResult(
                        b.TryGetProperty("syncedLyrics", out var s) ? s.GetString() : null,
                        b.TryGetProperty("plainLyrics", out var p) ? p.GetString() : null,
                        "LRCLIB");
                }
            }
        }
        catch { /* give up */ }
        return null;
    }

    private static bool HasSynced(JsonElement x) =>
        x.TryGetProperty("syncedLyrics", out var s) && !string.IsNullOrEmpty(s.GetString());

    /// <summary>LRCLIB then a YouTube Music fallback (the caller supplies the YTM lookup).</summary>
    public static async Task<LyricsFetchResult?> GetAsync(
        string title, string? album, IReadOnlyList<string> artists, double? duration,
        LyricsFetch fetch, Func<Task<(string? Plain, string? Source)?>>? ytFallback = null)
    {
        var a = await LrclibAsync(title, album, artists, duration, fetch);
        if (!string.IsNullOrEmpty(a?.Synced)) return a;
        if (ytFallback is not null)
        {
            try
            {
                var b = await ytFallback();
                if (b is not null && !string.IsNullOrEmpty(b.Value.Plain))
                    return new LyricsFetchResult(null, b.Value.Plain, Regex.Replace(b.Value.Source ?? "YouTube Music", "^Source:\\s*", "", RegexOptions.IgnoreCase));
            }
            catch { /* fall through */ }
        }
        return a;
    }

    // ---------- LRC parsing ----------
    [GeneratedRegex(@"\[(\d+):(\d+(?:\.\d+)?)\]")]
    private static partial Regex LrcTagRx();
    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex LrcStripRx();
    [GeneratedRegex(@"<\d+:\d+(?:\.\d+)?>")]
    private static partial Regex LrcWordTagRx();
    [GeneratedRegex(@"<(\d+):(\d+(?:\.\d+)?)>([^<]*)")]
    private static partial Regex LrcWordRx();

    /// <summary>Parse an LRC string (including enhanced word timings) into sorted lines with ends.</summary>
    public static List<LrcLine> ParseLrc(string lrc)
    {
        double Ts(string m, string s) => double.Parse(m, CultureInfo.InvariantCulture) * 60 + double.Parse(s, CultureInfo.InvariantCulture);
        var outv = new List<LrcLine>();
        foreach (var line in Regex.Split(lrc, @"\r?\n"))
        {
            var tags = LrcTagRx().Matches(line);
            if (tags.Count == 0) continue;
            var body = LrcStripRx().Replace(line, "");
            List<LrcWord>? words = null;
            if (LrcWordTagRx().IsMatch(body))
            {
                words = new List<LrcWord>();
                foreach (Match m in LrcWordRx().Matches(body))
                {
                    var w = m.Groups[3].Value;
                    if (w.Trim().Length > 0) words.Add(new LrcWord(Ts(m.Groups[1].Value, m.Groups[2].Value), w, null));
                    else if (words.Count > 0) words[^1] = words[^1] with { End = Ts(m.Groups[1].Value, m.Groups[2].Value) };
                }
                if (words.Count == 0) words = null;
            }
            var text = Regex.Replace(LrcWordTagRx().Replace(body, ""), @"\s+", " ").Trim();
            foreach (Match m in tags) outv.Add(new LrcLine(Ts(m.Groups[1].Value, m.Groups[2].Value), text, 0, words));
        }
        outv.Sort((a, b) => a.T.CompareTo(b.T));
        for (var i = 0; i < outv.Count; i++)
        {
            var end = i + 1 < outv.Count ? outv[i + 1].T : outv[i].T + 6;
            outv[i] = outv[i] with { End = end };
        }
        return outv;
    }
}
