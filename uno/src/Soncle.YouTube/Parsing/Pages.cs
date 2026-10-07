// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.RegularExpressions;
using Soncle.Core.Models;

namespace Soncle.YouTube.Parsing;

/// <summary>yt.mjs's <c>home()</c> result.</summary>
public sealed class YtHomePage
{
    public List<string> Chips { get; set; } = new();
    public List<YtShelf> Sections { get; set; } = new();
    public bool HasMore { get; set; }
}

/// <summary>yt.mjs's <c>explore()</c> result.</summary>
public sealed class YtExplorePage
{
    public List<YtItem> Buttons { get; set; } = new();
    public List<YtShelf> Sections { get; set; } = new();
}

/// <summary>yt.mjs's <c>search()</c> result.</summary>
public sealed class YtSearchPage
{
    public string Query { get; set; } = "";
    public string Type { get; set; } = "all";
    public YtItem? Top { get; set; }
    public List<YtShelf> Sections { get; set; } = new();
    public bool HasMore { get; set; }
    public string? Correction { get; set; }
    /// <summary>Set when a filtered search came back empty and 'all' was used instead.</summary>
    public string? RequestedType { get; set; }
    public bool Fallback { get; set; }
}

/// <summary>yt.mjs's <c>headerInfo()</c> result. <see cref="HasArtists"/> tracks whether the JS literal carries the key.</summary>
public sealed class YtHeaderInfo
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Second { get; set; } = "";
    public string Thumb { get; set; } = "";
    public List<ArtistRef>? Artists { get; set; }
    public bool HasArtists { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>yt.mjs's <c>playlist()</c> result (first page; continuations are M07's HTTP job).</summary>
public sealed class YtPlaylistPage
{
    public string Id { get; set; } = "";
    public YtHeaderInfo Info { get; set; } = new();
    public List<YtItem> Tracks { get; set; } = new();
    public bool HasMore { get; set; }
}

/// <summary>yt.mjs's <c>artist()</c> result.</summary>
public sealed class YtArtistPage
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Thumb { get; set; } = "";
    public string Listeners { get; set; } = "";
    public string Subscribers { get; set; } = "";
    public string? RadioId { get; set; }
    public string? ShuffleId { get; set; }
    public string? TopSongsId { get; set; }
    public string? AlbumsId { get; set; }
    public List<YtShelf> Sections { get; set; } = new();
}

/// <summary>yt.mjs's <c>browse()</c> result.</summary>
public sealed class YtBrowsePage
{
    public string Title { get; set; } = "";
    public List<YtShelf> Sections { get; set; } = new();
}

/// <summary>
/// The page builders of <c>src/yt.mjs</c> (home, explore, search, playlist, artist, browse,
/// upNext, radio, related), ported to walk raw InnerTube responses. No network: each method takes
/// the raw response JSON and returns exactly the plain JSON the JS produces (the golden tests in
/// <c>fixtures/yt/expected/pages</c> pin this). Continuation *requests* are M07; here a
/// continuation's presence is only reported as <c>HasMore</c>, like the JS's has_continuation.
/// </summary>
public static class Pages
{
    // The raw renderer keys that map to yt.mjs's SHELF_TYPES.
    private static readonly HashSet<string> ShelfKeys = new()
    {
        "musicCarouselShelfRenderer", "musicShelfRenderer", "gridRenderer",
        "musicPlaylistShelfRenderer", "musicImmersiveCarouselShelfRenderer", "musicDescriptionShelfRenderer",
    };

    private static JsonElement? Prop(JsonElement? n, string name) => Normalize.Prop(n, name);
    private static string Txt(JsonElement? t) => Normalize.Txt(t);
    private static string? Str(JsonElement? e) => Normalize.Str(e);

    /// <summary>The selected tab of a singleColumnBrowseResultsRenderer / twoColumnBrowseResultsRenderer / tabbedSearchResultsRenderer.</summary>
    private static JsonElement? SelectedTab(JsonElement raw)
    {
        var contents = Prop(raw, "contents");
        if (contents is null) return null;
        JsonElement? tabs = null;
        foreach (var key in new[] { "singleColumnBrowseResultsRenderer", "twoColumnBrowseResultsRenderer", "tabbedSearchResultsRenderer" })
        {
            var c = Prop(contents, key);
            if (c is null) continue;
            tabs = Prop(c, "tabs");
            break;
        }
        if (tabs is null || tabs.Value.ValueKind != JsonValueKind.Array) return null;
        JsonElement? first = null;
        foreach (var t in tabs.Value.EnumerateArray())
        {
            var tab = Prop(t, "tabRenderer");
            if (tab is null) continue;
            first ??= tab;
            var sel = Prop(tab, "selected");
            if (sel is not null && sel.Value.ValueKind == JsonValueKind.True) return tab;
        }
        return first;
    }

    private static bool HasContinuation(JsonElement? sectionList)
    {
        if (sectionList is null) return false;
        var cont = Prop(sectionList, "continuations");
        if (cont is not null && cont.Value.ValueKind == JsonValueKind.Array && cont.Value.EnumerateArray().Any()) return true;
        var contents = Prop(sectionList, "contents");
        if (contents is not null && contents.Value.ValueKind == JsonValueKind.Array)
            foreach (var c in contents.Value.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty("continuationItemRenderer", out _)) return true;
        return false;
    }

    /// <summary>The continuation token a follow-up request would use (carried, never sent here).</summary>
    private static string? ContinuationToken(JsonElement? sectionList)
    {
        var cont = Prop(sectionList, "continuations");
        if (cont is null || cont.Value.ValueKind != JsonValueKind.Array) return null;
        foreach (var c in cont.Value.EnumerateArray())
        {
            var token = Str(Prop(Prop(c, "nextContinuationData"), "continuation"));
            if (token is not null) return token;
        }
        return null;
    }

    /// <summary>collectShelves: walk raw JSON; norm every shelf renderer, never descending into one.</summary>
    public static List<YtShelf> CollectShelves(JsonElement? node) => CollectShelves(node, null);

    /// <summary>Same, but limited to the given renderer keys (null = every shelf type).</summary>
    public static List<YtShelf> CollectShelves(JsonElement? node, HashSet<string>? only)
    {
        var outv = new List<YtShelf>();
        Walk(node, 0);
        return outv;

        void Walk(JsonElement? n, int depth)
        {
            if (n is null || depth > 14) return;
            var e = n.Value;
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray()) Walk(x, depth + 1);
                return;
            }
            if (e.ValueKind != JsonValueKind.Object) return;
            // a renderer wrapper: { somethingRenderer: {...} }
            if (e.EnumerateObject().Count() == 1)
            {
                var only2 = e.EnumerateObject().First();
                if (ShelfKeys.Contains(only2.Name) && (only is null || only.Contains(only2.Name)))
                {
                    var sh = Normalize.NormShelf(only2.Name, only2.Value);
                    if (sh is not null) { outv.Add(sh); return; }
                }
            }
            foreach (var prop in e.EnumerateObject())
            {
                // shelves are consumed whole above; recurse into everything else
                if (ShelfKeys.Contains(prop.Name) && prop.Value.ValueKind == JsonValueKind.Object && (only is null || only.Contains(prop.Name)))
                {
                    var sh = Normalize.NormShelf(prop.Name, prop.Value);
                    if (sh is not null) { outv.Add(sh); continue; }
                }
                Walk(prop.Value, depth + 1);
            }
        }
    }

    /// <summary>home(): chips, shelves of the home feed's section list, hasMore.</summary>
    public static YtHomePage Home(JsonElement raw)
    {
        var sl = Prop(Prop(SelectedTab(raw), "content"), "sectionListRenderer");
        var chips = new List<string>();
        var cloud = Prop(Prop(Prop(sl, "header"), "chipCloudRenderer"), "chips");
        if (cloud is not null && cloud.Value.ValueKind == JsonValueKind.Array)
            foreach (var c in cloud.Value.EnumerateArray())
            {
                var chip = Prop(c, "chipCloudChipRenderer");
                if (chip is null) continue;
                var text = Txt(Prop(chip, "text"));
                if (text.Length > 0) chips.Add(text);
            }
        // yt.mjs: shelves(feed.sections) — the section list's top-level shelves, no deep walk
        var sections = new List<YtShelf>();
        var contents = Prop(sl, "contents");
        if (contents is not null && contents.Value.ValueKind == JsonValueKind.Array)
            foreach (var c in contents.Value.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                foreach (var prop in c.EnumerateObject())
                {
                    if (!ShelfKeys.Contains(prop.Name)) continue;
                    var sh = Normalize.NormShelf(prop.Name, prop.Value);
                    if (sh is not null) sections.Add(sh);
                    break;
                }
            }
        return new YtHomePage { Chips = chips, Sections = sections, HasMore = HasContinuation(sl) };
    }

    /// <summary>explore(): the first grid's navigation buttons plus the carousel shelves.</summary>
    public static YtExplorePage Explore(JsonElement raw)
    {
        var sl = Prop(Prop(SelectedTab(raw), "content"), "sectionListRenderer");
        var buttons = new List<YtItem>();
        var sections = new List<YtShelf>();
        var contents = Prop(sl, "contents");
        var buttonsDone = false;
        if (contents is not null && contents.Value.ValueKind == JsonValueKind.Array)
            foreach (var c in contents.Value.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                if (!buttonsDone && c.TryGetProperty("gridRenderer", out var grid))
                {
                    // youtubei.js: section_list.contents.firstOfType(Grid).items.as(MusicNavigationButton)
                    buttonsDone = true;
                    var items = Prop(grid, "items");
                    if (items is not null && items.Value.ValueKind == JsonValueKind.Array)
                        foreach (var it in items.Value.EnumerateArray())
                        {
                            var nav = Prop(it, "musicNavigationButtonRenderer");
                            if (nav is null) continue;
                            var b = Normalize.NormItem("musicNavigationButtonRenderer", nav.Value);
                            // yt.mjs keeps only buttons with an id
                            if (b?.Id is not null) buttons.Add(b);
                        }
                    continue;
                }
                if (c.TryGetProperty("musicCarouselShelfRenderer", out var car))
                {
                    // youtubei.js: section_list.contents.filterType(MusicCarouselShelf)
                    var sh = Normalize.NormShelf("musicCarouselShelfRenderer", car);
                    if (sh is not null) sections.Add(sh);
                }
            }
        return new YtExplorePage { Buttons = buttons, Sections = sections };
    }

    // ---------- search ----------

    private static readonly HashSet<string> SafeSearchTypes = new() { "all", "song", "video", "album", "artist", "playlist" };

    /// <summary>The search page's top-hit card (MusicCardShelf), or null when absent/unrecognised.</summary>
    private static YtItem? NormCardShelf(JsonElement c)
    {
        // yt.mjs: c.on_tap || runEndpoint(c.title) || runEndpoint(c.title.runs[0])
        var ep = Prop(c, "onTap") ?? Prop(c, "on_tap") ?? Normalize.RunEndpoint(Prop(c, "title") ?? default);
        if (ep is null)
        {
            var runs = Prop(Prop(c, "title"), "runs");
            if (runs is not null && runs.Value.ValueKind == JsonValueKind.Array)
                foreach (var r in runs.Value.EnumerateArray()) { ep = Normalize.RunEndpoint(r); break; }
        }
        var (videoId, browseId, playlistId, _) = Normalize.Endpoint(ep);
        var pk = Normalize.KindFromPage(Normalize.PageType(ep));
        var title = Txt(Prop(c, "title"));
        var subtitle = Txt(Prop(c, "subtitle"));
        var thumb = Normalize.PickThumb(Prop(Prop(c, "thumbnail"), "contents") ?? Prop(c, "thumbnail"));
        YtItem? top = null;
        if (videoId is not null)
        {
            var m = Regex.Match(subtitle, @"\d+:\d\d(:\d\d)?");
            top = new YtItem
            {
                Type = "song", Id = videoId, Title = title, Subtitle = subtitle, Thumb = thumb,
                Artists = Normalize.ArtistsFromRuns(Prop(Prop(c, "subtitle"), "runs")),
                Duration = Normalize.ParseDuration(m.Success ? m.Value : ""),
                Fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb", "artists", "duration" },
            };
        }
        else if (pk.Length > 0)
        {
            top = new YtItem { Type = pk, Id = browseId, Title = title, Subtitle = subtitle, Thumb = thumb, Fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb" } };
        }
        else if (playlistId is not null)
        {
            top = new YtItem { Type = "playlist", Id = playlistId, Title = title, Subtitle = subtitle, Thumb = thumb, Fields = new HashSet<string> { "type", "id", "title", "subtitle", "thumb" } };
        }
        if (top is null) return null;
        // top.related = items(c.contents)
        var related = new List<YtItem>();
        var contents = Prop(c, "contents");
        if (contents is not null && contents.Value.ValueKind == JsonValueKind.Array)
            foreach (var child in contents.Value.EnumerateArray())
            {
                if (child.ValueKind != JsonValueKind.Object) continue;
                foreach (var prop in child.EnumerateObject())
                {
                    if (!prop.Name.EndsWith("Renderer")) continue;
                    var item = Normalize.NormItem(prop.Name, prop.Value);
                    if (item is not null) { related.Add(item); break; }
                }
            }
        return new YtItem
        {
            Type = top.Type, Id = top.Id, Title = top.Title, Subtitle = top.Subtitle, Thumb = top.Thumb,
            Artists = top.Artists, Duration = top.Duration, Related = related,
            Fields = new HashSet<string>(top.Fields) { "related" },
        };
    }

    /// <summary>normShelf's ItemSection branch: a nested shelf wins, otherwise the rows form one list.</summary>
    private static YtShelf? NormItemSection(JsonElement s)
    {
        var contents = Prop(s, "contents");
        if (contents is null || contents.Value.ValueKind != JsonValueKind.Array) return null;
        foreach (var c in contents.Value.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in c.EnumerateObject())
                if (ShelfKeys.Contains(prop.Name))
                {
                    var nested = Normalize.NormShelf(prop.Name, prop.Value);
                    if (nested is not null) return nested;
                }
        }
        var items = new List<YtItem>();
        foreach (var c in contents.Value.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in c.EnumerateObject())
            {
                if (!prop.Name.EndsWith("Renderer")) continue;
                var item = Normalize.NormItem(prop.Name, prop.Value);
                if (item is not null) { items.Add(item); break; }
            }
        }
        if (items.Count == 0) return null;
        return new YtShelf { Title = Txt(Prop(Prop(s, "header"), "title")), Layout = "list", Items = items };
    }

    /// <summary>mergeSections: untitled list sections merge into one, appended after the titled ones.</summary>
    internal static List<YtShelf> MergeSections(List<YtShelf> sections)
    {
        var outv = new List<YtShelf>();
        var untitled = new List<YtItem>();
        foreach (var s in sections)
        {
            if (s.Layout == "list" && string.IsNullOrEmpty(s.Title)) untitled.AddRange(s.Items);
            else outv.Add(s);
        }
        if (untitled.Count > 0) outv.Add(new YtShelf { Title = "", Layout = "list", Items = untitled });
        return outv;
    }

    /// <summary>
    /// search(): top hit + sections + correction + hasMore. <paramref name="rawForType"/> supplies
    /// the recorded response per filter (the JS re-queries over HTTP; here the caller hands the
    /// fixture for 'all' so the empty-filter fallback stays offline).
    /// </summary>
    public static YtSearchPage Search(JsonElement raw, string query, string type, Func<string, JsonElement>? rawForType = null)
    {
        if (!SafeSearchTypes.Contains(type)) type = "all";
        var outp = ParseSearch(raw, query, type);
        // Some filters come back as an empty Message section; fall back to 'all' and filter here.
        if (type != "all" && outp.Sections.Count == 0 && outp.Top is null)
        {
            var allRaw = rawForType?.Invoke("all");
            var fallback = allRaw is not null ? ParseSearch(allRaw.Value, query, "all") : new YtSearchPage { Query = query, Type = "all" };
            var want = type is "song" or "album" ? type : null;
            var secs = fallback.Sections;
            if (want is not null)
            {
                var kept = secs.SelectMany(s => s.Items).Where(i => i.Type == want && !(want == "song" && i.IsVideo)).ToList();
                secs = new List<YtShelf> { new() { Title = "", Layout = "list", Items = kept } };
            }
            return new YtSearchPage
            {
                Query = query, Type = want ?? "all", Top = fallback.Top, Sections = secs,
                HasMore = fallback.HasMore, Correction = fallback.Correction,
                RequestedType = type, Fallback = true,
            };
        }
        outp.Sections = MergeSections(outp.Sections);
        if (type != "all")
        {
            var sl = Prop(Prop(SelectedTab(raw), "content"), "sectionListRenderer");
            outp.HasMore = HasContinuation(sl);
        }
        return outp;
    }

    private static YtSearchPage ParseSearch(JsonElement raw, string query, string type)
    {
        var outp = new YtSearchPage { Query = query, Type = type };
        // did_you_mean: memo-wide in youtubei.js, so search the whole response
        outp.Correction = FindDidYouMean(raw);
        var sl = Prop(Prop(SelectedTab(raw), "content"), "sectionListRenderer");
        var contents = Prop(sl, "contents");
        if (contents is not null && contents.Value.ValueKind == JsonValueKind.Array)
            foreach (var c in contents.Value.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                if (c.TryGetProperty("musicCardShelfRenderer", out var card))
                {
                    var top = NormCardShelf(card);
                    if (top is not null) outp.Top = top;
                    continue;
                }
                if (c.TryGetProperty("itemSectionRenderer", out var isec))
                {
                    var sh = NormItemSection(isec);
                    if (sh is not null) outp.Sections.Add(sh);
                    continue;
                }
                foreach (var prop in c.EnumerateObject())
                {
                    if (!ShelfKeys.Contains(prop.Name)) continue;
                    var sh = Normalize.NormShelf(prop.Name, prop.Value);
                    if (sh is not null) outp.Sections.Add(sh);
                    break;
                }
            }
        // Last-ditch: deep-scan for shelves if the top-level walk found nothing.
        if (outp.Sections.Count == 0 && outp.Top is null)
            outp.Sections = CollectShelves(Prop(raw, "contents"));
        return outp;
    }

    private static string? FindDidYouMean(JsonElement? node)
    {
        if (node is null) return null;
        var e = node.Value;
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray()) { var r = FindDidYouMean(x); if (r is not null) return r; }
            return null;
        }
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (e.TryGetProperty("didYouMeanRenderer", out var dym))
        {
            var t = Txt(Prop(dym, "correctedQuery"));
            if (t.Length > 0) return t;
        }
        foreach (var prop in e.EnumerateObject())
        {
            var r = FindDidYouMean(prop.Value);
            if (r is not null) return r;
        }
        return null;
    }

    // ---------- detail pages ----------

    /// <summary>headerInfo(): the detail header shared by album/playlist (and browse).</summary>
    internal static YtHeaderInfo HeaderInfo(JsonElement? h)
    {
        var info = new YtHeaderInfo();
        if (h is null) return info;
        info.Title = Txt(Prop(h, "title"));
        info.Subtitle = Txt(Prop(h, "subtitle"));
        info.Second = Txt(Prop(h, "secondSubtitle") ?? Prop(h, "second_subtitle"));
        info.Thumb = Normalize.PickThumb(Prop(Prop(h, "thumbnail"), "contents") ?? Prop(h, "thumbnails") ?? Prop(h, "thumbnail"));
        var strap = Prop(h, "straplineTextOne") ?? Prop(h, "strapline_text_one");
        if (strap is not null && Prop(strap, "runs") is { ValueKind: JsonValueKind.Array } runs)
        {
            info.Artists = Normalize.ArtistsFromRuns(runs);
            if (info.Artists.Count == 0) info.Artists = new List<ArtistRef> { new() { Name = Txt(strap) } };
            info.HasArtists = true;
        }
        else
        {
            var author = Prop(h, "author");
            if (author is not null)
            {
                info.Artists = new List<ArtistRef> { new() { Name = Txt(Prop(author, "name")), Id = Str(Prop(author, "channel_id")) } };
                info.HasArtists = true;
            }
        }
        var d = Prop(h, "description");
        if (d is not null)
        {
            // youtubei.js: a description shelf node (d.description) or a plain Text
            var shelf = Prop(d.Value, "musicDescriptionShelfRenderer");
            var inner = shelf is not null ? Prop(shelf, "description") : Prop(d.Value, "description");
            info.Description = Txt(inner ?? d);
        }
        return info;
    }

    /// <summary>Find the first occurrence of a renderer key anywhere in the tree.</summary>
    internal static JsonElement? FindRenderer(JsonElement? node, params string[] keys)
    {
        if (node is null) return null;
        var e = node.Value;
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray()) { var r = FindRenderer(x, keys); if (r is not null) return r; }
            return null;
        }
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var k in keys)
            if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Object) return v;
        foreach (var prop in e.EnumerateObject())
        {
            var r = FindRenderer(prop.Value, keys);
            if (r is not null) return r;
        }
        return null;
    }

    /// <summary>playlist(): header info + the playlist shelf's tracks + hasMore (first page only).</summary>
    public static YtPlaylistPage Playlist(JsonElement raw, string id, bool all = false)
    {
        var header = FindRenderer(Prop(raw, "contents"), "musicResponsiveHeaderRenderer", "musicEditablePlaylistDetailHeaderRenderer", "musicDetailHeaderRenderer");
        var info = HeaderInfo(header);
        var tracks = new List<YtItem>();
        var shelf = FindRenderer(Prop(raw, "contents"), "musicPlaylistShelfRenderer");
        if (shelf is not null)
        {
            var contents = Prop(shelf, "contents");
            if (contents is not null && contents.Value.ValueKind == JsonValueKind.Array)
                foreach (var c in contents.Value.EnumerateArray())
                {
                    var item = Prop(c, "musicResponsiveListItemRenderer");
                    if (item is null) continue; // skips continuationItemRenderer like youtubei.js's .as()
                    var t = Normalize.NormItem("musicResponsiveListItemRenderer", item.Value);
                    if (t is not null) tracks.Add(t);
                }
        }
        // yt.mjs: each track inherits the header's thumb/artists and the album it belongs to
        tracks = tracks.Select(t => new YtItem
        {
            Type = t.Type, Id = t.Id, Title = t.Title, Subtitle = t.Subtitle,
            Thumb = string.IsNullOrEmpty(t.Thumb) ? info.Thumb : t.Thumb,
            Artists = t.Artists is { Count: > 0 } && !string.IsNullOrEmpty(t.Artists[0].Name) ? t.Artists : info.Artists ?? new List<ArtistRef>(),
            Album = new AlbumRef { Name = info.Title, Id = id },
            Duration = t.Duration, Year = t.Year, Explicit = t.Explicit, IsVideo = t.IsVideo,
            Params = t.Params, Color = t.Color, Plays = t.Plays,
            Fields = new HashSet<string>(t.Fields),
        }).ToList();
        return new YtPlaylistPage { Id = id, Info = info, Tracks = tracks, HasMore = !all && HasContinuation(shelf) };
    }

    /// <summary>artist(): the immersive/visual header plus the shelf and carousel sections.</summary>
    public static YtArtistPage Artist(JsonElement raw, string id)
    {
        var header = Prop(Prop(raw, "header"), "musicImmersiveHeaderRenderer") ?? Prop(Prop(raw, "header"), "musicVisualHeaderRenderer");
        string subscribers = "";
        if (header is not null)
        {
            var sb = Prop(Prop(header, "subscriptionButton"), "subscribeButtonRenderer");
            // raw subscriberCountText wins; otherwise fish a count out of the button's texts
            subscribers = Txt(Prop(sb, "subscriberCountText") ?? Prop(sb, "subscriber_count"));
            if (subscribers.Length == 0)
            {
                foreach (var key in new[] { "subscriberCountWithSubscribeText", "subscribedButtonText", "unsubscribedButtonText" })
                {
                    var raw2 = Txt(Prop(sb, key));
                    if (raw2.Length == 0) continue;
                    var m = Regex.Match(raw2, @"[\d.,]+\s*[KMB]?", RegexOptions.IgnoreCase);
                    subscribers = m.Success ? m.Value.Trim() : "";
                    if (subscribers.Length > 0) break;
                }
            }
        }
        var radioEp = Prop(Prop(Prop(Prop(header, "startRadioButton"), "buttonRenderer"), "navigationEndpoint"), "watchPlaylistEndpoint");
        var playEp = Prop(Prop(Prop(Prop(header, "playButton"), "buttonRenderer"), "navigationEndpoint"), "watchPlaylistEndpoint");
        // youtubei.js gathers the artist page's shelves as [...MusicShelf, ...MusicCarouselShelf]
        var sections = CollectShelves(Prop(raw, "contents"), new HashSet<string> { "musicShelfRenderer" });
        sections.AddRange(CollectShelves(Prop(raw, "contents"), new HashSet<string> { "musicCarouselShelfRenderer" }));
        var songList = sections.FirstOrDefault(s => s.Items.Count > 0 && s.Items.All(i => i.Type == "song"));
        var albumGrid = sections.FirstOrDefault(s => s.Items.Count > 0 && s.Items.All(i => i.Type == "album"));
        return new YtArtistPage
        {
            Id = id,
            Title = Txt(Prop(header, "title")),
            Description = Txt(Prop(header, "description")),
            Thumb = Normalize.PickThumb(Prop(Prop(header, "thumbnail"), "contents") ?? Prop(header, "thumbnail") ?? Prop(header, "foregroundThumbnail")),
            Listeners = Txt(Prop(header, "monthlyListenerCount")),
            Subscribers = subscribers,
            RadioId = Str(Prop(radioEp, "playlistId")),
            ShuffleId = Str(Prop(playEp, "playlistId")),
            TopSongsId = songList?.More is not null && songList.More.TryGetValue("id", out var ts) ? ts : null,
            AlbumsId = albumGrid?.More is not null && albumGrid.More.TryGetValue("id", out var al) ? al : null,
            Sections = sections,
        };
    }

    /// <summary>browse(): the page header's title plus every shelf in the contents.</summary>
    public static YtBrowsePage Browse(JsonElement raw)
    {
        var header = FindRenderer(Prop(raw, "header"),
            "musicHeaderRenderer", "musicImmersiveHeaderRenderer", "musicVisualHeaderRenderer", "musicResponsiveHeaderRenderer", "musicDetailHeaderRenderer")
            ?? FindRenderer(Prop(raw, "contents"),
            "musicHeaderRenderer", "musicImmersiveHeaderRenderer", "musicVisualHeaderRenderer", "musicResponsiveHeaderRenderer", "musicDetailHeaderRenderer");
        return new YtBrowsePage { Title = Txt(Prop(header, "title")), Sections = CollectShelves(Prop(raw, "contents")) };
    }

    // ---------- watch-next ----------

    /// <summary>The playlist panel of a watch-next response (upNext / radio).</summary>
    private static JsonElement? PlaylistPanel(JsonElement rawNext) =>
        FindRenderer(Prop(rawNext, "contents"), "playlistPanelRenderer");

    /// <summary>upNext()/radio(): the queue's songs, in order.</summary>
    public static List<YtItem> UpNext(JsonElement rawNext)
    {
        var outv = new List<YtItem>();
        var panel = PlaylistPanel(rawNext);
        var contents = Prop(panel, "contents");
        if (contents is null || contents.Value.ValueKind != JsonValueKind.Array) return outv;
        foreach (var c in contents.Value.EnumerateArray())
        {
            JsonElement? node = Prop(c, "playlistPanelVideoRenderer");
            // PlaylistPanelVideoWrapper: the JS reads .primary
            var wrapper = Prop(c, "playlistPanelVideoWrapperRenderer");
            if (node is null && wrapper is not null)
                node = Prop(Prop(wrapper, "primaryRenderer"), "playlistPanelVideoRenderer");
            if (node is null) continue;
            var item = PanelItem(node.Value);
            if (item is not null) outv.Add(item);
        }
        return outv;
    }

    /// <summary>normPanel on a raw playlistPanelVideoRenderer.</summary>
    private static YtItem? PanelItem(JsonElement n)
    {
        var vid = Str(Prop(n, "videoId"));
        if (vid is null) return null;
        // youtubei.js parses artists from the long byline's UC runs; the author is its plain text
        var byline = Prop(n, "longBylineText") ?? Prop(n, "shortBylineText");
        var artists = Normalize.ArtistsFromRuns(Prop(byline, "runs"));
        if (artists.Count == 0) artists.Add(new ArtistRef { Name = Txt(byline) });
        // an album run links an MPRE/MPR browse id
        AlbumRef? album = null;
        var runs = Prop(byline, "runs");
        if (runs is not null && runs.Value.ValueKind == JsonValueKind.Array)
            foreach (var r in runs.Value.EnumerateArray())
            {
                var bid = Normalize.Endpoint(Normalize.RunEndpoint(r)).BrowseId;
                if (bid is not null && bid.StartsWith("MPR"))
                {
                    album = new AlbumRef { Name = Txt(r), Id = bid };
                    break;
                }
            }
        // parsed duration.seconds comes from lengthText ("3:45")
        var duration = Normalize.ParseDuration(Txt(Prop(n, "lengthText")));
        return new YtItem
        {
            Type = "song", Id = vid, Title = Txt(Prop(n, "title")), Thumb = Normalize.PickThumb(Prop(n, "thumbnail")),
            Artists = artists, Album = album, Duration = duration, Explicit = Normalize.IsExplicit(n),
            Fields = new HashSet<string> { "type", "id", "title", "thumb", "artists", "album", "duration", "explicit" },
        };
    }

    /// <summary>related(): shelves of the TRACK_RELATED tab's inline content, when recorded.</summary>
    public static List<YtShelf> Related(JsonElement rawNext)
    {
        var contents = Prop(rawNext, "contents");
        var tabs = Prop(Prop(Prop(contents, "singleColumnMusicWatchNextResultsRenderer"), "tabbedRenderer") is { } tabbed ? Prop(tabbed, "watchNextTabbedResultsRenderer") : null, "tabs");
        if (tabs is null || tabs.Value.ValueKind != JsonValueKind.Array) return new List<YtShelf>();
        foreach (var entry in tabs.Value.EnumerateArray())
        {
            var tab = Prop(entry, "tabRenderer");
            if (tab is null) continue;
            var pt = Normalize.PageType(Prop(tab, "endpoint"));
            if (pt != "MUSIC_PAGE_TYPE_TRACK_RELATED") continue;
            return CollectShelves(Prop(tab, "content"));
        }
        return new List<YtShelf>();
    }
}
