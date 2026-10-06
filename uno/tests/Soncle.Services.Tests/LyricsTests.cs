// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Services;
using Soncle.TestKit;

namespace Soncle.Services.Tests;

public class LyricsTests
{
    [Fact]
    public void LrcParsingMatchesTheJs()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("lyrics/lrc.json"))).RootElement;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            var lrc = c.GetProperty("lrc").GetString()!;
            var expected = c.GetProperty("lines").EnumerateArray().ToList();
            var got = Lyrics.ParseLrc(lrc);
            Assert.Equal(expected.Count, got.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].GetProperty("t").GetDouble(), got[i].T, 6);
                Assert.Equal(expected[i].GetProperty("text").GetString(), got[i].Text);
                Assert.Equal(expected[i].GetProperty("end").GetDouble(), got[i].End, 6);
                var ew = expected[i].GetProperty("words");
                if (ew.ValueKind == JsonValueKind.Null) Assert.Null(got[i].Words);
                else Assert.Equal(ew.GetArrayLength(), got[i].Words!.Count);
            }
        }
    }

    [Fact]
    public void CleanTitleStripsBracketedQualifiers()
    {
        Assert.Equal("Numb", Lyrics.CleanTitle("Numb (Official Video)"));
        Assert.Equal("Numb", Lyrics.CleanTitle("Numb [Official Audio]"));
        Assert.Equal("Wonderwall", Lyrics.CleanTitle("Wonderwall (Remastered 2014)"));
        Assert.Equal("Song", Lyrics.CleanTitle("Song (feat. X)"));   // feat is stripped too
    }

    [Fact]
    public async Task LrclibGetThenSearch()
    {
        const string synced = "[00:01.00]Hello";
        Task<(int, string)> Fetch(string url, IReadOnlyDictionary<string, string> headers)
        {
            if (url.Contains("/api/get?"))
                return Task.FromResult((200, JsonSerializer.Serialize(new { syncedLyrics = synced, plainLyrics = (string?)null })));
            return Task.FromResult((404, ""));
        }
        var r = await Lyrics.LrclibAsync("Numb (Official Video)", "Meteora", new[] { "Linkin Park" }, 185, Fetch);
        Assert.NotNull(r);
        Assert.Equal(synced, r!.Synced);
        Assert.Equal("LRCLIB", r.Source);
    }

    [Fact]
    public async Task LrclibSearchUsesDurationMatchThenFallsBack()
    {
        var results = new object[]
        {
            new { syncedLyrics = (string?)null, plainLyrics = "plain", duration = 100 },
            new { syncedLyrics = "[00:01.00]Right", plainLyrics = (string?)null, duration = 185 },
        };
        Task<(int, string)> Fetch(string url, IReadOnlyDictionary<string, string> headers)
        {
            if (url.Contains("/api/get?")) return Task.FromResult((404, ""));
            return Task.FromResult((200, JsonSerializer.Serialize(results)));
        }
        var r = await Lyrics.LrclibAsync("Numb", null, new[] { "Linkin Park" }, 185, Fetch);
        Assert.Equal("[00:01.00]Right", r!.Synced);
    }
}
