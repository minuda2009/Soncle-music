// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Services;

namespace Soncle.Services.Tests;

/// <summary>The JS <c>test/spotify.test.mjs</c> cases, ported.</summary>
public class ImportTests
{
    [Fact]
    public void Links()
    {
        Assert.Equal(new SpotifyRef("playlist", "37i9dQZF1DX0XUsuxWHRQd"), Import.ParseSpotifyLink("https://open.spotify.com/playlist/37i9dQZF1DX0XUsuxWHRQd?si=abc"));
        Assert.Equal(new SpotifyRef("album", "4aawyAB9vmqN3uQ7FjRGTy"), Import.ParseSpotifyLink("https://open.spotify.com/intl-de/album/4aawyAB9vmqN3uQ7FjRGTy"));
        Assert.Equal(new SpotifyRef("track", "4cOdK2wGLETKBW3PvgPWqT"), Import.ParseSpotifyLink("spotify:track:4cOdK2wGLETKBW3PvgPWqT"));
        Assert.Null(Import.ParseSpotifyLink("https://music.youtube.com/watch?v=x"));
        Assert.Equal(32, Import.ToGid("4cOdK2wGLETKBW3PvgPWqT").Length);
    }

    [Fact]
    public void ExportifyCsv()
    {
        const string csv = "\uFEFF\"Track URI\",\"Track Name\",\"Artist URI(s)\",\"Artist Name(s)\",\"Album Name\",\"Duration (ms)\"\n" +
                           "\"spotify:track:1\",\"Hello, World\",\"x\",\"Adele,Someone\",\"25\",\"295000\"\n" +
                           "\"spotify:track:2\",\"Numb\",\"y\",\"Linkin Park\",\"Meteora\",\"185000\"\n";
        var r = Import.ParseTrackList(csv);
        Assert.Equal(2, r.Tracks.Count);
        Assert.Equal("Hello, World", r.Tracks[0].Title);
        Assert.Equal(new[] { "Adele", "Someone" }, r.Tracks[0].Artists);
        Assert.Equal("25", r.Tracks[0].Album);
        Assert.Equal(295000, r.Tracks[0].DurationMs);
    }

    [Fact]
    public void PlainTextList()
    {
        var r = Import.ParseTrackList("1. Linkin Park - Numb\nAvicii – Wake Me Up\nBohemian Rhapsody");
        Assert.Equal(new[] { "Linkin Park", "Avicii" }, new[] { r.Tracks[0].Artists.FirstOrDefault(), r.Tracks[1].Artists.FirstOrDefault() });
        Assert.Equal("Numb", r.Tracks[0].Title);
        Assert.Equal("Wake Me Up", r.Tracks[1].Title);
        Assert.Empty(r.Tracks[2].Artists);
        Assert.Equal("Bohemian Rhapsody", r.Tracks[2].Title);
    }

    [Fact]
    public void ScoringPrefersRightArtistTitleAndDuration()
    {
        var want = new ImportTrack("Numb", new List<string> { "Linkin Park" }, "", 185000);
        var good = Import.ScoreCandidate(want, new MatchCandidate("g", "Numb", new[] { "Linkin Park" }, 186));
        var cover = Import.ScoreCandidate(want, new MatchCandidate("c", "Numb (Piano Cover)", new[] { "Some Pianist" }, 200));
        var other = Import.ScoreCandidate(want, new MatchCandidate("o", "Numb", new[] { "Marshmello" }, 150));
        Assert.True(good > 0.9, good.ToString());
        Assert.True(good > cover && good > other);

        var rem = Import.ScoreCandidate(
            new ImportTrack("Wonderwall - Remastered 2014", new List<string> { "Oasis" }, "", 258000),
            new MatchCandidate("r", "Wonderwall", new[] { "Oasis" }, 259));
        Assert.True(rem > 0.9, rem.ToString());
    }

    [Fact]
    public async Task MatchAll()
    {
        var db = new[]
        {
            new MatchCandidate("a", "Numb", new[] { "Linkin Park" }, 186),
            new MatchCandidate("b", "Wake Me Up", new[] { "Avicii" }, 247),
        };
        Task<IReadOnlyList<MatchCandidate>> Search(string q) =>
            Task.FromResult<IReadOnlyList<MatchCandidate>>(db.Where(x => q.ToLowerInvariant().Contains(x.Title.ToLowerInvariant())).ToList());
        var res = await Import.MatchAllAsync(new[]
        {
            new ImportTrack("Numb", new List<string> { "Linkin Park" }),
            new ImportTrack("Wake Me Up", new List<string> { "Avicii" }),
            new ImportTrack("Nothing Here", new List<string> { "Nobody" }),
        }, Search);
        Assert.Equal(new[] { "a", "b", null }, res.Select(r => r.Match?.Id));
    }

    [Fact]
    public async Task FetchSpotifyViaEmbedAndPaging()
    {
        static List<object> Mk(int n, int off = 0) => Enumerable.Range(0, n)
            .Select(i => (object)new { title = "T" + (i + off), subtitle = "A, B", duration = 1000, uri = "spotify:track:" + new string('x', 22) }).ToList();
        var state = new { data = new { entity = new { name = "Big", coverArt = new { sources = new[] { new { url = "c", width = 300 } } }, trackList = Mk(100) } }, settings = new { session = new { accessToken = "tok" } } };

        Task<(int, string)> Fetch(string url, IReadOnlyDictionary<string, string> headers)
        {
            if (url.Contains("/embed/"))
                return Task.FromResult((200, $"<html><script id=\"__NEXT_DATA__\" type=\"application/json\">{JsonSerializer.Serialize(new { props = new { pageProps = new { state } } })}</script>"));
            if (url.Contains("/playlist/v2/"))
            {
                var from = int.Parse(System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["from"]!);
                var items = from >= 130 ? Array.Empty<object>() : Enumerable.Range(0, 30).Select(_ => (object)new { uri = "spotify:track:4cOdK2wGLETKBW3PvgPWqT" }).ToArray();
                return Task.FromResult((200, JsonSerializer.Serialize(new { length = 130, contents = new { items } })));
            }
            if (url.Contains("/metadata/4/track/"))
                return Task.FromResult((200, JsonSerializer.Serialize(new { name = "Never Gonna Give You Up", artist = new[] { new { name = "Rick Astley" } }, duration = 213573, album = new { name = "Whenever" } })));
            return Task.FromResult((404, ""));
        }

        var r = await Import.FetchSpotifyAsync(Import.ParseSpotifyLink("https://open.spotify.com/playlist/37i9dQZF1DX4o1oenSJRJd")!, Fetch);
        Assert.Equal("Big", r.Name);
        Assert.Equal(130, r.Tracks.Count);
        Assert.Equal(new[] { "A", "B" }, r.Tracks[0].Artists);
        Assert.Equal("Never Gonna Give You Up", r.Tracks[129].Title);
    }
}
