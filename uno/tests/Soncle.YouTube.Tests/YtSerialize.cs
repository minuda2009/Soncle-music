// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Core.Models;
using Soncle.YouTube.Parsing;

namespace Soncle.YouTube.Tests;

/// <summary>
/// Serialises the parsed page/item/shelf objects back into the exact JSON shapes
/// <c>src/yt.mjs</c> produces, so the golden fixtures in <c>fixtures/yt/expected</c> can be
/// compared structurally. Only the fields whose key the JS object literal contains are emitted
/// (an undefined JS value is absent, a null one is present and serialised as null).
/// </summary>
internal static class YtSerialize
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };

    public static string Item(YtItem it) => JsonSerializer.Serialize(ItemDict(it), Opts);

    public static Dictionary<string, object?> ItemDict(YtItem it)
    {
        var d = new Dictionary<string, object?>();
        void Put(string k, object? v) { if (it.Fields.Contains(k)) d[k] = v; }
        Put("type", it.Type);
        Put("id", it.Id);
        Put("title", it.Title);
        Put("subtitle", it.Subtitle);
        Put("thumb", it.Thumb);
        if (it.Fields.Contains("artists")) d["artists"] = Artists(it.Artists ?? new());
        if (it.Fields.Contains("album")) d["album"] = it.Album is null ? null : new Dictionary<string, object?> { ["name"] = it.Album.Name, ["id"] = it.Album.Id };
        Put("duration", it.Duration);
        Put("year", it.Year);
        Put("explicit", it.Explicit);
        Put("isVideo", it.IsVideo);
        Put("params", it.Params);
        Put("color", it.Color);
        Put("plays", it.Plays);
        if (it.Fields.Contains("related")) d["related"] = (it.Related ?? new()).Select(ItemDict).ToList();
        return d;
    }

    private static List<Dictionary<string, object?>> Artists(List<ArtistRef> artists) =>
        artists.Select(a =>
        {
            var d = new Dictionary<string, object?> { ["name"] = a.Name };
            if (a.Id is not null) d["id"] = a.Id;
            return d;
        }).ToList();

    public static Dictionary<string, object?> ShelfDict(YtShelf s)
    {
        var d = new Dictionary<string, object?> { ["title"] = s.Title, ["layout"] = s.Layout };
        if (s.Layout != "text")
            d["items"] = s.Items.Select(ItemDict).ToList();
        if (s.Strapline is not null) d["strapline"] = s.Strapline;
        if (s.Thumb is not null) d["thumb"] = s.Thumb;
        if (s.MoreField) d["more"] = s.More;
        if (s.Text is not null) d["text"] = s.Text;
        return d;
    }

    public static string Shelf(YtShelf s) => JsonSerializer.Serialize(ShelfDict(s), Opts);

    public static string Home(YtHomePage p) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["chips"] = p.Chips,
        ["sections"] = p.Sections.Select(ShelfDict).ToList(),
        ["hasMore"] = p.HasMore,
    }, Opts);

    public static string Explore(YtExplorePage p) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["buttons"] = p.Buttons.Select(ItemDict).ToList(),
        ["sections"] = p.Sections.Select(ShelfDict).ToList(),
    }, Opts);

    public static string Search(YtSearchPage p)
    {
        var d = new Dictionary<string, object?>
        {
            ["query"] = p.Query,
            ["type"] = p.Type,
            ["top"] = p.Top is null ? null : ItemDict(p.Top),
            ["sections"] = p.Sections.Select(ShelfDict).ToList(),
            ["hasMore"] = p.HasMore,
            ["correction"] = p.Correction,
        };
        if (p.Fallback)
        {
            d["requestedType"] = p.RequestedType;
            d["fallback"] = true;
        }
        return JsonSerializer.Serialize(d, Opts);
    }

    public static string Playlist(YtPlaylistPage p)
    {
        var d = new Dictionary<string, object?> { ["type"] = "playlist", ["id"] = p.Id };
        foreach (var kv in Header(p.Info)) d[kv.Key] = kv.Value;
        d["tracks"] = p.Tracks.Select(ItemDict).ToList();
        d["hasMore"] = p.HasMore;
        return JsonSerializer.Serialize(d, Opts);
    }

    public static string Artist(YtArtistPage p) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["type"] = "artist",
        ["id"] = p.Id,
        ["title"] = p.Title,
        ["description"] = p.Description,
        ["thumb"] = p.Thumb,
        ["listeners"] = p.Listeners,
        ["subscribers"] = p.Subscribers,
        ["radioId"] = p.RadioId,
        ["shuffleId"] = p.ShuffleId,
        ["topSongsId"] = p.TopSongsId,
        ["albumsId"] = p.AlbumsId,
        ["sections"] = p.Sections.Select(ShelfDict).ToList(),
    }, Opts);

    public static string Items(List<YtItem> items) => JsonSerializer.Serialize(items.Select(ItemDict).ToList(), Opts);

    /// <summary>headerInfo's keys, in the JS literal's shape (artists only when the JS sets it).</summary>
    private static Dictionary<string, object?> Header(YtHeaderInfo info)
    {
        var d = new Dictionary<string, object?>
        {
            ["title"] = info.Title,
            ["subtitle"] = info.Subtitle,
            ["second"] = info.Second,
            ["thumb"] = info.Thumb,
        };
        if (info.HasArtists) d["artists"] = Artists(info.Artists ?? new());
        d["description"] = info.Description;
        return d;
    }
}
