// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Soncle.YouTube;

/// <summary>
/// The platform-neutral half of PO token generation, a port of <c>src/botguard.mjs</c>. Both heads
/// run <c>po_token.html</c> with a youtube.com origin; this file talks to the BotGuard endpoints and
/// decodes their responses. The WebView itself belongs to the heads.
/// </summary>
public static class BotGuard
{
    // Not secrets: public identifiers YouTube's own web player sends for its BotGuard attestation,
    // copied unchanged from the upstream PoTokenWebView.kt. No account or billing is tied to them.
    public const string RequestKey = "O43z0dpjhgX20SCx4KAo";
    public const string ApiKey = "AIzaSyDyT5W0Jh49F30Pqqtyfdf7pDLFKLJoAnw";
    public const string CreateUrl = "https://www.youtube.com/api/jnn/v1/Create";
    public const string GenerateItUrl = "https://www.youtube.com/api/jnn/v1/GenerateIT";

    /// <summary>Headers for the two BotGuard POST requests.</summary>
    public static Dictionary<string, string> Headers(string userAgent) => new()
    {
        ["User-Agent"] = userAgent,
        ["Accept"] = "application/json",
        ["Content-Type"] = "application/json+protobuf",
        ["x-goog-api-key"] = ApiKey,
        ["x-user-agent"] = "grpc-web-javascript/0.1",
    };

    /// <summary>base64 / base64url (with "." padding) → bytes.</summary>
    public static byte[] B64Bytes(string s)
    {
        var normalised = s.Replace('-', '+').Replace('_', '/').Replace('.', '=');
        return Convert.FromBase64String(normalised);
    }

    /// <summary>bytes → base64url, the form PO tokens are sent in (padding kept, like the JS btoa).</summary>
    public static string TokenFromBytes(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');

    /// <summary>The response body of a BotGuard request, which some HTTP layers hand back double-encoded.</summary>
    public static JsonNode? ParseBody(string text)
    {
        var node = JsonNode.Parse(text);
        if (node is JsonValue v && v.TryGetValue<string>(out var inner)) node = JsonNode.Parse(inner);
        return node;
    }

    /// <summary>The parsed /Create challenge runBotGuard() in po_token.html expects.</summary>
    public sealed record Challenge(string? MessageId, string? Script, string? ResourceUrl, string? InterpreterHash, string? Program, string? GlobalName, string? ClientExperimentsStateBlob);

    /// <summary>The /Create response → the challenge object.</summary>
    public static Challenge ParseChallenge(JsonNode? raw)
    {
        JsonNode? cd;
        if (raw is JsonArray arr && arr.Count > 1 && arr[1] is JsonValue second && second.TryGetValue<string>(out var b64))
        {
            var buf = B64Bytes(b64);
            for (var i = 0; i < buf.Length; i++) buf[i] = (byte)((buf[i] + 97) & 0xff);
            cd = JsonNode.Parse(Encoding.UTF8.GetString(buf));
        }
        else
        {
            cd = raw is JsonArray a && a.Count > 0 ? a[0] : null;
        }
        var arr2 = cd as JsonArray;
        string? At(int i) => arr2 is not null && i < arr2.Count ? arr2[i]?.GetValue<string>() : null;
        string? Str(JsonNode? x) => x is JsonArray inner ? inner.OfType<JsonValue>().FirstOrDefault(v => v.TryGetValue<string>(out _))?.GetValue<string>() : null;
        var interp = arr2 is not null && arr2.Count > 1 ? arr2[1] : null;
        var interpObj = interp as JsonObject;
        return new Challenge(
            At(0),
            Str(interp),
            interpObj?["privateDoNotAccessOrElseTrustedResourceUrlWrappedValue"]?.GetValue<string>(),
            At(3), At(4), At(5), At(7));
    }
}
