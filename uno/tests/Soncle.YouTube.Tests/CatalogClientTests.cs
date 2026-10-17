// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Soncle.TestKit;
using Soncle.YouTube.InnerTube;
using Soncle.YouTube.Parsing;

namespace Soncle.YouTube.Tests;

/// <summary>
/// The catalog client against <c>fixtures/yt/expected/search-request.json</c>: the requests
/// youtubei.js 18.1.0 really builds (<c>tools/fixtures/export-search-request.mjs</c>).
/// </summary>
public class CatalogClientTests
{
    private static JsonElement Golden() =>
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("yt/expected/search-request.json"))).RootElement;

    private static string SearchAll() => File.ReadAllText(Fixtures.Path("yt/raw/search-all.json"));

    /// <summary>The fake visitor id the exporter used; fixtures never hold one, so the test puts it back.</summary>
    private const string Visitor = "TEST-VISITOR";

    /// <summary>The session youtubei.js generated, rebuilt from the recorded context.</summary>
    private static InnerTubeSession SessionFrom(JsonElement client)
    {
        string S(string name) => client.GetProperty(name).GetString()!;
        return new InnerTubeSession
        {
            Lang = S("hl"), Location = S("gl"), TimeZone = S("timeZone"),
            UtcOffsetMinutes = client.GetProperty("utcOffsetMinutes").GetInt32(),
            UserAgent = S("userAgent"), VisitorData = Visitor, RemoteHost = S("remoteHost"),
            OsName = S("osName"), OsVersion = S("osVersion"),
            BrowserName = S("browserName"), BrowserVersion = S("browserVersion"),
            DeviceMake = S("deviceMake"), DeviceModel = S("deviceModel"),
        };
    }

    [Fact]
    public void FilterParamsMatchYoutubeiJsForEveryType()
    {
        foreach (var r in Golden().GetProperty("requests").EnumerateArray())
        {
            var type = r.GetProperty("type").GetString()!;
            var expected = r.GetProperty("body").TryGetProperty("params", out var p) ? p.GetString() : null;
            Assert.Equal(expected, InnerTubeCatalogClient.SearchParams(type));
        }
    }

    [Fact]
    public void UnknownTypesHaveNoFilter() => Assert.Null(InnerTubeCatalogClient.SearchParams("mix"));

    [Fact]
    public async Task EveryRequestMatchesYoutubeiJs()
    {
        var golden = Golden();
        var checkedAny = 0;
        foreach (var r in golden.GetProperty("requests").EnumerateArray())
        {
            var query = r.GetProperty("query").GetString()!;
            var type = r.GetProperty("type").GetString()!;
            var session = SessionFrom(r.GetProperty("body").GetProperty("context").GetProperty("client"));
            var handler = new FakeHandler(_ => FakeHandler.Json(SearchAll()));
            var client = new InnerTubeCatalogClient(new HttpClient(handler), session);

            await client.SearchRawAsync(query, type);

            var seen = Assert.Single(handler.Requests);
            Assert.Equal(r.GetProperty("method").GetString(), seen.Method.Method);
            Assert.Equal(r.GetProperty("url").GetString(), seen.Url);
            // every header youtubei.js sets, with the same value (Content-Type is on the body here)
            // (FakeHandler lists a parsed User-Agent as comma-joined product tokens, so that one header
            // is compared without commas and spaces; on the wire the tokens are space-joined)
            static string Bare(string v) => string.Concat(v.Where(c => c is not (',' or ' ')));
            foreach (var h in r.GetProperty("headers").EnumerateObject().Where(h => h.Name != "content-type"))
            {
                var got = seen.Headers[h.Name];
                if (h.Name == "user-agent") Assert.Equal(Bare(h.Value.GetString()!), Bare(got));
                else Assert.Equal(h.Value.GetString(), got);
            }
            Assert.Equal(Visitor, seen.Headers["x-goog-visitor-id"]);
            var expectedBody = JsonNode.Parse(r.GetProperty("body").GetRawText())!.AsObject();
            expectedBody["context"]!["client"]!["visitorData"] = Visitor;
            JsonAssert.Equal(expectedBody.ToJsonString(), seen.Body);
            // the keys come in the same order too
            Assert.Equal(
                r.GetProperty("body").EnumerateObject().Select(x => x.Name),
                JsonDocument.Parse(seen.Body).RootElement.EnumerateObject().Select(x => x.Name));
            checkedAny++;
        }
        Assert.Equal(7, checkedAny);
    }

    [Fact]
    public async Task NeverSendsACookieOrCredentials()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(SearchAll()));
        var client = new InnerTubeCatalogClient(new HttpClient(handler), new InnerTubeSession { VisitorData = "v" });
        await client.SearchAsync("daft punk");
        var seen = Assert.Single(handler.Requests);
        Assert.DoesNotContain(seen.Headers.Keys, k => k.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(seen.Headers.Keys, k => k.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("SAPISID", seen.Body);
    }

    [Fact]
    public async Task SearchAsyncParsesTheRecordedAnswer()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(SearchAll()));
        var client = new InnerTubeCatalogClient(new HttpClient(handler), new InnerTubeSession());
        var page = await client.SearchAsync("daft punk");
        Assert.Equal("all", page.Type);
        Assert.False(page.Fallback);
        Assert.NotNull(page.Top);
        Assert.NotEmpty(page.Sections);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AFailingFilterIsRetriedAsAll()
    {
        // yt.mjs: `if (type !== 'all') { s = await y.music.search(q, { type: 'all' }); type = 'all'; }`
        var handler = new FakeHandler(r => r.Body.Contains("\"params\"")
            ? new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            : FakeHandler.Json(SearchAll()));
        var client = new InnerTubeCatalogClient(new HttpClient(handler), new InnerTubeSession());
        var page = await client.SearchAsync("daft punk", "song");
        Assert.Equal("all", page.Type);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("\"params\"", handler.Requests[0].Body);
        Assert.DoesNotContain("\"params\"", handler.Requests[1].Body);
    }

    // some filters answer with an empty Message section instead of failing
    private const string EmptyAnswer = """{"contents":{}}""";

    private static FakeHandler EmptyWhenFilteredElseAll() => new(r => r.Body.Contains("\"params\"")
        ? FakeHandler.Json(EmptyAnswer)
        : FakeHandler.Json(SearchAll()));

    [Fact]
    public async Task AnEmptyFilteredAnswerFallsBackToAllAndFiltersHere()
    {
        var handler = EmptyWhenFilteredElseAll();
        var client = new InnerTubeCatalogClient(new HttpClient(handler), new InnerTubeSession());
        var page = await client.SearchAsync("daft punk", "song");

        // exactly what the pure parser makes of the same two responses (the JS filters the 'all' page)
        var expected = Pages.Search(JsonDocument.Parse(EmptyAnswer).RootElement, "daft punk", "song",
            _ => JsonDocument.Parse(SearchAll()).RootElement);
        Assert.True(page.Fallback);
        Assert.Equal("song", page.RequestedType);
        Assert.Equal("song", page.Type);
        Assert.Equal(expected.Sections.Sum(x => x.Items.Count), page.Sections.Sum(x => x.Items.Count));
        Assert.All(page.Sections.SelectMany(x => x.Items), i => Assert.True(i.Type == "song" && !i.IsVideo));
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("\"params\"", handler.Requests[1].Body);
    }

    [Fact]
    public async Task AnEmptyArtistFilterKeepsTheWholeAllPage()
    {
        // only song and album are filtered client-side; any other type shows the full 'all' page
        var client = new InnerTubeCatalogClient(new HttpClient(EmptyWhenFilteredElseAll()), new InnerTubeSession());
        var page = await client.SearchAsync("daft punk", "artist");
        Assert.True(page.Fallback);
        Assert.Equal("artist", page.RequestedType);
        Assert.Equal("all", page.Type);
        Assert.NotNull(page.Top);
        Assert.NotEmpty(page.Sections.SelectMany(x => x.Items));
    }

    [Fact]
    public async Task AFailingAllSearchThrows()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
        var client = new InnerTubeCatalogClient(new HttpClient(handler), new InnerTubeSession());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SearchAsync("x"));
        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task ACancelledSearchIsNotRetriedAsAll()
    {
        // a typed search that is cancelled must not fall back to 'all' (only real failures do)
        var handler = new FakeHandler(_ => throw new OperationCanceledException());
        var client = new InnerTubeCatalogClient(new HttpClient(handler), new InnerTubeSession());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchAsync("x", "song"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ABlankQueryIsRefused()
    {
        var client = new InnerTubeCatalogClient(new HttpClient(new FakeHandler(_ => FakeHandler.Json("{}"))), new InnerTubeSession());
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchRawAsync("  ", "all"));
    }
}
