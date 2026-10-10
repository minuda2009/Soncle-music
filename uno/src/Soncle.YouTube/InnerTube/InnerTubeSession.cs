// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Soncle.YouTube.InnerTube;

/// <summary>
/// The anonymous web session every InnerTube request is built on: language, region, time zone and
/// the visitor data YouTube hands out (PO tokens are bound to it). A port of youtubei.js'
/// <c>Session.getSessionData</c> / <c>#buildContext</c> with <c>generate_session_locally: false</c>.
/// No cookie ever enters this class.
/// </summary>
public sealed class InnerTubeSession
{
    public string Lang { get; init; } = "en";
    public string Location { get; init; } = "US";
    public string TimeZone { get; init; } = "UTC";
    public int UtcOffsetMinutes { get; init; }
    public string UserAgent { get; init; } = ClientProfile.DesktopUserAgent;
    public string VisitorData { get; init; } = "";
    public string RemoteHost { get; init; } = "";
    public string OsName { get; init; } = "Windows";
    public string OsVersion { get; init; } = "10.0";
    public string BrowserName { get; init; } = "Chrome";
    public string BrowserVersion { get; init; } = "140.0.0.0";
    public string DeviceMake { get; init; } = "";
    public string DeviceModel { get; init; } = "";
    public string? AppInstallData { get; init; }
    public string? RolloutToken { get; init; }
    public string? DeviceExperimentId { get; init; }
    /// <summary>True when the visitor data came from YouTube rather than being absent (a server-less fallback).</summary>
    public bool FromServer { get; init; }

    /// <summary>
    /// Fetches visitor data and defaults from <c>/sw.js_data</c>. If YouTube can't be reached the
    /// session still works for clients that need no visitor binding; PO-token clients will then fail
    /// and the resolver moves on to the next client.
    /// </summary>
    public static async Task<InnerTubeSession> CreateAsync(HttpClient http, string lang = "", string location = "", string? timeZone = null, CancellationToken ct = default)
    {
        var tz = timeZone ?? TimeZoneInfo.Local.Id;
        var offset = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;
        var basic = new InnerTubeSession
        {
            Lang = string.IsNullOrEmpty(lang) ? "en" : lang,
            Location = string.IsNullOrEmpty(location) ? "US" : location,
            TimeZone = tz,
            UtcOffsetMinutes = offset,
        };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/sw.js_data");
            req.Headers.TryAddWithoutValidation("Accept-Language", string.IsNullOrEmpty(lang) ? "en-US" : lang);
            req.Headers.TryAddWithoutValidation("User-Agent", basic.UserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.youtube.com/sw.js");
            // a throwaway visitor id, as youtubei.js sends
            req.Headers.TryAddWithoutValidation("Cookie", $"PREF=tz={tz.Replace('/', '.')};VISITOR_INFO1_LIVE={RandomId(11)};");
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return basic;
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseSwJsData(text, basic) ?? basic;
        }
        catch (OperationCanceledException) { throw; }
        catch { return basic; }
    }

    /// <summary>Reads <c>device_info</c> out of the <c>)]}'</c>-prefixed JSPB body. Null if the shape is not recognised.</summary>
    public static InnerTubeSession? ParseSwJsData(string text, InnerTubeSession basic)
    {
        if (!text.StartsWith(")]}'", StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text[4..]);
            var ytcfg = doc.RootElement[0][2];
            var d = ytcfg[0][0];
            string S(int i) => i < d.GetArrayLength() && d[i].ValueKind == JsonValueKind.String ? d[i].GetString()! : "";
            string? cfgBlob = null;
            if (d.GetArrayLength() > 61 && d[61].ValueKind == JsonValueKind.Array && d[61].GetArrayLength() > 0)
            {
                var last = d[61][d[61].GetArrayLength() - 1];
                if (last.ValueKind == JsonValueKind.String) cfgBlob = last.GetString();
            }
            var visitor = S(13);
            if (visitor.Length == 0) return null;
            return new InnerTubeSession
            {
                Lang = string.IsNullOrEmpty(basic.Lang) ? S(0) : basic.Lang,
                Location = string.IsNullOrEmpty(basic.Location) ? S(1) : basic.Location,
                TimeZone = S(79) is { Length: > 0 } tz ? tz : basic.TimeZone,
                UtcOffsetMinutes = basic.UtcOffsetMinutes,
                UserAgent = basic.UserAgent,
                VisitorData = visitor,
                RemoteHost = S(3),
                OsName = S(17) is { Length: > 0 } o ? o : basic.OsName,
                OsVersion = S(18) is { Length: > 0 } ov ? ov : basic.OsVersion,
                BrowserName = S(86) is { Length: > 0 } bn ? bn : basic.BrowserName,
                BrowserVersion = S(87) is { Length: > 0 } bv ? bv : basic.BrowserVersion,
                DeviceMake = S(11),
                DeviceModel = S(12),
                AppInstallData = cfgBlob,
                DeviceExperimentId = S(103) is { Length: > 0 } de ? de : null,
                RolloutToken = S(107) is { Length: > 0 } rt ? rt : null,
                FromServer = true,
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>The base <c>context</c> object (youtubei.js <c>#buildContext</c>), before a client adjusts it.</summary>
    public JsonObject BuildContext()
    {
        var client = new JsonObject
        {
            ["hl"] = Lang,
            ["gl"] = Location,
            ["remoteHost"] = RemoteHost,
            ["screenDensityFloat"] = 1,
            ["screenHeightPoints"] = 1440,
            ["screenPixelDensity"] = 1,
            ["screenWidthPoints"] = 2560,
            ["visitorData"] = VisitorData,
            ["clientName"] = "WEB",
            ["clientVersion"] = "",
            ["osName"] = OsName,
            ["osVersion"] = OsVersion,
            ["userAgent"] = UserAgent,
            ["platform"] = "DESKTOP",
            ["clientFormFactor"] = "UNKNOWN_FORM_FACTOR",
            ["userInterfaceTheme"] = "USER_INTERFACE_THEME_LIGHT",
            ["timeZone"] = TimeZone,
            ["originalUrl"] = "https://www.youtube.com",
            ["deviceMake"] = DeviceMake,
            ["deviceModel"] = DeviceModel,
            ["browserName"] = BrowserName,
            ["browserVersion"] = BrowserVersion,
            ["utcOffsetMinutes"] = UtcOffsetMinutes,
            ["memoryTotalKbytes"] = "8000000",
        };
        if (RolloutToken is not null) client["rolloutToken"] = RolloutToken;
        if (DeviceExperimentId is not null) client["deviceExperimentId"] = DeviceExperimentId;
        client["mainAppWebInfo"] = new JsonObject
        {
            ["graftUrl"] = "https://www.youtube.com",
            ["pwaInstallabilityStatus"] = "PWA_INSTALLABILITY_STATUS_UNKNOWN",
            ["webDisplayMode"] = "WEB_DISPLAY_MODE_BROWSER",
            ["isWebNativeShareAvailable"] = true,
        };
        if (AppInstallData is not null) client["configInfo"] = new JsonObject { ["appInstallData"] = AppInstallData };
        return new JsonObject
        {
            ["client"] = client,
            ["user"] = new JsonObject { ["enableSafetyMode"] = false, ["lockedSafetyMode"] = false },
            ["request"] = new JsonObject { ["useSsl"] = true, ["internalExperimentFlags"] = new JsonArray() },
        };
    }

    private static string RandomId(int n)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var chars = new char[n];
        for (var i = 0; i < n; i++) chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(chars);
    }
}
