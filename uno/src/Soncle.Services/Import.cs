// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Soncle.Services;

public sealed record SpotifyRef(string Type, string Id);

public sealed record ImportTrack(string Title, List<string> Artists, string Album = "", double DurationMs = 0, string Uri = "");

public sealed record ImportResult(string Kind, string? Id, string Name, string Cover, string Owner, List<ImportTrack> Tracks, bool Partial = false, string? PartialReason = null);

/// <summary>A candidate from a YouTube Music search (for scoring).</summary>
public sealed record MatchCandidate(string Id, string Title, IReadOnlyList<string> Artists, double Duration, bool IsVideo = false);

public sealed record MatchResult(ImportTrack Want, MatchCandidate? Match, double Score, bool Unsure);

/// <summary>How a fetch is done (so tests replay a fake Spotify). Returns status and body text.</summary>
public delegate Task<(int Status, string Text)> ImportFetch(string url, IReadOnlyDictionary<string, string> headers);

/// <summary>
/// Import from Spotify (public playlist/album/track links) or a CSV / plain-text list, then match
/// against YouTube Music. A direct port of <c>src/spotify.mjs</c>.
/// </summary>
public static partial class Import
{
    private const string B62 = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string ToGid(string id)
    {
        var n = System.Numerics.BigInteger.Zero;
        foreach (var c in id) n = n * 62 + B62.IndexOf(c);
        // BigInteger.ToString("x") prepends a 0 when the top bit is set (two's-complement rule);
        // the JS just does n.toString(16).padStart(32, '0'), so strip and re-pad to match.
        var hex = n.ToString("x").TrimStart('0');
        return hex.PadLeft(32, '0');
    }

    [GeneratedRegex(@"open\.spotify\.com\/(?:intl-[a-z-]+\/)?(?:embed\/)?(playlist|album|track)\/([A-Za-z0-9]{22})", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRx1();
    [GeneratedRegex(@"spotify:(playlist|album|track):([A-Za-z0-9]{22})", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRx2();

    public static SpotifyRef? ParseSpotifyLink(string? text)
    {
        var s = (text ?? "").Trim();
        var m = LinkRx1().Match(s);
        if (!m.Success) m = LinkRx2().Match(s);
        return m.Success ? new SpotifyRef(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value) : null;
    }

    private const string EmbedUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

    private static async Task<JsonElement> EmbedState(string type, string id, ImportFetch fetch)
    {
        var (status, html) = await fetch($"https://open.spotify.com/embed/{type}/{id}",
            new Dictionary<string, string> { ["User-Agent"] = EmbedUa, ["Accept-Language"] = "en" });
        if (status == 404) throw new InvalidOperationException("Spotify says this link does not exist (or it is private).");
        if (status is < 200 or >= 300) throw new InvalidOperationException($"Spotify returned HTTP {status}");
        var m = NextDataRx().Match(html);
        if (!m.Success) throw new InvalidOperationException("Could not read the Spotify page (layout changed?)");
        using var doc = JsonDocument.Parse(m.Groups[1].Value);
        if (!doc.RootElement.TryGetProperty("props", out var props) || !props.TryGetProperty("pageProps", out var pp)
            || !pp.TryGetProperty("state", out var state) || !state.TryGetProperty("data", out var data)
            || !data.TryGetProperty("entity", out _))
            throw new InvalidOperationException("This Spotify item is private or unavailable.");
        return state.Clone();
    }

    [GeneratedRegex(@"<script id=""__NEXT_DATA__""[^>]*>([\s\S]*?)</script>")]
    private static partial Regex NextDataRx();

    private static ImportTrack FromEmbedTrack(JsonElement t)
    {
        var title = t.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : t.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
        var artists = new List<string>();
        if (t.TryGetProperty("artists", out var ar) && ar.GetArrayLength() > 0)
            foreach (var a in ar.EnumerateArray()) artists.Add(a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "");
        else
            artists.AddRange((t.TryGetProperty("subtitle", out var sub) ? sub.GetString() ?? "" : "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var dur = t.TryGetProperty("duration", out var d) ? d.GetDouble() : 0;
        var uri = t.TryGetProperty("uri", out var u) ? u.GetString() ?? "" : "";
        return new ImportTrack(title, artists, "", dur, uri);
    }

    /// <summary>Fetch a public Spotify playlist/album/track.</summary>
    public static async Task<ImportResult> FetchSpotifyAsync(SpotifyRef reference, ImportFetch fetch, Action<object>? onProgress = null)
    {
        var state = await EmbedState(reference.Type, reference.Id, fetch);
        var e = state.GetProperty("data").GetProperty("entity");
        var cover = "";
        if (e.TryGetProperty("coverArt", out var ca) && ca.TryGetProperty("sources", out var sources))
        {
            string? best = null; var bestW = -1;
            foreach (var s in sources.EnumerateArray())
            {
                var w = s.TryGetProperty("width", out var wv) ? wv.GetInt32() : 0;
                if (w > bestW) { bestW = w; best = s.TryGetProperty("url", out var uv) ? uv.GetString() : null; }
            }
            cover = best ?? "";
        }
        var name = e.TryGetProperty("name", out var nv) ? nv.GetString() ?? "" : e.TryGetProperty("title", out var tv) ? tv.GetString() ?? "" : "Spotify import";
        var owner = e.TryGetProperty("subtitle", out var ov) ? ov.GetString() ?? "" : "";
        var outv = new ImportResult(reference.Type, reference.Id, name, cover, owner, new List<ImportTrack>());

        if (reference.Type == "track")
        {
            outv.Tracks.Add(FromEmbedTrack(e));
            return outv;
        }
        if (e.TryGetProperty("trackList", out var tl))
            foreach (var t in tl.EnumerateArray()) outv.Tracks.Add(FromEmbedTrack(t));
        if (reference.Type == "album")
            for (var i = 0; i < outv.Tracks.Count; i++) outv.Tracks[i] = outv.Tracks[i] with { Album = outv.Name };

        // page through the rest of a long playlist with the anonymous token
        string? token = null;
        if (state.TryGetProperty("settings", out var settings) && settings.TryGetProperty("session", out var session)
            && session.TryGetProperty("accessToken", out var at)) token = at.GetString();
        if (reference.Type == "playlist" && token is not null && outv.Tracks.Count >= 100)
        {
            var headers = new Dictionary<string, string> { ["authorization"] = "Bearer " + token, ["accept"] = "application/json" };
            try
            {
                var uris = new List<string>();
                long from = outv.Tracks.Count; long total = long.MaxValue;
                while (from < total && from < 10000)
                {
                    var (status, body) = await fetch($"https://spclient.wg.spotify.com/playlist/v2/playlist/{reference.Id}?from={from}&length=100", headers);
                    if (status is < 200 or >= 300) throw new InvalidOperationException("playlist page HTTP " + status);
                    using var jd = JsonDocument.Parse(body);
                    total = jd.RootElement.TryGetProperty("length", out var ln) ? ln.GetInt64() : 0;
                    var items = jd.RootElement.TryGetProperty("contents", out var c) && c.TryGetProperty("items", out var it) ? it : default;
                    if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) break;
                    foreach (var x in items.EnumerateArray())
                    {
                        var u = x.TryGetProperty("uri", out var uv) ? uv.GetString() : null;
                        if (u is not null && u.StartsWith("spotify:track:")) uris.Add(u);
                    }
                    from += items.GetArrayLength();
                }
                var rest = new ImportTrack?[uris.Count];
                var done = 0;
                var queue = new System.Collections.Concurrent.ConcurrentQueue<(string Uri, int Idx)>();
                for (var i = 0; i < uris.Count; i++) queue.Enqueue((uris[i], i));
                async Task Worker()
                {
                    while (queue.TryDequeue(out var item))
                    {
                        try
                        {
                            var (status, body) = await fetch($"https://spclient.wg.spotify.com/metadata/4/track/{ToGid(item.Uri.Split(':')[2])}?market=from_token", headers);
                            if (status is >= 200 and < 300)
                            {
                                using var md = JsonDocument.Parse(body);
                                var arts = new List<string>();
                                if (md.RootElement.TryGetProperty("artist", out var ar)) foreach (var a in ar.EnumerateArray()) arts.Add(a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "");
                                var alb = md.RootElement.TryGetProperty("album", out var al) && al.TryGetProperty("name", out var aln) ? aln.GetString() ?? "" : "";
                                var dur = md.RootElement.TryGetProperty("duration", out var dv) ? dv.GetDouble() : 0;
                                rest[item.Idx] = new ImportTrack(md.RootElement.GetProperty("name").GetString() ?? "", arts, alb, dur, item.Uri);
                            }
                        }
                        catch { /* skip */ }
                        var d = Interlocked.Increment(ref done);
                        if (d % 10 == 0) onProgress?.Invoke(new { phase = "spotify", done = d, total = uris.Count });
                    }
                }
                await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Worker()));
                outv.Tracks.AddRange(rest.Where(x => x is not null)!);
                if (rest.Any(x => x is null)) outv = outv with { Partial = true };
            }
            catch (Exception err)
            {
                outv = outv with { Partial = true, PartialReason = err.Message };
            }
        }
        return outv;
    }

    // ---------- CSV / text ----------
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var q = false;
        text = text.TrimStart('\uFEFF');
        var useSemicolon = !text[..Math.Min(2000, text.Length)].Contains(',');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (q)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else q = false; }
                else cell.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',' || (c == ';' && useSemicolon)) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); rows.Add(row); row = new List<string>(); cell.Clear();
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows.Where(r => r.Any(x => x.Trim().Length > 0)).ToList();
    }

    public static ImportResult ParseTrackList(string text, string name = "Imported playlist")
    {
        var rows = ParseCsv(text);
        var head = (rows.Count > 0 ? rows[0] : new List<string>()).Select(x => x.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => head.FindIndex(h => names.Contains(h));
        var ti = Col("track name", "track", "title", "name", "song", "song name", "track title");
        var ai = Col("artist name(s)", "artist names", "artist name", "artist", "artists", "artist(s)");
        if (rows.Count > 1 && ti >= 0)
        {
            var al = Col("album name", "album", "album title");
            var di = Col("duration (ms)", "track duration (ms)", "duration_ms", "duration");
            var tracks = new List<ImportTrack>();
            foreach (var r in rows.Skip(1))
            {
                double d = di >= 0 && di < r.Count ? ParseNum(r[di]) : 0;
                if (di >= 0 && di < r.Count && r[di].Contains(':'))
                {
                    var p = r[di].Split(':').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    d = (p.Length == 3 ? p[0] * 3600 + p[1] * 60 + p[2] : p[0] * 60 + p[1]) * 1000;
                }
                else if (d != 0 && d < 20000) d *= 1000;
                var title = ti < r.Count ? r[ti].Trim() : "";
                var artists = ai >= 0 && ai < r.Count
                    ? r[ai].Split(',', ';').Select(x => x.Replace("\\,", ",").Trim()).Where(x => x.Length > 0).ToList()
                    : new List<string>();
                var album = al >= 0 && al < r.Count ? r[al].Trim() : "";
                if (title.Length > 0) tracks.Add(new ImportTrack(title, artists, album, d));
            }
            return new ImportResult("csv", null, name, "", "", tracks);
        }
        // plain text: one song per line, "Artist - Title" or "Title"
        var lines = Regex.Split(text, @"\r?\n").Select(l => Regex.Replace(l, @"^\s*\d+[.)]\s*", "").Trim()).Where(l => l.Length > 0);
        var list = new List<ImportTrack>();
        foreach (var l in lines)
        {
            var m = Regex.Match(l, @"^(.+?)\s+[-–—]\s+(.+)$");
            list.Add(m.Success ? new ImportTrack(m.Groups[2].Value.Trim(), new List<string> { m.Groups[1].Value.Trim() }) : new ImportTrack(l, new List<string>()));
        }
        return new ImportResult("text", null, name, "", "", list);
    }

    private static double ParseNum(string s) => double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    // ---------- matching on YouTube Music ----------
    public static string Norm(string? s)
    {
        var x = (s ?? "").Normalize(NormalizationForm.FormKD).ToLowerInvariant();
        x = Regex.Replace(x, @"\s*[\[(].*?(feat|ft\.|with|remaster|version|edit|mix|live|mono|stereo|deluxe|bonus|explicit|clean|from|prod).*?[\])]", " ");
        x = Regex.Replace(x, @"\s+-\s+(.*remaster.*|.*version.*|.*edit.*|live.*|mono|stereo)$", " ");
        x = x.Replace("&", " and ");
        x = Regex.Replace(x, @"[^\p{L}\p{N}]+", " ");
        return x.Trim();
    }

    private static HashSet<string> Tokens(string s) => Norm(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    private static double Sim(string a, string b)
    {
        var A = Tokens(a); var B = Tokens(b);
        if (A.Count == 0 || B.Count == 0) return 0;
        var inter = A.Count(x => B.Contains(x));
        return inter / (double)Math.Max(A.Count, B.Count) * 0.6 + inter / (double)Math.Min(A.Count, B.Count) * 0.4;
    }

    public static double ScoreCandidate(ImportTrack want, MatchCandidate c)
    {
        var titleS = Norm(want.Title) == Norm(c.Title) ? 1 : Sim(want.Title, c.Title);
        var wa = want.Artists.Select(Norm).Where(x => x.Length > 0).ToList();
        var ca = c.Artists.Select(Norm).Where(x => x.Length > 0).ToList();
        var artistS = wa.Count == 0 ? 0.5 : 0.0;
        foreach (var x in wa) foreach (var y in ca) if (x == y || (x.Length > 3 && (x.Contains(y) || y.Contains(x)))) artistS = 1;
        if (artistS < 1 && wa.Count > 0 && ca.Count > 0)
            artistS = wa.Max(x => ca.Max(y => Sim(x, y))) * 0.8;
        var durS = 0.5;
        if (want.DurationMs != 0 && c.Duration != 0)
        {
            var d = Math.Abs(want.DurationMs / 1000 - c.Duration);
            durS = d <= 3 ? 1 : d <= 10 ? 0.7 : d <= 30 ? 0.3 : 0;
        }
        return titleS * 0.55 + artistS * 0.3 + durS * 0.15 - (c.IsVideo ? 0.04 : 0);
    }

    /// <summary>Match wanted tracks against YouTube Music search results.</summary>
    public static async Task<List<MatchResult>> MatchAllAsync(
        IReadOnlyList<ImportTrack> tracks,
        Func<string, Task<IReadOnlyList<MatchCandidate>>> searchSongs,
        Action<object>? onProgress = null,
        int concurrency = 4,
        Func<bool>? isCancelled = null)
    {
        var results = new MatchResult?[tracks.Count];
        var queue = new System.Collections.Concurrent.ConcurrentQueue<(ImportTrack T, int I)>();
        for (var i = 0; i < tracks.Count; i++) queue.Enqueue((tracks[i], i));
        var done = 0;
        async Task Worker()
        {
            while (queue.TryDequeue(out var item) && !(isCancelled?.Invoke() ?? false))
            {
                MatchCandidate? best = null; var bestScore = 0.0;
                var q = string.Join(' ', new[] { item.T.Title, item.T.Artists.FirstOrDefault() ?? "" }.Where(x => x.Length > 0));
                foreach (var query in new[] { q, item.T.Title })
                {
                    IReadOnlyList<MatchCandidate> cands;
                    try { cands = await searchSongs(query); } catch { cands = Array.Empty<MatchCandidate>(); }
                    foreach (var c in cands.Take(8)) { var s = ScoreCandidate(item.T, c); if (s > bestScore) { best = c; bestScore = s; } }
                    if (bestScore >= 0.72 || item.T.Artists.Count == 0) break;
                }
                results[item.I] = new MatchResult(item.T, bestScore >= 0.5 ? best : null, Math.Round(bestScore * 100) / 100, bestScore >= 0.5 && bestScore < 0.7);
                var d = Interlocked.Increment(ref done);
                onProgress?.Invoke(new { phase = "match", done = d, total = tracks.Count, last = item.T.Title });
            }
        }
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => Worker()));
        return results.Where(r => r is not null).Select(r => r!).ToList();
    }
}
