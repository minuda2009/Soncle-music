// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Soncle.YouTube.InnerTube;

/// <summary>
/// The real <see cref="IStreamClient"/>: asks InnerTube's <c>/player</c> for a song as one of the
/// stream clients, and does the two range probes the resolver needs. It is anonymous (like
/// <c>ytAnon()</c> in yt.mjs, stream resolution never sends a cookie). It takes an
/// <see cref="HttpClient"/>, so tests replay recorded responses and never touch the network.
/// A format is only offered if it has a playable URL: a ciphered one needs an
/// <see cref="IPlayerScript"/> from a head.
/// </summary>
public sealed class InnerTubeStreamClient : IStreamClient
{
    private const string PlayerUrl = "https://www.youtube.com/youtubei/v1/player?prettyPrint=false&alt=json";
    private const long MidProbe = 1_500_000;

    private readonly HttpClient _http;
    private readonly InnerTubeSession _session;
    private readonly IPlayerScript? _script;

    public InnerTubeStreamClient(HttpClient http, InnerTubeSession session, IPlayerScript? script = null)
    {
        _http = http;
        _session = session;
        _script = script;
    }

    public string UserAgent => _session.UserAgent;

    /// <summary>The User-Agent googlevideo requests carry for a client (<c>uaFor</c> in yt.mjs).</summary>
    public string UserAgentFor(string client) => StreamUserAgent(client);

    /// <summary>Per client, with TV's Cobalt string; everything else looks like desktop Chrome.</summary>
    internal static string StreamUserAgent(string client) => client switch
    {
        "ANDROID_VR" => ClientProfile.AndroidVrUa,
        "IOS" => ClientProfile.IosUa,
        "TV" => ClientProfile.TvUa,
        _ => ClientProfile.DesktopUserAgent,
    };

    // ---------- /player ----------
    public async Task<PlayerResponse> GetPlayerAsync(string videoId, string client, string? potToken, CancellationToken ct)
    {
        if (!ClientProfile.Streaming.TryGetValue(client, out var profile))
            throw new InvalidOperationException($"unknown stream client '{client}'");

        var context = _session.BuildContext();
        profile.Apply(context);
        var body = BuildPlayerBody(videoId, context, potToken, _script?.SignatureTimestamp);

        using var req = new HttpRequestMessage(HttpMethod.Post, PlayerUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        ApplyHeaders(req, profile);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"InnerTube player request failed with status code {(int)res.StatusCode}");
        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return await ParsePlayerAsync(doc.RootElement, _script, ct).ConfigureAwait(false);
    }

    internal static JsonObject BuildPlayerBody(string videoId, JsonObject context, string? potToken, int? signatureTimestamp)
    {
        // Same keys as youtubei.js getBasicInfo → WatchEndpoint.buildRequest + extra payload.
        var cpc = new JsonObject { ["vis"] = 0, ["splay"] = false, ["lactMilliseconds"] = "-1" };
        if (signatureTimestamp is { } st) cpc["signatureTimestamp"] = st;
        var body = new JsonObject
        {
            ["videoId"] = videoId,
            ["racyCheckOk"] = true,
            ["contentCheckOk"] = true,
            ["playbackContext"] = new JsonObject { ["contentPlaybackContext"] = cpc },
            ["context"] = context,
        };
        if (!string.IsNullOrEmpty(potToken))
            body["serviceIntegrityDimensions"] = new JsonObject { ["poToken"] = potToken };
        return body;
    }

    private void ApplyHeaders(HttpRequestMessage req, ClientProfile profile)
    {
        var h = req.Headers;
        h.TryAddWithoutValidation("Accept", "*/*");
        h.TryAddWithoutValidation("Accept-Language", "*");
        h.TryAddWithoutValidation("X-Goog-Visitor-Id", _session.VisitorData);
        h.TryAddWithoutValidation("X-Youtube-Client-Version", profile.Version);
        h.TryAddWithoutValidation("X-Youtube-Client-Name", profile.NameId);
        h.TryAddWithoutValidation("Origin", "https://www.youtube.com");
        // iOS and Android VR identify themselves in the header too (youtubei.js HTTPClient)
        var ua = profile.Spec is "IOS" or "ANDROID_VR" ? profile.UserAgent! : _session.UserAgent;
        h.TryAddWithoutValidation("User-Agent", ua);
    }

    // ---------- response → formats ----------
    internal static async Task<PlayerResponse> ParsePlayerAsync(JsonElement root, IPlayerScript? script, CancellationToken ct)
    {
        string status = "", reason = "";
        if (root.TryGetProperty("playabilityStatus", out var ps))
        {
            status = Str(ps, "status") ?? "";
            reason = Str(ps, "reason") ?? "";
        }
        string? loudness = null;
        if (root.TryGetProperty("playerConfig", out var pc) && pc.TryGetProperty("audioConfig", out var ac) &&
            ac.TryGetProperty("loudnessDb", out var ld) && ld.ValueKind == JsonValueKind.Number)
            loudness = ld.GetDouble().ToString("R", CultureInfo.InvariantCulture);

        var formats = new List<AudioFormat>();
        if (root.TryGetProperty("streamingData", out var sd) && sd.TryGetProperty("adaptiveFormats", out var af) && af.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in af.EnumerateArray())
            {
                // audio only: has_audio = audioBitrate || audioQuality; has_video = qualityLabel
                var hasAudio = Truthy(f, "audioBitrate") || Truthy(f, "audioQuality");
                var hasVideo = Truthy(f, "qualityLabel");
                if (!hasAudio || hasVideo) continue;

                var url = Str(f, "url");
                var cipher = Str(f, "signatureCipher") ?? Str(f, "cipher");
                string? finalUrl;
                if (script is not null) finalUrl = await script.DecipherAsync(url, cipher, ct).ConfigureAwait(false);
                else finalUrl = url;                       // only plain URLs are playable without a player script
                if (string.IsNullOrEmpty(finalUrl)) continue;

                var xtags = XTags.Decode(Str(f, "xtags"));
                var isDrc = (f.TryGetProperty("isDrc", out var d) && d.ValueKind == JsonValueKind.True) || XTags.Has(xtags, "drc", "1");
                var isDefault = true;
                if (f.TryGetProperty("audioTrack", out var at) && at.TryGetProperty("audioIsDefault", out var ad) && ad.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    isDefault = ad.GetBoolean();
                long.TryParse(Str(f, "contentLength"), NumberStyles.None, CultureInfo.InvariantCulture, out var len);
                var bitrate = f.TryGetProperty("bitrate", out var br) && br.ValueKind == JsonValueKind.Number ? br.GetInt32() : 0;
                formats.Add(new AudioFormat(finalUrl, Str(f, "mimeType") ?? "", bitrate, len, isDrc, isDefault, loudness));
            }
        }
        return new PlayerResponse(status, string.IsNullOrEmpty(reason) ? null : reason, formats);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Truthy(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.GetDouble() != 0,
            JsonValueKind.String => v.GetString()!.Length > 0,
            _ => false,
        };
    }

    // ---------- probes (yt.mjs clientCanSeek / probeLength) ----------
    public async Task<string?> CanSeekAsync(string client, string url, IReadOnlyDictionary<string, string> headers, long length, CancellationToken ct)
    {
        // a mid-file chunk, not just the first bytes: some origins reject ranges past a small ceiling
        var start = length > MidProbe + 100_000 ? MidProbe : (length > 200_000 ? length / 2 : 0);
        var end = start + 100_000;
        using var req = new HttpRequestMessage(HttpMethod.Get, url + $"&range={start}-{end}");
        foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return $"HTTP {(int)res.StatusCode}";
        try
        {
            await using var s = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buf = new byte[8192];
            long total = 0; int n;
            while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0) total += n;
            return total > 0 ? null : "empty body";
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public async Task<long> ProbeLengthAsync(string client, string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var total = res.Content.Headers.ContentRange?.Length;
            return total ?? 0;
        }
        catch (OperationCanceledException) { throw; }
        catch { return 0; }
    }
}
