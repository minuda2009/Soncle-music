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
