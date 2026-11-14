// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Services;
using Soncle.TestKit;

namespace Soncle.Services.Tests;

/// <summary>
/// Golden diff for the lyrics engine (M09d): every case in <c>fixtures/lyrics/engine.json</c> is the
/// JS engine's own output, so the port is checked against it rather than a hand-written expectation.
/// </summary>
public class LyricsEngineTests
{
    private static readonly JsonElement Root = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("lyrics/engine.json"))).RootElement;

    private static LyricsTrack Track(JsonElement song)
    {
        var artists = song.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? "").ToList()
            : new List<string>();
        return new LyricsTrack
        {
            Title = song.GetProperty("title").GetString() ?? "",
            Artists = artists,
            AlbumName = song.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object ? al.GetProperty("name").GetString() : null,
            Duration = song.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null,
        };
    }

    private static LrcCandidate Candidate(JsonElement e) => new(
        e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt64() : null,
        e.TryGetProperty("trackName", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
        e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
        e.TryGetProperty("artistName", out var ar) && ar.ValueKind == JsonValueKind.String ? ar.GetString() : null,
        e.TryGetProperty("albumName", out var al) && al.ValueKind == JsonValueKind.String ? al.GetString() : null,
        e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null,
        e.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True,
        e.TryGetProperty("plainLyrics", out var pl) && pl.ValueKind == JsonValueKind.String ? pl.GetString() : null,
        e.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String ? sl.GetString() : null);

    private static Func<int, Task> NoWait => _ => Task.CompletedTask;

    [Fact]
    public void CleanTitleNormAndFlagsMatchTheJs()
    {
        foreach (var c in Root.GetProperty("cleanTitle").EnumerateArray())
            Assert.Equal(c.GetProperty("out").GetString(), LyricsEngine.CleanTitle(c.GetProperty("input").GetString()));
        foreach (var c in Root.GetProperty("norm").EnumerateArray())
            Assert.Equal(c.GetProperty("out").GetString(), LyricsEngine.Norm(c.GetProperty("input").GetString()));
        foreach (var c in Root.GetProperty("flagsOf").EnumerateArray())
            Assert.Equal(
                c.GetProperty("out").EnumerateArray().Select(x => x.GetString()!).ToList(),
                LyricsEngine.FlagsOf(c.GetProperty("input").GetString()));
    }

    [Fact]
    public void TitleSimilarityMatchesTheJs()
    {
        foreach (var c in Root.GetProperty("titleSimilarity").EnumerateArray())
            Assert.Equal(c.GetProperty("out").GetDouble(), LyricsEngine.TitleSimilarity(c.GetProperty("a").GetString(), c.GetProperty("b").GetString()), 6);
    }

    [Fact]
    public void ScoresMatchTheJs()
    {
        foreach (var c in Root.GetProperty("score").EnumerateArray())
        {
            var got = LyricsEngine.ScoreCandidate(Track(c.GetProperty("song")), Candidate(c.GetProperty("candidate")));
            Assert.Equal(c.GetProperty("score").GetDouble(), got, 6);
        }
    }

    [Fact]
    public void RankingConfidenceAndVariantsMatchTheJs()
    {
        foreach (var c in Root.GetProperty("rank").EnumerateArray())
        {
            var song = Track(c.GetProperty("song"));
            var list = c.GetProperty("list").EnumerateArray().Select(Candidate).ToList();
            var ranked = LyricsEngine.RankCandidates(song, list);

            var expected = c.GetProperty("ranked").EnumerateArray().ToList();
            Assert.Equal(expected.Count, ranked.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].GetProperty("id").GetInt64(), ranked[i].C.Id);
                Assert.Equal(expected[i].GetProperty("score").GetDouble(), ranked[i].Score, 6);
            }

            Assert.Equal(c.GetProperty("confident").GetBoolean(), LyricsEngine.IsConfident(ranked));

            var variants = c.GetProperty("variants").EnumerateArray().ToList();
            var gotVariants = ranked.Take(8).Select(x => LyricsEngine.VariantOf(x.C, x.Score)).ToList();
            Assert.Equal(variants.Count, gotVariants.Count);
            for (var i = 0; i < variants.Count; i++)
            {
                Assert.Equal(variants[i].GetProperty("id").GetInt64(), gotVariants[i].Id);
                Assert.Equal(variants[i].GetProperty("duration").GetInt32(), gotVariants[i].Duration);
                Assert.Equal(variants[i].GetProperty("synced").GetBoolean(), gotVariants[i].Synced);
                Assert.Equal(variants[i].GetProperty("score").GetDouble(), gotVariants[i].Score, 6);
            }
        }
    }

    [Fact]
    public void SyncIssuesMatchTheJs()
    {
        foreach (var c in Root.GetProperty("sync").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var synced = input.GetProperty("synced").ValueKind == JsonValueKind.String ? input.GetProperty("synced").GetString() : null;
            var duration = input.GetProperty("duration").ValueKind == JsonValueKind.Number ? input.GetProperty("duration").GetDouble() : (double?)null;
            Assert.Equal(
                c.GetProperty("issues").EnumerateArray().Select(x => x.GetString()!).ToList(),
                LyricsEngine.SyncIssues(synced, duration));
        }
    }

    [Fact]
    public async Task FindCandidatesMatchesTheJs()
    {
        foreach (var c in Root.GetProperty("find").EnumerateArray())
        {
            var song = Track(c.GetProperty("song"));
            var cands = await LyricsEngine.FindCandidatesAsync(song, ReplayFetch(c), NoWait);
            var expected = c.GetProperty("candidates").EnumerateArray().ToList();
            Assert.Equal(expected.Count, cands.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                if (expected[i].ValueKind == JsonValueKind.Null) Assert.Null(cands[i].Id);
                else Assert.Equal(expected[i].GetInt64(), cands[i].Id);
            }
        }
    }

    [Fact]
    public async Task GetMatchesTheJs()
    {
        foreach (var c in Root.GetProperty("get").EnumerateArray())
        {
            var song = Track(c.GetProperty("song"));
            long? preferId = c.TryGetProperty("preferId", out var pid) && pid.ValueKind == JsonValueKind.Number ? pid.GetInt64() : null;
            var got = await LyricsEngine.GetAsync(song, ReplayFetch(c), NoWait, preferId);
            var expected = c.GetProperty("result");
            if (expected.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(got);
                continue;
            }
            Assert.NotNull(got);
            Assert.Equal(expected.GetProperty("synced").ValueKind == JsonValueKind.String ? expected.GetProperty("synced").GetString() : null, got!.Synced);
            Assert.Equal(expected.GetProperty("plain").ValueKind == JsonValueKind.String ? expected.GetProperty("plain").GetString() : null, got.Plain);
            Assert.Equal(expected.GetProperty("source").GetString(), got.Source);
            Assert.Equal(expected.GetProperty("id").GetInt64(), got.Id);
            Assert.Equal(expected.GetProperty("score").GetDouble(), got.Score, 6);
            Assert.Equal(expected.GetProperty("confident").GetBoolean(), got.Confident);
            Assert.Equal(expected.GetProperty("chosen").GetBoolean(), got.Chosen);
            Assert.Equal(expected.GetProperty("issues").EnumerateArray().Select(x => x.GetString()!).ToList(), got.Issues);
            Assert.Equal(expected.GetProperty("variants").GetArrayLength(), got.Variants.Count);
        }
    }

    /// <summary>
    /// A fetch that answers exactly what the JS fixture recorded. Each `get` case replays the
    /// candidates the JS saw (its `candidates` list is stored too), so the C# makes the same call.
    /// </summary>
    private static LyricsFetch ReplayFetch(JsonElement testCase)
    {
        var routes = new Dictionary<string, string>();
        if (testCase.TryGetProperty("routes", out var r) && r.ValueKind == JsonValueKind.Object)
            foreach (var p in r.EnumerateObject()) routes[p.Name] = p.Value.GetRawText();
        return (url, _) =>
        {
            foreach (var (frag, body) in routes)
                if (url.Contains(frag)) return Task.FromResult((200, body));
            return Task.FromResult((404, ""));
        };
    }
}
