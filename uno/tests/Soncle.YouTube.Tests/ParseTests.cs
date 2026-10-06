// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.TestKit;
using Soncle.Core.Models;
using Soncle.YouTube.Parsing;

namespace Soncle.YouTube.Tests;

/// <summary>
/// Golden test: for every raw renderer in <c>fixtures/yt/expected/cases.json</c>, the C# normaliser
/// must produce exactly what <c>src/yt.mjs</c> produced. This is the M06 acceptance.
/// </summary>
public class ParseTests
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };

    private static JsonElement Cases() =>
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("yt/expected/cases.json"))).RootElement;

    [Fact]
    public void ItemsMatchTheJs()
    {
        var items = Cases().GetProperty("items");
        Assert.True(items.GetArrayLength() > 50, "expected a decent number of item cases");
        var failures = new List<string>();
        var i = 0;
        foreach (var c in items.EnumerateArray())
        {
            i++;
            var renderer = c.GetProperty("renderer").GetString()!;
            var raw = c.GetProperty("raw");
            var expected = c.GetProperty("expected");
            var got = Normalize.NormItem(renderer, raw);
            if (got is null) { failures.Add($"#{i} {renderer}: got null"); continue; }
            try { JsonAssert.Equal(expected.GetRawText(), Serialize(got)); }
            catch (Exception e) { failures.Add($"#{i} {renderer}: {e.Message}"); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(15)));
    }

    [Fact]
    public void ShelvesMatchTheJs()
    {
        var shelves = Cases().GetProperty("shelves");
        Assert.True(shelves.GetArrayLength() > 0);
        var failures = new List<string>();
        var i = 0;
        foreach (var c in shelves.EnumerateArray())
        {
            i++;
            var renderer = c.GetProperty("renderer").GetString()!;
            var raw = c.GetProperty("raw");
            var expected = c.GetProperty("expected");
            var got = Normalize.NormShelf(renderer, raw);
            if (got is null) { failures.Add($"#{i} {renderer}: got null"); continue; }
            try { JsonAssert.Equal(expected.GetRawText(), Serialize(got)); }
            catch (Exception e) { failures.Add($"#{i} {renderer}: {e.Message}"); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(15)));
    }

    // Emit only the fields the JS object literal for this branch contains (its `Fields` set).
    private static string Serialize(YtItem it)
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
        return JsonSerializer.Serialize(d, Opts);
    }

    private static List<Dictionary<string, object?>> Artists(List<ArtistRef> artists) =>
        artists.Select(a =>
        {
            var d = new Dictionary<string, object?> { ["name"] = a.Name };
            if (a.Id is not null) d["id"] = a.Id;
            return d;
        }).ToList();

    private static string Serialize(YtShelf s)
    {
        var d = new Dictionary<string, object?> { ["title"] = s.Title, ["layout"] = s.Layout };
        if (s.Layout != "text")
            d["items"] = s.Items.Select(i => JsonSerializer.Deserialize<Dictionary<string, object?>>(Serialize(i), Opts)).ToList();
        if (s.Strapline is not null) d["strapline"] = s.Strapline;
        if (s.Thumb is not null) d["thumb"] = s.Thumb;
        if (s.MoreField) d["more"] = s.More;
        if (s.Text is not null) d["text"] = s.Text;
        return JsonSerializer.Serialize(d, Opts);
    }
}
