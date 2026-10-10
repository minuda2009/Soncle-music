// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json.Nodes;

namespace Soncle.YouTube.InnerTube;

/// <summary>
/// What distinguishes one InnerTube client from another: its name and version, the numeric id sent
/// in <c>X-Youtube-Client-Name</c>, and how it changes the request <c>context</c>. Every constant
/// is copied from youtubei.js 18.1.0 (<c>utils/Constants.js</c> and <c>HTTPClient.#adjustContext</c>),
/// the library the JS app uses, so the two stay request-for-request the same. These versions go
/// stale; bump them together with the JS dependency.
/// </summary>
public sealed record ClientProfile(string Spec, string Name, string Version, string NameId, string? UserAgent, Action<JsonObject> Adjust)
{
    public const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    private const string YtBase = "https://www.youtube.com";
    private const string GoogleSearchBase = "https://www.google.com/";

    public const string IosUa = "com.google.ios.youtube/20.11.6 (iPhone10,4; U; CPU iOS 16_7_7 like Mac OS X)";
    public const string AndroidVrUa = "com.google.android.apps.youtube.vr.oculus/1.65.10 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip";
    public const string TvUa = "Mozilla/5.0 (ChromiumStylePlatform) Cobalt/Version";

    /// <summary>The six stream clients in <c>STREAM_SPECS</c>, keyed by spec name.</summary>
    public static IReadOnlyDictionary<string, ClientProfile> Streaming { get; } = new Dictionary<string, ClientProfile>
    {
        ["YTMUSIC"] = new("YTMUSIC", "WEB_REMIX", "1.20250219.01.00", "67", null, _ => { }),
        ["WEB"] = new("WEB", "WEB", "2.20260623.01.00", "1", null, _ => { }),
        ["IOS"] = new("IOS", "iOS", "20.11.6", "5", IosUa, c =>
        {
            c["deviceMake"] = "Apple"; c["deviceModel"] = "iPhone10,4";
            c["platform"] = "MOBILE"; c["osName"] = "iOS"; c["osVersion"] = "16.7.7.20H330";
            c.Remove("browserName"); c.Remove("browserVersion");
        }),
        ["ANDROID_VR"] = new("ANDROID_VR", "ANDROID_VR", "1.65.10", "28", AndroidVrUa, c =>
        {
            c["androidSdkVersion"] = 32; c["osName"] = "Android"; c["osVersion"] = "12L";
            c["platform"] = "MOBILE"; c["userAgent"] = AndroidVrUa;
            c["deviceMake"] = "Oculus"; c["deviceModel"] = "Quest 3";
            c["clientFormFactor"] = "SMALL_FORM_FACTOR";
        }),
        ["TV"] = new("TV", "TVHTML5", "7.20260311.12.00", "7", null, c => { c["userAgent"] = TvUa; }),
        ["WEB_EMBEDDED"] = new("WEB_EMBEDDED", "WEB_EMBEDDED_PLAYER", "1.20260206.01.00", "56", null, c => { c["clientScreen"] = "EMBED"; }),
    };

    /// <summary>Applies this client to a fresh session context (the object <c>{ client, user, request }</c>).</summary>
    public void Apply(JsonObject context)
    {
        var client = context["client"]!.AsObject();
        // the config blob only goes with the web client
        if (Spec != "WEB") client.Remove("configInfo");
        client["clientName"] = Name;
        client["clientVersion"] = Version;
        Adjust(client);
        if (Spec == "WEB_EMBEDDED") context["thirdParty"] = new JsonObject { ["embedUrl"] = GoogleSearchBase };
    }

    /// <summary>The Origin/Referer a stream request needs (yt.mjs <c>headersFor</c>).</summary>
    public static string? StreamOrigin(string spec) => spec switch
    {
        "YTMUSIC" => "https://music.youtube.com",
        "WEB" or "MWEB" or "WEB_EMBEDDED" => YtBase,
        _ => null,
    };
}
