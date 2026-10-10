// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soncle.TestKit;
using Soncle.YouTube;
using Soncle.YouTube.InnerTube;

namespace Soncle.YouTube.Tests;

/// <summary>A handler that answers from a delegate and records every request (including its body).</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public sealed record Seen(HttpMethod Method, string Url, Dictionary<string, string> Headers, string Body, string? Range);
    public readonly List<Seen> Requests = new();
    private readonly Func<Seen, HttpResponseMessage> _answer;
    public FakeHandler(Func<Seen, HttpResponseMessage> answer) => _answer = answer;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers) headers[h.Key] = string.Join(",", h.Value);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var seen = new Seen(request.Method, request.RequestUri!.ToString(), headers, body, request.Headers.Range?.ToString());
        Requests.Add(seen);
        return _answer(seen);
    }

    public static HttpResponseMessage Json(string json) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class SidAuthTests
{
    // sha1("1700000000 abcDEF123_-sapisid https://www.youtube.com"), computed independently
    [Fact]
    public void MatchesTheYoutubeiJsAlgorithm()
    {
        Assert.Equal("SAPISIDHASH 1700000000_4cc8c93ace369bf73b7a766539e3e2a29671a85f", SidAuth.Header("abcDEF123_-sapisid", 1700000000));
        Assert.Equal("SAPISIDHASH 1234567890_905d1d6914f5122c864d23a71fa746de26feaa97", SidAuth.Header("x", 1234567890));
    }

    [Fact]
    public void ReadsOneCookieAndNeverTheWrongOne()
    {
        const string cookie = "A=1; SAPISID=secret-value; __Secure-3PAPISID=other; B=2";
        Assert.Equal("secret-value", SidAuth.CookieValue(cookie, "SAPISID"));
        Assert.Equal("other", SidAuth.CookieValue(cookie, "__Secure-3PAPISID"));
        Assert.Null(SidAuth.CookieValue(cookie, "SID"));
        Assert.Null(SidAuth.CookieValue(null, "SAPISID"));
    }

    [Fact]
    public void RefusesAnEmptySapisid() => Assert.Throws<ArgumentException>(() => SidAuth.Header("", 1));
}

public class XTagsTests
{
    private static string Encode(params (string K, string V)[] tags)
    {
        var all = new List<byte>();
        foreach (var (k, v) in tags)
        {
            var inner = new List<byte> { 0x0A, (byte)k.Length };
            inner.AddRange(Encoding.UTF8.GetBytes(k));
            inner.Add(0x12); inner.Add((byte)v.Length);
            inner.AddRange(Encoding.UTF8.GetBytes(v));
            all.Add(0x0A); all.Add((byte)inner.Count);
            all.AddRange(inner);
        }
        return Convert.ToBase64String(all.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public void DecodesKeyValuePairs()
    {
        var t = XTags.Decode(Encode(("drc", "1"), ("lang", "en")));
        Assert.True(XTags.Has(t, "drc", "1"));
        Assert.True(XTags.Has(t, "lang", "en"));
        Assert.False(XTags.Has(t, "drc", "0"));
    }

    [Fact]
    public void GarbageGivesNoTags()
    {
        Assert.Empty(XTags.Decode(null));
        Assert.Empty(XTags.Decode("!!!not base64!!!"));
        Assert.Empty(XTags.Decode("CgN"));
    }
}

public class InnerTubeSessionTests
{
    private const string Jspb = ")]}'\n[[null,null,[[\"en\",\"LK\",null,\"1.2.3.4\",null,null,null,null,null,null,null,\"\",\"\",\"VISITOR123\",null,null,\"2.20260623.01.00\",\"Windows\",\"10.0\"]," +
        "\"API_KEY\"]]]";

    [Fact]
    public void ParsesVisitorDataFromSwJsData()
    {
        // build a device_info array long enough to hold the fields we read
        var d = new JsonArray();
        for (var i = 0; i < 110; i++) d.Add(null);
        d[0] = "en"; d[1] = "LK"; d[3] = "1.2.3.4"; d[11] = ""; d[12] = ""; d[13] = "VISITOR123"; d[16] = "2.2026"; d[17] = "Windows"; d[18] = "10.0";
        d[79] = "Asia/Colombo"; d[86] = "Chrome"; d[87] = "140.0.0.0";
        var cfg = new JsonArray { "x", "APPINSTALL" }; d[61] = cfg;
        var text = ")]}'\n" + new JsonArray(new JsonArray(null, null, new JsonArray(new JsonArray(d), "KEY"))).ToJsonString();
        var s = InnerTubeSession.ParseSwJsData(text, new InnerTubeSession { Lang = "", Location = "" });
        Assert.NotNull(s);
        Assert.Equal("VISITOR123", s!.VisitorData);
        Assert.Equal("Asia/Colombo", s.TimeZone);
        Assert.Equal("APPINSTALL", s.AppInstallData);
        Assert.True(s.FromServer);
    }

    [Fact]
    public void AnUnrecognisedBodyIsNotASession()
    {
        Assert.Null(InnerTubeSession.ParseSwJsData("<html>", new InnerTubeSession()));
        Assert.Null(InnerTubeSession.ParseSwJsData(")]}'\n[1,2]", new InnerTubeSession()));
    }

    [Fact]
    public async Task AnUnreachableServerStillGivesAUsableSession()
    {
        using var http = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("offline")));
        var s = await InnerTubeSession.CreateAsync(http, "en", "US", "UTC");
        Assert.False(s.FromServer);
        Assert.Equal("", s.VisitorData);
        Assert.Equal("en", s.Lang);
    }

    [Fact]
    public async Task ASessionRequestCarriesNoCookieButTheThrowawayVisitorId()
    {
        var h = new FakeHandler(_ => FakeHandler.Json("nope"));
        using var http = new HttpClient(h);
        await InnerTubeSession.CreateAsync(http, "en", "US", "Asia/Colombo");
        var cookie = h.Requests[0].Headers["Cookie"];
        Assert.StartsWith("PREF=tz=Asia.Colombo;VISITOR_INFO1_LIVE=", cookie);
        Assert.DoesNotContain("SAPISID", cookie);
    }

    [Fact]
    public void TheBaseContextHasTheWebShape()
    {
        var c = new InnerTubeSession { VisitorData = "V", Lang = "si", Location = "LK", TimeZone = "Asia/Colombo", UtcOffsetMinutes = 330 }.BuildContext();
        var client = c["client"]!;
        Assert.Equal("si", (string)client["hl"]!);
        Assert.Equal("V", (string)client["visitorData"]!);
        Assert.Equal(330, (int)client["utcOffsetMinutes"]!);
        Assert.Null(client["configInfo"]);
        Assert.NotNull(c["user"]);
        Assert.NotNull(c["request"]);
    }
}

public class InnerTubeStreamClientTests
{
    private static InnerTubeSession Session() => new() { VisitorData = "VISITOR", AppInstallData = "APPDATA" };

    private static (InnerTubeStreamClient Client, FakeHandler Handler) Make(Func<FakeHandler.Seen, HttpResponseMessage> answer, IPlayerScript? script = null)
    {
        var h = new FakeHandler(answer);
        return (new InnerTubeStreamClient(new HttpClient(h), Session(), script), h);
    }

    private const string TwoFormats = """
        {"playabilityStatus":{"status":"OK"},
         "playerConfig":{"audioConfig":{"loudnessDb":-3.5}},
         "streamingData":{"adaptiveFormats":[
           {"itag":251,"mimeType":"audio/webm; codecs=\"opus\"","bitrate":160000,"audioQuality":"AUDIO_QUALITY_MEDIUM","contentLength":"3456789","url":"https://gv/a?expire=1"},
           {"itag":140,"mimeType":"audio/mp4; codecs=\"mp4a.40.2\"","bitrate":130000,"audioBitrate":130,"signatureCipher":"s=AAA&sp=sig&url=https%3A%2F%2Fgv%2Fb"},
           {"itag":137,"mimeType":"video/mp4","bitrate":4000000,"qualityLabel":"1080p","url":"https://gv/v"},
           {"itag":251,"mimeType":"audio/webm; codecs=\"opus\"","bitrate":120000,"audioQuality":"AUDIO_QUALITY_LOW","url":"https://gv/drc","isDrc":true},
           {"itag":251,"mimeType":"audio/webm; codecs=\"opus\"","bitrate":110000,"audioQuality":"AUDIO_QUALITY_LOW","url":"https://gv/dub","audioTrack":{"audioIsDefault":false}}
         ]}}
        """;

    [Fact]
    public async Task AndroidVrRequestMatchesWhatYoutubeiJsSends()
    {
        var (c, h) = Make(_ => FakeHandler.Json(TwoFormats));
        await c.GetPlayerAsync("vid123", "ANDROID_VR", null, default);
        var r = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("https://www.youtube.com/youtubei/v1/player?prettyPrint=false&alt=json", r.Url);
        Assert.Equal("28", r.Headers["X-Youtube-Client-Name"]);
        Assert.Equal("1.65.10", r.Headers["X-Youtube-Client-Version"]);
        Assert.Equal("VISITOR", r.Headers["X-Goog-Visitor-Id"]);
        Assert.StartsWith("com.google.android.apps.youtube.vr.oculus/1.65.10", r.Headers["User-Agent"]);
        Assert.DoesNotContain("Cookie", r.Headers.Keys);
        Assert.DoesNotContain("Authorization", r.Headers.Keys);

        var b = JsonNode.Parse(r.Body)!;
        Assert.Equal("vid123", (string)b["videoId"]!);
        Assert.True((bool)b["racyCheckOk"]!);
        Assert.True((bool)b["contentCheckOk"]!);
        var cpc = b["playbackContext"]!["contentPlaybackContext"]!;
        Assert.Equal("-1", (string)cpc["lactMilliseconds"]!);
        Assert.Null(cpc["signatureTimestamp"]);
        Assert.Null(b["serviceIntegrityDimensions"]);
        var cl = b["context"]!["client"]!;
        Assert.Equal("ANDROID_VR", (string)cl["clientName"]!);
        Assert.Equal("1.65.10", (string)cl["clientVersion"]!);
        Assert.Equal("12L", (string)cl["osVersion"]!);
        Assert.Equal(32, (int)cl["androidSdkVersion"]!);
        Assert.Equal("Quest 3", (string)cl["deviceModel"]!);
        Assert.Null(cl["configInfo"]);      // only the web client carries it
    }

    [Fact]
    public async Task IosDropsTheBrowserFieldsAndUsesItsOwnUserAgent()
    {
        var (c, h) = Make(_ => FakeHandler.Json(TwoFormats));
        await c.GetPlayerAsync("v", "IOS", null, default);
        var cl = JsonNode.Parse(h.Requests[0].Body)!["context"]!["client"]!;
        Assert.Equal("iOS", (string)cl["clientName"]!);
        Assert.Equal("20.11.6", (string)cl["clientVersion"]!);
        Assert.Equal("16.7.7.20H330", (string)cl["osVersion"]!);
        Assert.Null(cl["browserName"]);
        Assert.Null(cl["browserVersion"]);
        Assert.Equal("5", h.Requests[0].Headers["X-Youtube-Client-Name"]);
        Assert.StartsWith("com.google.ios.youtube/20.11.6", h.Requests[0].Headers["User-Agent"]);
    }

    [Fact]
    public async Task EmbeddedClientSaysItIsEmbedded()
    {
        var (c, h) = Make(_ => FakeHandler.Json(TwoFormats));
        await c.GetPlayerAsync("v", "WEB_EMBEDDED", null, default);
        var ctx = JsonNode.Parse(h.Requests[0].Body)!["context"]!;
        Assert.Equal("WEB_EMBEDDED_PLAYER", (string)ctx["client"]!["clientName"]!);
        Assert.Equal("EMBED", (string)ctx["client"]!["clientScreen"]!);
        Assert.Equal("https://www.google.com/", (string)ctx["thirdParty"]!["embedUrl"]!);
    }

    [Fact]
    public async Task TheMusicClientIsWebRemixAndKeepsItsConfig()
    {
        var (c, h) = Make(_ => FakeHandler.Json(TwoFormats));
        await c.GetPlayerAsync("v", "YTMUSIC", "POT-A", default);
        var b = JsonNode.Parse(h.Requests[0].Body)!;
        Assert.Equal("WEB_REMIX", (string)b["context"]!["client"]!["clientName"]!);
        Assert.Equal("67", h.Requests[0].Headers["X-Youtube-Client-Name"]);
        Assert.Equal("POT-A", (string)b["serviceIntegrityDimensions"]!["poToken"]!);
        // configInfo is dropped for everything but WEB
        Assert.Null(b["context"]!["client"]!["configInfo"]);
    }

    [Fact]
    public async Task WebKeepsTheConfigAndTheScriptsSignatureTimestamp()
    {
        var (c, h) = Make(_ => FakeHandler.Json(TwoFormats), new FakeScript { Timestamp = 20373 });
        await c.GetPlayerAsync("v", "WEB", null, default);
        var b = JsonNode.Parse(h.Requests[0].Body)!;
        Assert.Equal("APPDATA", (string)b["context"]!["client"]!["configInfo"]!["appInstallData"]!);
        Assert.Equal(20373, (int)b["playbackContext"]!["contentPlaybackContext"]!["signatureTimestamp"]!);
    }

    [Fact]
    public async Task OnlyAudioWithAPlayableUrlIsOffered()
    {
        var (c, _) = Make(_ => FakeHandler.Json(TwoFormats));
        var p = await c.GetPlayerAsync("v", "IOS", null, default);
        Assert.Equal("OK", p.Status);
        // the ciphered format (no script) and the video format are gone
        Assert.Equal(3, p.AudioFormats.Count);
        Assert.All(p.AudioFormats, f => Assert.StartsWith("https://gv/", f.Url));
        Assert.DoesNotContain(p.AudioFormats, f => f.Url == "https://gv/v");
        var main = p.AudioFormats[0];
        Assert.Equal(160000, main.Bitrate);
        Assert.Equal(3456789, main.ContentLength);
        Assert.Equal("-3.5", main.LoudnessDb);
        Assert.False(main.IsDrc);
        Assert.True(main.IsDefaultAudioTrack);
        Assert.True(p.AudioFormats.Single(f => f.Url.EndsWith("/drc")).IsDrc);
        Assert.False(p.AudioFormats.Single(f => f.Url.EndsWith("/dub")).IsDefaultAudioTrack);
    }

    [Fact]
    public async Task ADrcFlagInXtagsIsRecognised()
    {
        var xt = "CggKA2RyYxIBMQ";   // { key: "drc", value: "1" }
        var json = "{\"playabilityStatus\":{\"status\":\"OK\"},\"streamingData\":{\"adaptiveFormats\":[{\"mimeType\":\"audio/webm\",\"bitrate\":1,\"audioQuality\":\"X\",\"url\":\"https://gv/x\",\"xtags\":\"" + xt + "\"}]}}";
        var (c, _) = Make(_ => FakeHandler.Json(json));
        var p = await c.GetPlayerAsync("v", "IOS", null, default);
        Assert.True(Assert.Single(p.AudioFormats).IsDrc);
    }

    [Fact]
    public async Task APlayerScriptDecipheresEveryFormat()
    {
        var script = new FakeScript { Decipher = (url, cipher) => url ?? (cipher is null ? null : "https://gv/deciphered") };
        var (c, _) = Make(_ => FakeHandler.Json(TwoFormats), script);
        var p = await c.GetPlayerAsync("v", "WEB", null, default);
        Assert.Contains(p.AudioFormats, f => f.Url == "https://gv/deciphered");
        Assert.Equal(4, p.AudioFormats.Count);
    }

    [Fact]
    public async Task TheRecordedLoginRequiredAnswerSurfacesTheReason()
    {
        var recorded = File.ReadAllText(Fixtures.Path("yt/raw/player-android-vr.json"));
        var (c, _) = Make(_ => FakeHandler.Json(recorded));
        var p = await c.GetPlayerAsync("v", "ANDROID_VR", null, default);
        Assert.Equal("LOGIN_REQUIRED", p.Status);
        Assert.Contains("Sign in to confirm", p.Reason);
        Assert.Empty(p.AudioFormats);
    }

    [Fact]
    public async Task AServerErrorIsAnError()
    {
        var (c, _) = Make(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => c.GetPlayerAsync("v", "IOS", null, default));
        Assert.Contains("400", e.Message);
    }

    [Fact]
    public async Task AnUnknownClientIsRefusedBeforeAnyRequest()
    {
        var (c, h) = Make(_ => FakeHandler.Json("{}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.GetPlayerAsync("v", "NOPE", null, default));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public void StreamUserAgentsFollowUaFor()
    {
        var (c, _) = Make(_ => FakeHandler.Json("{}"));
        Assert.StartsWith("com.google.ios.youtube/", c.UserAgentFor("IOS"));
        Assert.StartsWith("com.google.android.apps.youtube.vr.oculus/", c.UserAgentFor("ANDROID_VR"));
        Assert.Equal("Mozilla/5.0 (ChromiumStylePlatform) Cobalt/Version", c.UserAgentFor("TV"));
        Assert.Contains("Chrome/140", c.UserAgentFor("WEB"));
    }

    // ---------- probes ----------
    [Fact]
    public async Task TheSeekProbeAsksForAMidFileRange()
    {
        var (c, h) = Make(_ => new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[100]) });
        var hdr = new Dictionary<string, string> { ["User-Agent"] = "ua" };
        Assert.Null(await c.CanSeekAsync("IOS", "https://gv/a?x=1", hdr, 5_000_000, default));
        Assert.Equal("https://gv/a?x=1&range=1500000-1600000", h.Requests[0].Url);
        Assert.Null(await c.CanSeekAsync("IOS", "https://gv/a?x=1", hdr, 1_000_000, default));
        Assert.Equal("https://gv/a?x=1&range=500000-600000", h.Requests[1].Url);
        Assert.Null(await c.CanSeekAsync("IOS", "https://gv/a?x=1", hdr, 100_000, default));
        Assert.Equal("https://gv/a?x=1&range=0-100000", h.Requests[2].Url);
        Assert.Equal("ua", h.Requests[0].Headers["User-Agent"]);
    }

    [Fact]
    public async Task TheSeekProbeReportsHttpErrorsAndEmptyBodies()
    {
        var hdr = new Dictionary<string, string>();
        var (c403, _) = Make(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
        Assert.Equal("HTTP 403", await c403.CanSeekAsync("IOS", "https://gv/a?x=1", hdr, 5_000_000, default));
        var (cEmpty, _) = Make(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) });
        Assert.Equal("empty body", await cEmpty.CanSeekAsync("IOS", "https://gv/a?x=1", hdr, 5_000_000, default));
    }

    [Fact]
    public async Task TheLengthProbeReadsTheTotalFromContentRange()
    {
        var (c, h) = Make(_ =>
        {
            var r = new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[1]) };
            r.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 0, 4_321_000);
            return r;
        });
        Assert.Equal(4_321_000, await c.ProbeLengthAsync("IOS", "https://gv/a", new Dictionary<string, string>(), default));
        Assert.Equal("bytes=0-0", h.Requests[0].Range);
        var (bad, _) = Make(_ => throw new HttpRequestException("x"));
        Assert.Equal(0, await bad.ProbeLengthAsync("IOS", "https://gv/a", new Dictionary<string, string>(), default));
    }

    // ---------- with the resolver ----------
    [Fact]
    public async Task TheResolverUsesThePerClientUserAgentAndFallsBackWhenARangeIsRefused()
    {
        var (c, h) = Make(s =>
        {
            if (s.Url.Contains("/player"))
            {
                var client = (string)JsonNode.Parse(s.Body)!["context"]!["client"]!["clientName"]!;
                var url = client == "iOS" ? "https://gv/ios?clen=3000000&expire=4102444800" : "https://gv/vr?clen=3000000&expire=4102444800";
                return FakeHandler.Json("{\"playabilityStatus\":{\"status\":\"OK\"},\"streamingData\":{\"adaptiveFormats\":[{\"mimeType\":\"audio/webm\",\"bitrate\":128000,\"audioQuality\":\"X\",\"url\":\"" + url + "\"}]}}");
            }
            // the iOS stream refuses a mid-file range; Android VR serves it
            return s.Url.StartsWith("https://gv/ios") ? new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
                : new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[100]) };
        });
        var r = new StreamResolver(c);
        r.SetClients(new[] { "IOS", "ANDROID_VR" });
        var s = await r.ResolveAsync("v");
        Assert.Equal("ANDROID_VR", s.Client);
        Assert.StartsWith("com.google.android.apps.youtube.vr.oculus/", s.UserAgent);
        Assert.Equal(s.UserAgent, s.Headers["User-Agent"]);
        Assert.Equal(3_000_000, s.Length);
        Assert.Contains(h.Requests, q => q.Url.Contains("/ios") && q.Url.Contains("&range="));
    }

    private sealed class FakeScript : IPlayerScript
    {
        public int? Timestamp { get; init; }
        public Func<string?, string?, string?>? Decipher { get; init; }
        public int? SignatureTimestamp => Timestamp;
        public Task<string?> DecipherAsync(string? url, string? cipher, CancellationToken ct) =>
            Task.FromResult(Decipher is null ? url : Decipher(url, cipher));
    }
}
