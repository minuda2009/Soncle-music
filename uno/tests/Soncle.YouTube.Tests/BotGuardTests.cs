// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;
using System.Text.Json.Nodes;
using Soncle.YouTube;

namespace Soncle.YouTube.Tests;

/// <summary>The JS <c>test/botguard.test.mjs</c> cases, ported.</summary>
public class BotGuardTests
{
    [Fact]
    public void HelpersRoundTripAndDecodeTheChallenge()
    {
        var bytes = new byte[] { 0, 1, 250, 251, 255, 62, 63 };
        var tok = BotGuard.TokenFromBytes(bytes);
        Assert.DoesNotContain("+", tok);
        Assert.DoesNotContain("/", tok);
        Assert.Equal(bytes, BotGuard.B64Bytes(tok));
        // same as a plain base64 with + and / swapped
        Assert.Equal(Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_'), tok);

        // /Create answers with the challenge shifted by -97 and base64-encoded
        var cd = new JsonArray("msg", new JsonArray("js"), null, "hash", "prog", "gName", null, "blob");
        var json = cd.ToJsonString();
        var shifted = Encoding.UTF8.GetBytes(json).Select(b => (byte)((b - 97) & 0xff)).ToArray();
        var raw = new JsonArray("x", Convert.ToBase64String(shifted));
        var ch = BotGuard.ParseChallenge(raw);
        Assert.Equal("msg", ch.MessageId);
        Assert.Equal("js", ch.Script);
        Assert.Equal("prog", ch.Program);
        Assert.Equal("gName", ch.GlobalName);
        Assert.Equal("blob", ch.ClientExperimentsStateBlob);
        Assert.Null(ch.ResourceUrl);

        // a double-encoded body
        var inner = new JsonArray(1, "a").ToJsonString();
        var body = JsonValue.Create(inner);
        Assert.Equal("[1,\"a\"]", BotGuard.ParseBody(body!.ToJsonString())!.ToJsonString());
    }
}
