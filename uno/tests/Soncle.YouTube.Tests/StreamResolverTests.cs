// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.YouTube;

namespace Soncle.YouTube.Tests;

/// <summary>A fake stream client keyed by client name, to test the resolver's portable rules.</summary>
internal sealed class FakeStreamClient : IStreamClient
{
    public string UserAgent => "test-ua";
    public readonly Dictionary<string, Func<PlayerResponse>> Responses = new();
    public readonly Dictionary<string, string?> SeekResults = new();   // client → null ok / reason
    public readonly List<string> CalledClients = new();
    public long ProbeLength;

    public Task<PlayerResponse> GetPlayerAsync(string videoId, string client, string? potToken, CancellationToken ct)
    {
        CalledClients.Add(client);
        if (!Responses.TryGetValue(client, out var f)) throw new InvalidOperationException($"no response for {client}");
        return Task.FromResult(f());
    }
    public Task<string?> CanSeekAsync(string client, string url, IReadOnlyDictionary<string, string> headers, long length, CancellationToken ct) =>
        Task.FromResult(SeekResults.TryGetValue(client, out var r) ? r : null);
    public Task<long> ProbeLengthAsync(string client, string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct) => Task.FromResult(ProbeLength);
}

public class StreamResolverTests
{
    private static PlayerResponse Fmts(params AudioFormat[] fmts) => new("OK", null, fmts);

    /// <summary>Answers every client with one high-bitrate format.</summary>
    private static FakeStreamClient AnyClient(string url = "https://gv/x?clen=1")
    {
        var c = new FakeStreamClient();
        foreach (var name in new[] { "YTMUSIC", "IOS", "ANDROID_VR", "WEB", "TV", "WEB_EMBEDDED" })
            c.Responses[name] = () => Fmts(new AudioFormat(url, "audio/webm", 128_000, 0));
        return c;
    }

    [Fact]
    public async Task UsesTheFirstClientInOrder()
    {
        var c = AnyClient();
        var r = new StreamResolver(c, pot: (id, ct) => Task.FromResult("pot"));
        var s = await r.ResolveAsync("v1");
        Assert.Equal("YTMUSIC", s.Client);
        Assert.Equal(new[] { "YTMUSIC" }, c.CalledClients);
    }

    [Fact]
    public async Task FallsBackToTheNextClientWhenOneFails()
    {
        var c = AnyClient();
        c.Responses.Remove("YTMUSIC");   // first client has no response → fails
        var r = new StreamResolver(c, pot: (id, ct) => Task.FromResult("pot"));
        var s = await r.ResolveAsync("v1");
        Assert.Equal("IOS", s.Client);
        // a token client is tried with the token and then swapped, then the next client
        Assert.Equal(new[] { "YTMUSIC", "YTMUSIC", "IOS" }, c.CalledClients);
    }

    [Fact]
    public async Task APoTokenFailureSkipsTheTokenClients()
    {
        var c = AnyClient();
        c.Responses.Remove("YTMUSIC"); c.Responses.Remove("WEB");   // both token clients fail
        var r = new StreamResolver(c, pot: (id, ct) => Task.FromResult("pot"));
        var s = await r.ResolveAsync("v1");
        Assert.Equal("IOS", s.Client);
        // YTMUSIC+pot failed (no response, both token swaps) → skip; IOS succeeds, so WEB is never tried
        Assert.Equal(new[] { "YTMUSIC", "YTMUSIC", "IOS" }, c.CalledClients);
    }

    [Fact]
    public async Task WithoutAPoTokenProviderTheTokenClientsAreSkipped()
    {
        var c = AnyClient();
        var r = new StreamResolver(c, pot: null);
        var s = await r.ResolveAsync("v1");
        // YTMUSIC needs a token → "no PO token provider" → skip to IOS
        Assert.Equal("IOS", s.Client);
    }

    [Fact]
    public async Task PicksTheHighestBitrateNonDrcDefaultTrack()
    {
        var c = new FakeStreamClient();
        c.Responses["IOS"] = () => Fmts(
            new AudioFormat("https://gv/a?clen=1000", "audio/webm", 128_000, 0, IsDrc: true),
            new AudioFormat("https://gv/b?clen=1000", "audio/webm", 160_000, 0),
            new AudioFormat("https://gv/c?clen=1000", "audio/webm", 96_000, 0, IsDefaultAudioTrack: false));
        var r = new StreamResolver(c);
        var s = await r.ResolveAsync("v1");
        Assert.Equal("https://gv/b?clen=1000", s.Url);
        Assert.Equal(160_000, s.Bitrate);
    }

    [Fact]
    public async Task LowQualityPicksTheLowestBitrate()
    {
        var c = new FakeStreamClient();
        c.Responses["IOS"] = () => Fmts(
            new AudioFormat("https://gv/hi?clen=1", "audio/webm", 160_000, 0),
            new AudioFormat("https://gv/lo?clen=1", "audio/webm", 64_000, 0));
        var r = new StreamResolver(c);
        var s = await r.ResolveAsync("v1", "low");
        Assert.Equal("https://gv/lo?clen=1", s.Url);
    }

    [Fact]
    public async Task LengthFallsBackToTheClenParameterThenAProbe()
    {
        var c = new FakeStreamClient();
        c.Responses["IOS"] = () => Fmts(new AudioFormat("https://gv/x?clen=4242", "audio/webm", 128_000, 0));
        Assert.Equal(4242, (await new StreamResolver(c).ResolveAsync("v1")).Length);

        var c2 = new FakeStreamClient { ProbeLength = 9999 };
        c2.Responses["IOS"] = () => Fmts(new AudioFormat("https://gv/x", "audio/webm", 128_000, 0));
        Assert.Equal(9999, (await new StreamResolver(c2).ResolveAsync("v1")).Length);
    }

    [Fact]
    public async Task TheCacheServesASecondCallAndForceBypassesIt()
    {
        var c = AnyClient();
        var n = 0;
        c.Responses["IOS"] = () => { n++; return Fmts(new AudioFormat("https://gv/x?clen=1", "audio/webm", 128_000, 0)); };
        var r = new StreamResolver(c);
        await r.ResolveAsync("v1");
        await r.ResolveAsync("v1");
        Assert.Equal(1, n);
        await r.ResolveAsync("v1", force: true);
        Assert.Equal(2, n);
    }

    [Fact]
    public async Task ASeekProbeFailureSkipsTheClient()
    {
        var c = AnyClient();
        c.SeekResults["YTMUSIC"] = "HTTP 403";
        var r = new StreamResolver(c);
        var s = await r.ResolveAsync("v1");
        Assert.Equal("IOS", s.Client);   // YTMUSIC skipped, IOS works
    }

    [Fact]
    public async Task RotatingRetryAvoidsTheExcludedClient()
    {
        var c = AnyClient();
        var r = new StreamResolver(c);
        var s = await r.ResolveRotatingAsync("v1", exclude: "YTMUSIC");
        Assert.Equal("IOS", s.Client);
        Assert.DoesNotContain("YTMUSIC", c.CalledClients);
    }

    [Fact]
    public void SetClientsKeepsOnlyTheNamedOnesInOrder()
    {
        var r = new StreamResolver(AnyClient());
        r.SetClients(new[] { "ANDROID_VR", "IOS" });
        Assert.Equal(new[] { "ANDROID_VR", "IOS" }, r.Clients);
    }
}
