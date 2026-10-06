// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.RegularExpressions;
using Soncle.Core.Models;

namespace Soncle.YouTube.Parsing;

/// <summary>One parsed item (a song, album, artist, playlist, mood or browse row).</summary>
public sealed class YtItem
{
    public string Type { get; init; } = "";
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Thumb { get; init; }
    public List<ArtistRef>? Artists { get; init; }
    public AlbumRef? Album { get; init; }
    public double Duration { get; init; }
    public string? Year { get; init; }
    public bool Explicit { get; init; }
    public bool IsVideo { get; init; }
    public string? Params { get; init; }
    public string? Color { get; init; }
    public string? Plays { get; init; }

    /// <summary>
    /// Exactly which JSON keys the JS object literal for this branch contains (an undefined JS
    /// value is absent, a null one is present). The golden test emits only these.
    /// </summary>
    public HashSet<string> Fields { get; init; } = new();
}

/// <summary>One parsed shelf (carousel, list, grid, description).</summary>
public sealed class YtShelf
{
    public string? Title { get; init; }
    public string? Strapline { get; init; }
    public string? Thumb { get; init; }
    public string Layout { get; init; } = "list";
    public List<YtItem> Items { get; init; } = new();
    public Dictionary<string, string>? More { get; init; }
    /// <summary>Whether the JS shelf literal carries a <c>more</c> key at all (carousel and list do).</summary>
    public bool MoreField { get; init; }
    public string? Text { get; init; }
}

/// <summary>
/// Walks raw InnerTube renderers and normalises them exactly as <c>src/yt.mjs</c>'s
/// <c>normItem</c>/<c>normShelf</c> do. In C# there is no youtubei.js, so this reads the raw
/// renderers itself; the golden cases in <c>fixtures/yt/expected/cases.json</c> prove it matches.
/// </summary>
public static partial class Normalize
{
    /// <summary>fixThumb: a //-less URL gets https, googleusercontent/ggpht sizes are normalised.</summary>
    public static string FixThumb(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.StartsWith("//")) url = "https:" + url;
        if (Regex.IsMatch(url, "googleusercontent\\.com|ggpht\\.com"))
        {
            url = Regex.Replace(url, "=w\\d+-h\\d+.*$", "=w544-h544-l90-rj");
            url = Regex.Replace(url, "=s\\d+.*$", "=s544");
        }
        return url;
    }

    /// <summary>parseDuration: "3:45" → 225.</summary>
    public static double ParseDuration(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var parts = s.Split(':');
        var vals = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], out vals[i])) return 0;
        double total = 0;
        foreach (var v in vals) total = total * 60 + v;
        return total;
    }

    private static string Txt(JsonElement? t)
    {
        if (t is null || t.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "";
        var e = t.Value;
        if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? "";
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var r in runs.EnumerateArray())
                    if (r.TryGetProperty("text", out var tx)) sb.Append(tx.GetString());
                return sb.ToString();
            }
            if (e.TryGetProperty("simpleText", out var st)) return st.GetString() ?? "";
            if (e.TryGetProperty("text", out var t2)) return t2.ValueKind == JsonValueKind.String ? t2.GetString() ?? "" : Txt(t2);
        }
        return "";
    }

    private static JsonElement? Prop(JsonElement? node, string name) =>
        node is not null && node.Value.ValueKind == JsonValueKind.Object && node.Value.TryGetProperty(name, out var v) ? v : null;

    private static string PickThumb(JsonElement? list)
    {
        if (list is null) return "";
        var e = list.Value;
        if (e.ValueKind == JsonValueKind.Object)
        {
            // raw renderers wrap the list in musicThumbnailRenderer.thumbnail.thumbnails
            if (e.TryGetProperty("thumbnailRenderer", out var tr)) return PickThumb(tr);
            if (e.TryGetProperty("musicThumbnailRenderer", out var mtr)) return PickThumb(mtr);
            if (e.TryGetProperty("thumbnail", out var th)) return PickThumb(th);
            if (e.TryGetProperty("contents", out var c)) e = c;
            else if (e.TryGetProperty("thumbnails", out var th2)) e = th2;
            else return "";
        }
        if (e.ValueKind != JsonValueKind.Array) return "";
        string bestUrl = ""; var bestW = -1;
        foreach (var x in e.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.Object || !x.TryGetProperty("url", out var u)) continue;
            var w = x.TryGetProperty("width", out var wv) && wv.ValueKind == JsonValueKind.Number ? wv.GetInt32() : 0;
            if (w > bestW) { bestW = w; bestUrl = u.GetString() ?? ""; }
        }
        return FixThumb(bestUrl);
    }

    /// <summary>Unwrap a shelf header (musicCarouselShelfBasicHeaderRenderer) and any renderer wrapper.</summary>
    private static JsonElement? Unwrap(JsonElement? node, params string[] wrappers)
    {
        if (node is null) return null;
        var e = node.Value;
        foreach (var w in wrappers)
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(w, out var inner)) return inner;
        return e;
    }

    private static string PageType(JsonElement? ep)
    {
        var cfg = Prop(Prop(Prop(Prop(ep, "payload"), "browseEndpointContextSupportedConfigs"), "browseEndpointContextMusicConfig"), "pageType");
        if (cfg is not null) return cfg.Value.GetString() ?? "";
        // raw shape: navigationEndpoint.browseEndpoint.browseEndpointContextSupportedConfigs...
        var be = Prop(ep, "browseEndpoint");
        cfg = Prop(Prop(Prop(be, "browseEndpointContextSupportedConfigs"), "browseEndpointContextMusicConfig"), "pageType");
        return cfg is null ? "" : cfg.Value.GetString() ?? "";
    }

    /// <summary>
    /// The raw renderers nest ids under watchEndpoint/browseEndpoint (youtubei.js flattens them into
    /// <c>payload</c>). This resolves both shapes into (videoId, browseId, playlistId, params).
    /// </summary>
    private static (string? VideoId, string? BrowseId, string? PlaylistId, string? Params) Endpoint(JsonElement? ep)
    {
        var p = Prop(ep, "payload");
        if (p is not null)
            return (Str(Prop(p, "videoId")), Str(Prop(p, "browseId")), Str(Prop(p, "playlistId")), Str(Prop(p, "params")));
        foreach (var key in new[] { "watchEndpoint", "watchPlaylistEndpoint", "browseEndpoint", "playlistEndpoint" })
        {
            var e = Prop(ep, key);
            if (e is null) continue;
            return (Str(Prop(e, "videoId")), Str(Prop(e, "browseId")), Str(Prop(e, "playlistId")), Str(Prop(e, "params")));
        }
        return (null, null, null, null);
    }

    private static JsonElement? RunEndpoint(JsonElement r) => Prop(r, "endpoint") ?? Prop(r, "navigationEndpoint");

    private static List<ArtistRef> ArtistsFromRuns(JsonElement? runs)
    {
        var outv = new List<ArtistRef>();
        if (runs is null || runs.Value.ValueKind != JsonValueKind.Array) return outv;
        foreach (var r in runs.Value.EnumerateArray())
        {
            var ep = RunEndpoint(r);
            var bid = Endpoint(ep).BrowseId ?? "";
            var pt = PageType(ep);
            if (bid.StartsWith("UC") || pt == "MUSIC_PAGE_TYPE_ARTIST" || pt == "MUSIC_PAGE_TYPE_USER_CHANNEL")
                outv.Add(new ArtistRef { Name = Txt(r), Id = bid.Length > 0 ? bid : null });
        }
        return outv;
    }

    private static bool IsExplicit(JsonElement node)
    {
        foreach (var key in new[] { "badges", "subtitle_badges" })
        {
            if (!node.TryGetProperty(key, out var badges) || badges.ValueKind != JsonValueKind.Array) continue;
            foreach (var b in badges.EnumerateArray())
            {
                var it = b.TryGetProperty("icon_type", out var itv) ? itv.GetString() : null;
                var label = b.TryGetProperty("label", out var lv) ? lv.GetString() : null;
                if (it == "MUSIC_EXPLICIT_BADGE" || (label is not null && Regex.IsMatch(label, "explicit", RegexOptions.IgnoreCase))) return true;
            }
        }
        return false;
    }

    private static string KindFromPage(string pt) => pt switch
    {
        "MUSIC_PAGE_TYPE_ALBUM" or "MUSIC_PAGE_TYPE_AUDIOBOOK" => "album",
        "MUSIC_PAGE_TYPE_PLAYLIST" => "playlist",
        "MUSIC_PAGE_TYPE_ARTIST" or "MUSIC_PAGE_TYPE_USER_CHANNEL" or "MUSIC_PAGE_TYPE_LIBRARY_ARTIST" => "artist",
        "MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE" => "playlist",
        _ => "",
    };

    /// <summary>normItem: dispatch on the renderer kind.</summary>
    public static YtItem? NormItem(string renderer, JsonElement raw)
    {
        try
        {
            return renderer switch
            {
                "musicTwoRowItemRenderer" => NormTwoRow(raw),
                "musicResponsiveListItemRenderer" => NormResponsive(raw),
                "musicMultiRowListItemRenderer" => NormMultiRow(raw),
                "musicNavigationButtonRenderer" => NormNavButton(raw),
                "playlistPanelVideoRenderer" => NormPanel(raw),
                _ => null,
            };
        }
        catch { return null; }
    }

    private static YtItem? NormTwoRow(JsonElement n)
    {
        var ep = Prop(n, "navigationEndpoint") ?? Prop(n, "endpoint");
        var (videoId, browseId, playlistId, eparams) = Endpoint(ep);
        var title = Txt(Prop(n, "title"));
        var subtitle = Txt(Prop(n, "subtitle"));
        var thumb = PickThumb(Prop(n, "thumbnailRenderer") ?? Prop(n, "thumbnail"));
        var pk = KindFromPage(PageType(ep));
        if (pk.Length > 0)
        {
            var fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb", "artists", "explicit" };
            if (Str(Prop(n, "year")) is not null) fields.Add("year");
            return new YtItem
            {
                Type = pk,
                Id = browseId ?? playlistId,
                Title = title, Subtitle = subtitle, Thumb = thumb,
                Artists = ArtistsFromRuns(Prop(Prop(n, "subtitle"), "runs")),
                Year = Str(Prop(n, "year")), Explicit = IsExplicit(n), Fields = fields,
            };
        }
        if (videoId is not null)
        {
            var artists = ArtistsFromRuns(Prop(Prop(n, "subtitle"), "runs"));
            var duration = DurationSeconds(Prop(n, "duration"));
            if (duration == 0)
            {
                var fixedTxt = Txt(FirstFixedColumn(n));
                if (fixedTxt.Length == 0)
                {
                    var m = Regex.Match(subtitle, @"(\d+:)?\d+:\d\d");
                    fixedTxt = m.Success ? m.Value : "";
                }
                duration = ParseDuration(fixedTxt);
            }
            if (artists.Count == 0)
            {
                var pick = subtitle.Split(" • ").FirstOrDefault(s => !Regex.IsMatch(s, "^(Song|Video|Episode)$", RegexOptions.IgnoreCase));
                artists.Add(new ArtistRef { Name = pick ?? subtitle });
            }
            var itemType = Str(Prop(n, "item_type"));
            return new YtItem
            {
                Type = "song", Id = videoId, Title = title, Subtitle = subtitle, Thumb = thumb, Artists = artists,
                // youtubei.js gives a MusicTwoRowItem the item_type "video" (it is a video card)
                Duration = duration, Explicit = IsExplicit(n), IsVideo = itemType != "song",
                Fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb", "artists", "duration", "explicit", "isVideo" },
            };
        }
        var bfields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb" };
        if (eparams is not null) bfields.Add("params");
        if (playlistId is not null) return new YtItem { Type = "radio", Id = playlistId, Params = eparams, Title = title, Subtitle = subtitle, Thumb = thumb, Fields = bfields };
        if (browseId is not null) return new YtItem { Type = "browse", Id = browseId, Params = eparams, Title = title, Subtitle = subtitle, Thumb = thumb, Fields = bfields };
        return null;
    }

    private static JsonElement? FirstFixedColumn(JsonElement n)
    {
        var cols = Prop(n, "fixedColumns") ?? Prop(n, "fixed_columns");
        if (cols is null || cols.Value.ValueKind != JsonValueKind.Array) return null;
        foreach (var c in cols.Value.EnumerateArray()) return Prop(c, "title");
        return null;
    }

    private static double DurationSeconds(JsonElement? d) =>
        d is not null && d.Value.ValueKind == JsonValueKind.Object && d.Value.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0;

    private static YtItem? NormResponsive(JsonElement n)
    {
        var cols = Prop(n, "flexColumns") ?? Prop(n, "flex_columns");
        JsonElement? Col(int i)
        {
            if (cols is null || cols.Value.ValueKind != JsonValueKind.Array) return null;
            var arr = cols.Value.EnumerateArray().ToList();
            if (i >= arr.Count) return null;
            // raw: musicResponsiveListItemFlexColumnRenderer.text ; youtubei.js flattens it to title
            return Prop(arr[i], "title") ?? Prop(Prop(arr[i], "musicResponsiveListItemFlexColumnRenderer"), "text");
        }
        var title = Txt(Prop(n, "title"));
        if (title.Length == 0) title = Txt(Col(0));
        var thumb = PickThumb(Prop(Prop(n, "thumbnail"), "contents") ?? Prop(n, "thumbnail"));
        var parts = new List<string>();
        for (var i = 1; i <= 3; i++)
        {
            var c = Col(i);
            if (c is null) continue;
            var t = c.Value.ValueKind == JsonValueKind.Object && !c.Value.EnumerateObject().Any() ? "N/A" : Txt(c);
            if (t.Length > 0) parts.Add(t);
        }
        var subtitle = string.Join(" • ", parts);
        var ep = Prop(n, "navigationEndpoint") ?? Prop(n, "endpoint");
        var pk = KindFromPage(PageType(ep));
        var type = ResponsiveItemType(n, cols);

        if (type is "artist" or "library_artist" || pk == "artist")
        {
            var sub = subtitle;
            var subs = Prop(n, "subscribers");
            if (subs is not null)
            {
                var s = subs.Value.ValueKind == JsonValueKind.String ? subs.Value.GetString() : subs.Value.ToString();
                sub = Regex.IsMatch(s ?? "", "subscriber", RegexOptions.IgnoreCase) ? s! : $"{s} subscribers";
            }
            return new YtItem { Type = "artist", Id = Str(Prop(n, "id")) ?? Endpoint(ep).BrowseId, Title = title, Subtitle = sub, Thumb = thumb, Fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb" } };
        }
        if (type is "album" || pk == "album")
            return new YtItem { Type = "album", Id = Str(Prop(n, "id")) ?? Endpoint(ep).BrowseId, Title = title, Subtitle = subtitle, Thumb = thumb, Year = Str(Prop(n, "year")), Artists = ArtistsOf(n), Explicit = IsExplicit(n), Fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb", "year", "artists", "explicit" } };
        if (type is "playlist" or "podcast_show" || pk == "playlist")
            return new YtItem { Type = "playlist", Id = Str(Prop(n, "id")) ?? Endpoint(ep).BrowseId, Title = title, Subtitle = subtitle, Thumb = thumb, Fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb" } };
        // songs / videos / episodes — mirror youtubei.js: #parseSong/#parseVideo take the id from
        // playlistItemData; the others fall back to the overlay/endpoint, then a flex-column run.
        var overlayVideoId = Prop(Prop(Prop(Prop(n, "overlay"), "content"), "endpoint"), "payload") is { } ovp ? Str(Prop(ovp, "videoId")) : null;
        string? id = type is "song" or "video" or "non_music_track" ? Str(Prop(Prop(n, "playlistItemData"), "videoId")) : null;
        id ??= overlayVideoId;
        id ??= Endpoint(ep).VideoId;
        if (id is null && cols is not null && cols.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cols.Value.EnumerateArray())
            {
                var runs = Prop(Prop(c, "title"), "runs");
                if (runs is null || runs.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var r in runs.Value.EnumerateArray())
                {
                    var vid = Str(Prop(Prop(RunEndpoint(r), "payload"), "videoId"));
                    if (vid is not null) { id = vid; break; }
                }
            }
        }
        if (id is null) return null;

        var artists = ArtistsOf(n);
        var secondRuns = Prop(Col(1), "runs");
        if (artists.Count == 0) artists = ArtistsFromRuns(secondRuns);
        if (artists.Count == 0 && secondRuns is not null && secondRuns.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in secondRuns.Value.EnumerateArray())
            {
                var t = Txt(r);
                if (!Regex.IsMatch(t, @"^(Song|Video|Episode|\s*•\s*)$")) { artists.Add(new ArtistRef { Name = t }); break; }
            }
        }
        AlbumRef? album = null;
        var alb = Prop(n, "album");
        if (alb is not null) album = new AlbumRef { Name = Txt(Prop(alb, "name")), Id = Str(Prop(alb, "id")) ?? "" };
        if (album is null && cols is not null && cols.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cols.Value.EnumerateArray())
            {
                var runs = Prop(Prop(c, "title"), "runs");
                if (runs is null || runs.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var r in runs.Value.EnumerateArray())
                    if (PageType(RunEndpoint(r)) == "MUSIC_PAGE_TYPE_ALBUM")
                        album = new AlbumRef { Name = Txt(r), Id = Str(Prop(Prop(RunEndpoint(r), "payload"), "browseId")) ?? "" };
            }
        }
        var duration = DurationSeconds(Prop(n, "duration"));
        if (duration == 0)
        {
            duration = ParseDuration(Txt(FirstFixedColumn(n)));
            if (duration == 0)
            {
                var m = Regex.Match(subtitle, @"(\d+:)?\d+:\d\d");
                if (m.Success) duration = ParseDuration(m.Value);
            }
        }
        return new YtItem
        {
            Type = "song", Id = id, Title = title, Thumb = thumb, Artists = artists, Album = album,
            Duration = duration, Subtitle = subtitle, Explicit = IsExplicit(n),
            IsVideo = type is "video" or "non_music_track", Plays = ResponsiveViews(cols) ?? "",
            Fields = new HashSet<string> { "type", "id", "title", "thumb", "artists", "album", "duration", "subtitle", "explicit", "isVideo", "plays" },
        };
    }

    /// <summary>
    /// The item type youtubei.js assigns a MusicResponsiveListItem: the pageType (from the
    /// navigation endpoint, or any flex column run) decides album/playlist/artist/non-music-track/
    /// podcast; otherwise the first flex column's watchEndpoint musicVideoType decides video vs song.
    /// </summary>
    private static string? ResponsiveItemType(JsonElement n, JsonElement? cols)
    {
        var pt = PageType(Prop(n, "navigationEndpoint") ?? Prop(n, "endpoint"));
        if (pt.Length == 0 && cols is not null && cols.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var col in cols.Value.EnumerateArray())
            {
                var runs = Prop(Prop(Prop(col, "musicResponsiveListItemFlexColumnRenderer"), "text"), "runs")
                    ?? Prop(Prop(col, "title"), "runs");
                if (runs is null || runs.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var run in runs.Value.EnumerateArray())
                    if (PageType(RunEndpoint(run)) == "MUSIC_PAGE_TYPE_NON_MUSIC_AUDIO_TRACK_PAGE")
                    { pt = "MUSIC_PAGE_TYPE_NON_MUSIC_AUDIO_TRACK_PAGE"; break; }
                if (pt.Length > 0) break;
            }
        }
        switch (pt)
        {
            case "MUSIC_PAGE_TYPE_ALBUM": return "album";
            case "MUSIC_PAGE_TYPE_PLAYLIST": return "playlist";
            case "MUSIC_PAGE_TYPE_ARTIST" or "MUSIC_PAGE_TYPE_USER_CHANNEL": return "artist";
            case "MUSIC_PAGE_TYPE_LIBRARY_ARTIST": return "library_artist";
            case "MUSIC_PAGE_TYPE_NON_MUSIC_AUDIO_TRACK_PAGE": return "non_music_track";
            case "MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE": return "podcast_show";
        }
        if (cols is null || cols.Value.ValueKind != JsonValueKind.Array) return null;
        if (cols.Value.GetArrayLength() < 2) return null;   // no second column → endpoint/unknown
        var first = cols.Value.EnumerateArray().First();
        var runs0 = Prop(Prop(Prop(first, "musicResponsiveListItemFlexColumnRenderer"), "text"), "runs")
            ?? Prop(Prop(first, "title"), "runs");
        var run0 = runs0 is null || runs0.Value.ValueKind != JsonValueKind.Array ? default : runs0.Value.EnumerateArray().FirstOrDefault();
        var mvt = Prop(Prop(Prop(Prop(Prop(run0, "navigationEndpoint"), "watchEndpoint"), "watchEndpointMusicSupportedConfigs"), "watchEndpointMusicConfig"), "musicVideoType");
        return mvt is null ? null : mvt.Value.GetString() switch
        {
            "MUSIC_VIDEO_TYPE_UGC" or "MUSIC_VIDEO_TYPE_OMV" => "video",
            "MUSIC_VIDEO_TYPE_ATV" => "song",
            _ => null,
        };
    }

    /// <summary>youtubei.js's <c>views</c>: the second flex column run whose text matches "... views".</summary>
    private static string? ResponsiveViews(JsonElement? cols)
    {
        if (cols is null || cols.Value.ValueKind != JsonValueKind.Array) return null;
        var arr = cols.Value.EnumerateArray().ToList();
        if (arr.Count < 2) return null;
        var runs = Prop(Prop(Prop(arr[1], "musicResponsiveListItemFlexColumnRenderer"), "text"), "runs")
            ?? Prop(Prop(arr[1], "title"), "runs");
        if (runs is null || runs.Value.ValueKind != JsonValueKind.Array) return null;
        foreach (var run in runs.Value.EnumerateArray())
        {
            var t = Txt(run);
            if (Regex.IsMatch(t, "views")) return t;
        }
        return null;
    }

    private static List<ArtistRef> ArtistsOf(JsonElement n)
    {
        var outv = new List<ArtistRef>();
        foreach (var key in new[] { "artists", "authors", "author" })
        {
            if (!n.TryGetProperty(key, out var arr)) continue;
            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                    outv.Add(new ArtistRef { Name = Txt(Prop(a, "name")), Id = Str(Prop(a, "channel_id")) });
                return outv;
            }
            if (arr.ValueKind == JsonValueKind.Object)
                outv.Add(new ArtistRef { Name = Txt(Prop(arr, "name")), Id = Str(Prop(arr, "channel_id")) });
        }
        return outv;
    }

    private static YtItem? NormMultiRow(JsonElement n)
    {
        var (videoId, _, _, _) = Endpoint(Prop(n, "onTap") ?? Prop(n, "on_tap"));
        if (videoId is null) return null;
        return new YtItem { Type = "song", Id = videoId, Title = Txt(Prop(n, "title")), Subtitle = Txt(Prop(n, "subtitle")), Thumb = PickThumb(Prop(n, "thumbnail")), Artists = new List<ArtistRef> { new() { Name = Txt(Prop(n, "subtitle")) } }, Duration = 0, IsVideo = true, Fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb", "artists", "duration", "isVideo" } };
    }

    private static YtItem NormNavButton(JsonElement n)
    {
        // raw: navigationEndpoint may be absent; the target is in clickCommand.browseEndpoint
        var ep = Prop(n, "navigationEndpoint") ?? Prop(n, "endpoint") ?? Prop(n, "clickCommand");
        var (_, browseId, _, eparams) = Endpoint(ep);
        var color = Prop(n, "color");
        string? hex = null;
        if (color is not null && color.Value.ValueKind == JsonValueKind.Number)
            hex = "#" + (color.Value.GetUInt32() & 0xFFFFFF).ToString("x6");
        var nfields = new HashSet<string> { "type", "id", "title", "color" };
        if (eparams is not null) nfields.Add("params");
        return new YtItem { Type = "mood", Id = browseId, Params = eparams, Title = Txt(Prop(n, "buttonText") ?? Prop(n, "button_text")), Color = hex, Fields = nfields };
    }

    private static YtItem? NormPanel(JsonElement n)
    {
        if (n.TryGetProperty("primary", out var prim)) n = prim;
        var vid = Str(Prop(n, "videoId") ?? Prop(n, "video_id"));
        if (vid is null) return null;
        var artists = ArtistsOf(n);
        if (artists.Count == 0) artists.Add(new ArtistRef { Name = Str(Prop(n, "author")) ?? "" });
        return new YtItem
        {
            Type = "song", Id = vid, Title = Txt(Prop(n, "title")), Thumb = PickThumb(Prop(n, "thumbnail")), Artists = artists,
            Album = Prop(n, "album") is { } a ? new AlbumRef { Name = Txt(Prop(a, "name")), Id = Str(Prop(a, "id")) ?? "" } : null,
            Duration = DurationSeconds(Prop(n, "duration")), Explicit = IsExplicit(n),
            Fields = new HashSet<string> { "type", "id", "title", "thumb", "artists", "album", "duration", "explicit" },
        };
    }

    private static string? Str(JsonElement? e) => e is null || e.Value.ValueKind != JsonValueKind.String ? null : e.Value.GetString();

    /// <summary>normShelf: dispatch on the shelf renderer kind.</summary>
    public static YtShelf? NormShelf(string renderer, JsonElement s)
    {
        try
        {
            switch (renderer)
            {
                case "musicCarouselShelfRenderer":
                {
                    var h = Unwrap(Prop(s, "header"), "musicCarouselShelfBasicHeaderRenderer", "musicHeaderRenderer");
                    var list = Items(s);
                    if (list.Count == 0) return null;
                    var moreBtn = Prop(Prop(h, "moreContentButton"), "buttonRenderer");
                    var layout = list.All(i => i.Type == "song") && FirstChildType(s) != "musicTwoRowItemRenderer" ? "grid-songs"
                        : list.All(i => i.Type == "mood") ? "moods" : "carousel";
                    var moreEp = Prop(moreBtn, "navigationEndpoint") ?? Prop(Prop(Prop(h, "moreContent") ?? Prop(h, "more_content"), "endpoint"), "payload");
                    var (_, moreBrowse, morePlaylist, moreParams) = Endpoint(moreEp);
                    var moreId = moreBrowse ?? morePlaylist;
                    var more = moreId is null ? null : new Dictionary<string, string> { ["id"] = moreId };
                    if (more is not null && moreParams is not null) more["params"] = moreParams;
                    return new YtShelf { Title = Txt(Prop(h, "title")), Strapline = Txt(Prop(h, "strapline")), Thumb = PickThumb(Prop(Prop(h, "thumbnail"), "contents") ?? Prop(h, "thumbnail")), Layout = layout, Items = list, More = more, MoreField = true };
                }
                case "musicShelfRenderer":
                {
                    var list = Items(s);
                    if (list.Count == 0) return null;
                    var more = Prop(Prop(Prop(s, "bottomButton") ?? Prop(s, "bottom_button"), "endpoint"), "payload") ?? Prop(Prop(s, "endpoint"), "payload");
                    var bid = Str(Prop(more, "browseId"));
                    var q = Str(Prop(more, "query"));
                    return new YtShelf { Title = Txt(Prop(s, "title")), Layout = "list", Items = list, MoreField = true, More = bid is not null ? new Dictionary<string, string> { ["id"] = bid, ["params"] = Str(Prop(more, "params")) ?? "" } : q is not null ? new Dictionary<string, string> { ["query"] = q, ["params"] = Str(Prop(more, "params")) ?? "" } : null };
                }
                case "musicImmersiveCarouselShelfRenderer":
                {
                    var list = Items(s);
                    return list.Count > 0 ? new YtShelf { Title = Txt(Prop(Prop(s, "header"), "title")), Layout = "carousel", Items = list } : null;
                }
                case "gridRenderer":
                {
                    var list = Items(s);
                    var isMood = list.Count > 0 && list.All(i => i.Type == "mood");
                    return list.Count > 0 ? new YtShelf { Title = Txt(Prop(Prop(s, "header"), "title")), Layout = isMood ? "moods" : "grid", Items = list } : null;
                }
                case "musicPlaylistShelfRenderer":
                {
                    var list = Items(s);
                    return list.Count > 0 ? new YtShelf { Title = "", Layout = "list", Items = list } : null;
                }
                case "musicDescriptionShelfRenderer":
                {
                    var t = Txt(Prop(Prop(s, "header"), "title"));
                    return new YtShelf { Title = t.Length > 0 ? t : "About", Layout = "text", Text = Txt(Prop(s, "description")) };
                }
                default:
                    return null;
            }
        }
        catch { return null; }
    }

    private static string? FirstChildType(JsonElement s)
    {
        var c = Prop(s, "contents");
        if (c is null || c.Value.ValueKind != JsonValueKind.Array) return null;
        foreach (var x in c.Value.EnumerateArray())
            foreach (var key in x.EnumerateObject())
                if (key.Name.EndsWith("Renderer")) return key.Name;
        return null;
    }

    private static List<YtItem> Items(JsonElement shelf)
    {
        var outv = new List<YtItem>();
        var contents = Prop(shelf, "contents") ?? Prop(shelf, "items");
        if (contents is null || contents.Value.ValueKind != JsonValueKind.Array) return outv;
        foreach (var child in contents.Value.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in child.EnumerateObject())
            {
                if (!prop.Name.EndsWith("Renderer")) continue;
                var item = NormItem(prop.Name, prop.Value);
                if (item is not null) { outv.Add(item); break; }
            }
        }
        return outv;
    }
}
