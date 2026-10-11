// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soncle.YouTube.Parsing;

namespace Soncle.YouTube.InnerTube;

/// <summary>
/// The catalog side of InnerTube, anonymous: today only <c>search</c>. A port of what the JS app
/// gets from youtubei.js 18.1.0's <c>music.search(query, { type })</c> plus the two fallbacks in
/// <c>yt.mjs</c>'s <c>search()</c>. The request is the one youtubei.js sends, byte for byte in
/// meaning (see <c>fixtures/yt/expected/search-request.json</c>): the YouTube Music client
/// (<c>WEB_REMIX</c>) posted to <c>www.youtube.com/youtubei/v1/search</c>. No cookie is read or
/// sent; signed-in catalog calls (library, likes) come with the head's sign-in.
/// </summary>
public sealed class InnerTubeCatalogClient
{
    private const string SearchUrl = "https://www.youtube.com/youtubei/v1/search?prettyPrint=false&alt=json";

    // type:'song' and type:'album' are rejected by the YouTube Music API on some responses, so an
    // unknown type becomes 'all' (yt.mjs SAFE_SEARCH_TYPES).
    private static readonly HashSet<string> SafeSearchTypes = new() { "all", "song", "video", "album", "artist", "playlist" };

    private readonly HttpClient _http;
    private readonly InnerTubeSession _session;

    public InnerTubeCatalogClient(HttpClient http, InnerTubeSession session)
    {
        _http = http;
        _session = session;
    }

    /// <summary>
    /// youtubei.js' <c>SearchFilter</c> protobuf for a typed music search, base64 and then
    /// <c>encodeURIComponent</c>ed (that is how it travels inside the JSON body). Null for 'all'.
    /// <code>SearchFilter { filters(2) { musicSearchType(17) { song(1) video(2) album(3) artist(4) playlist(5) : bool } } }</code>
    /// </summary>
    public static string? SearchParams(string type)
    {
        var field = type switch { "song" => 1, "video" => 2, "album" => 3, "artist" => 4, "playlist" => 5, _ => 0 };
        if (field == 0) return null;
        // musicSearchType: one bool field = tag (field << 3 | varint) then 1
        var music = new byte[] { (byte)(field << 3), 1 };
        // filters: field 17, length-delimited => key 138 = 0x8A 0x01
        var filters = new List<byte> { 0x8A, 0x01, (byte)music.Length };
        filters.AddRange(music);
        // SearchFilter: field 2, length-delimited => key 0x12
        var msg = new List<byte> { 0x12, (byte)filters.Count };
        msg.AddRange(filters);
        return Uri.EscapeDataString(Convert.ToBase64String(msg.ToArray()));
    }

    /// <summary>The body youtubei.js sends: <c>query</c>, <c>params</c> (typed only), <c>isAudioOnly</c>, then <c>context</c>.</summary>
    internal static JsonObject BuildSearchBody(string query, string type, JsonObject context)
    {
        var body = new JsonObject { ["query"] = query };
        if (SearchParams(type) is { } p) body["params"] = p;
        body["isAudioOnly"] = true;   // Actions.execute sets it for every YTMUSIC request
        body["context"] = context;
        return body;
    }

    /// <summary>One <c>/search</c> call; returns the raw response for <see cref="Pages.Search"/>.</summary>
    public async Task<JsonElement> SearchRawAsync(string query, string type, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("query is required", nameof(query));
        var profile = ClientProfile.Streaming["YTMUSIC"];
        var context = _session.BuildContext();
        profile.Apply(context);
        var body = BuildSearchBody(query, type, context);

        using var req = new HttpRequestMessage(HttpMethod.Post, SearchUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        var h = req.Headers;
        h.TryAddWithoutValidation("Accept", "*/*");
        h.TryAddWithoutValidation("Accept-Language", "*");
        h.TryAddWithoutValidation("X-Goog-Visitor-Id", _session.VisitorData);
        h.TryAddWithoutValidation("X-Youtube-Client-Version", profile.Version);
        h.TryAddWithoutValidation("X-Youtube-Client-Name", profile.NameId);
        h.TryAddWithoutValidation("Origin", "https://www.youtube.com");
        h.TryAddWithoutValidation("User-Agent", _session.UserAgent);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"InnerTube search request failed with status code {(int)res.StatusCode}");
        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// yt.mjs <c>search(q, type)</c>. If a typed search fails it is repeated as 'all'; if a typed
    /// search comes back empty (some filters answer with an empty Message section) 'all' is fetched
    /// and filtered here. Both fallbacks mark the page (<c>Type</c>, <c>RequestedType</c>, <c>Fallback</c>).
    /// </summary>
    public async Task<YtSearchPage> SearchAsync(string query, string type = "all", CancellationToken ct = default)
    {
        if (!SafeSearchTypes.Contains(type)) type = "all";
        JsonElement raw;
        try
        {
            raw = await SearchRawAsync(query, type, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (type != "all" && e is not OperationCanceledException)
        {
            raw = await SearchRawAsync(query, "all", ct).ConfigureAwait(false);
            type = "all";
        }

        var page = Pages.Search(raw, query, type);
        if (page.Fallback)
        {
            // empty filtered answer: Pages.Search needs the 'all' response to filter client-side
            var all = await SearchRawAsync(query, "all", ct).ConfigureAwait(false);
            page = Pages.Search(raw, query, type, _ => all);
        }
        return page;
    }
}
