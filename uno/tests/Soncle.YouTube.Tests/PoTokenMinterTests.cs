// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;
using System.Text.Json.Nodes;
using Soncle.YouTube;

namespace Soncle.YouTube.Tests;

/// <summary>
/// A fake BotGuard page: answers the scripts potoken.mjs runs in po_token.html (location.origin,
/// runBotGuard, createPoTokenMinter, obtainPoToken) and records them. A test can hold or fail a
/// mint through <see cref="OnMint"/>.
/// </summary>
internal sealed class FakePage : IBotGuardPage
{
    public const string BotGuardResponse = "BG-RESPONSE-1";
    private readonly List<string> _codes = new();
    private int _disposed;
    public int Id { get; init; }
    /// <summary>Called for each obtainPoToken with the identifier; null = answer right away.</summary>
    public Func<string, CancellationToken, Task<JsonNode?>>? OnMint;
    public bool Disposed => Volatile.Read(ref _disposed) != 0;
    public int DisposeCount => Volatile.Read(ref _disposed);
    public bool IsAlive => !Disposed;
    public string[] Codes { get { lock (_codes) return _codes.ToArray(); } }
    public int Mints => Codes.Count(c => c.StartsWith("obtainPoToken(", StringComparison.Ordinal));

    /// <summary>The token the fake minter makes for an identifier: its UTF-8 bytes plus the page id.</summary>
    public static byte[] TokenBytes(string identifier, int page) => Encoding.UTF8.GetBytes(identifier).Append((byte)page).ToArray();

    public Task<JsonNode?> ExecAsync(string code, CancellationToken ct)
    {
        lock (_codes) _codes.Add(code);
        if (Disposed) return Task.FromException<JsonNode?>(new ObjectDisposedException("page"));
        if (code == "location.origin") return Task.FromResult<JsonNode?>(JsonValue.Create(PoTokenMinter.Origin));
        if (code.StartsWith("obtainPoToken(", StringComparison.Ordinal))
        {
            var bytes = JsonNode.Parse(code["obtainPoToken(new Uint8Array(".Length..code.IndexOf(')')])!.AsArray().Select(b => (byte)b!.GetValue<int>()).ToArray();
            var id = Encoding.UTF8.GetString(bytes);
            if (OnMint is { } hook) return hook(id, ct);
            return Task.FromResult<JsonNode?>(Answer(id));
        }
        if (code.StartsWith("runBotGuard(", StringComparison.Ordinal)) return Task.FromResult<JsonNode?>(JsonValue.Create(BotGuardResponse));
        if (code.StartsWith("createPoTokenMinter(", StringComparison.Ordinal)) return Task.FromResult<JsonNode?>(JsonValue.Create(true));
        return Task.FromException<JsonNode?>(new InvalidOperationException("unexpected script"));
    }

    public JsonNode Answer(string id) => new JsonArray(TokenBytes(id, Id).Select(b => (JsonNode?)JsonValue.Create((int)b)).ToArray());

    public void Dispose() => Interlocked.Increment(ref _disposed);
}

/// <summary>A clock whose delays only finish when the test moves it on.</summary>
internal sealed class FakeClock : IMinterClock
{
    private readonly List<(long Due, TaskCompletionSource Done)> _waits = new();
    private long _now;
    public long NowMs => Interlocked.Read(ref _now);

    public int Pending { get { lock (_waits) return _waits.Count(w => !w.Done.Task.IsCompleted); } }

    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Task.FromCanceled(ct);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => done.TrySetCanceled(ct));
        lock (_waits) _waits.Add((NowMs + (long)delay.TotalMilliseconds, done));
        return done.Task;
    }

    public void Advance(TimeSpan d)
    {
        Interlocked.Add(ref _now, (long)d.TotalMilliseconds);
        List<TaskCompletionSource> due;
        lock (_waits)
        {
            due = _waits.Where(w => w.Due <= NowMs).Select(w => w.Done).ToList();
            _waits.RemoveAll(w => w.Due <= NowMs);
        }
        foreach (var w in due) w.TrySetResult();
    }
}

public class PoTokenMinterTests
{
    private const string IntegrityToken = "aW50ZWdyaXR5LXRva2Vu";   // base64 of "integrity-token"

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly List<FakePage> Pages = new();
        public readonly List<(string Url, string? Body)> Requests = new();
        public int Ttl = 3600;
        public string? CreateBody;
        public bool FailGenerateIt;
        /// <summary>Holds the Create request until set (honours cancellation).</summary>
        public TaskCompletionSource? HoldCreate;
        /// <summary>Holds the page factory until set; the page is still created when it's released.</summary>
        public TaskCompletionSource? HoldFactory;
        public Action<FakePage>? SetUp;
        public PoTokenMinter Minter = null!;

        public Rig()
        {
            Minter = new PoTokenMinter(Factory, Fetch, Clock, _ => { });
        }

        public FakePage Page => Pages[^1];
        public int Creates { get { lock (Requests) return Requests.Count(r => r.Url == BotGuard.CreateUrl); } }

        private async Task<IBotGuardPage> Factory(CancellationToken ct)
        {
            if (HoldFactory is { } hold) await hold.Task;   // ignores ct, like a WebView that is slow to come up
            FakePage page;
            lock (Pages)
            {
                page = new FakePage { Id = Pages.Count + 1 };
                Pages.Add(page);
            }
            SetUp?.Invoke(page);
            return page;
        }

        private async Task<(int, string)> Fetch(string url, IReadOnlyDictionary<string, string> headers, string? body, CancellationToken ct)
        {
            lock (Requests) Requests.Add((url, body));
            if (url == BotGuard.CreateUrl)
            {
                Assert.Equal(BotGuard.ApiKey, headers["x-goog-api-key"]);
                if (HoldCreate is { } hold) await hold.Task.WaitAsync(ct);
                return (200, CreateBody ?? PoTokenMinterTests.CreateBody(new JsonArray("js"), null));
            }
            if (url == BotGuard.GenerateItUrl)
            {
                if (FailGenerateIt) return (403, "");
                return (200, new JsonArray(IntegrityToken, Ttl).ToJsonString());
            }
            if (body is null && url == "https://www.google.com/js/bg/interp.js") return (200, "/* the interpreter */");
            return (404, "");
        }
    }

    // a /Create body: ["x", base64(challenge shifted by -97)]
    private static string CreateBody(JsonNode? script, JsonNode? resource)
    {
        var cd = new JsonArray("msg", script, resource, "hash", "prog", "gName", null, "blob").ToJsonString();
        var shifted = Encoding.UTF8.GetBytes(cd).Select(b => (byte)((b - 97) & 0xff)).ToArray();
        return new JsonArray("x", Convert.ToBase64String(shifted)).ToJsonString();
    }

    private static async Task Until(Func<bool> cond, string what)
    {
        for (var i = 0; i < 2000 && !cond(); i++) await Task.Delay(2);
        Assert.True(cond(), what);
    }

    private static string Token(string id, int page) => BotGuard.TokenFromBytes(FakePage.TokenBytes(id, page));

    [Fact]
    public async Task InitRunsBotGuardAndSendsItsResponseToGenerateIT()
    {
        var rig = new Rig();
        Assert.Equal(Token("visitor", 1), await rig.Minter.MintAsync("visitor"));

        // Create with the request key, then GenerateIT with the BotGuard response the page produced
        Assert.Equal(new[] { BotGuard.CreateUrl, BotGuard.GenerateItUrl }, rig.Requests.Select(r => r.Url));
        Assert.Equal($"[\"{BotGuard.RequestKey}\"]", rig.Requests[0].Body);
        Assert.Equal($"[\"{BotGuard.RequestKey}\",\"{FakePage.BotGuardResponse}\"]", rig.Requests[1].Body);

        var codes = rig.Page.Codes;
        Assert.Equal(4, codes.Length);
        Assert.Equal("location.origin", codes[0]);
        // runBotGuard gets the parsed challenge, keeps webPoSignalOutput and returns the response
        Assert.StartsWith("runBotGuard(", codes[1]);
        Assert.EndsWith(".then(function (r) { window.__wpso = r.webPoSignalOutput; return r.botguardResponse; })", codes[1]);
        var ch = JsonNode.Parse(codes[1]["runBotGuard(".Length..codes[1].IndexOf(").then(", StringComparison.Ordinal)])!;
        Assert.Equal("js", (string?)ch["interpreterJavascript"]!["privateDoNotAccessOrElseSafeScriptWrappedValue"]);
        Assert.Null(ch["interpreterJavascript"]!["privateDoNotAccessOrElseTrustedResourceUrlWrappedValue"]);
        Assert.Equal("msg", (string?)ch["messageId"]);
        Assert.Equal("hash", (string?)ch["interpreterHash"]);
        Assert.Equal("prog", (string?)ch["program"]);
        Assert.Equal("gName", (string?)ch["globalName"]);
        Assert.Equal("blob", (string?)ch["clientExperimentsStateBlob"]);
        // the minter is made once, from the integrity token's bytes
        var itBytes = string.Join(",", BotGuard.B64Bytes(IntegrityToken));
        Assert.Equal($"createPoTokenMinter(window.__wpso, new Uint8Array([{itBytes}])).then(function () {{ return true; }})", codes[2]);
        Assert.Equal($"obtainPoToken(new Uint8Array([{string.Join(",", Encoding.UTF8.GetBytes("visitor"))}])).then(function (u) {{ return Array.from(u); }})", codes[3]);

        // a second identifier only mints
        Assert.Equal(Token("video", 1), await rig.Minter.MintAsync("video"));
        Assert.Equal(5, rig.Page.Codes.Length);
        Assert.Single(rig.Pages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]   // an empty inline script is fetched too, like !SafeScriptWrappedValue in the JS
    public async Task AnInterpreterGivenAsAUrlIsFetchedFirst(bool emptyInline)
    {
        var inline = emptyInline ? JsonValue.Create("") : null;
        var rig = new Rig { CreateBody = CreateBody(inline, new JsonArray("//www.google.com/js/bg/interp.js")) };
        await rig.Minter.MintAsync("v");
        Assert.Contains(rig.Requests, r => r.Url == "https://www.google.com/js/bg/interp.js" && r.Body is null);
        var run = rig.Page.Codes.Single(c => c.StartsWith("runBotGuard(", StringComparison.Ordinal));
        var ch = JsonNode.Parse(run["runBotGuard(".Length..run.IndexOf(").then(", StringComparison.Ordinal)])!;
        Assert.Equal("/* the interpreter */", (string?)ch["interpreterJavascript"]!["privateDoNotAccessOrElseSafeScriptWrappedValue"]);
        Assert.Equal("//www.google.com/js/bg/interp.js", (string?)ch["interpreterJavascript"]!["privateDoNotAccessOrElseTrustedResourceUrlWrappedValue"]);
    }

    [Fact]
    public async Task ConcurrentMintsShareOneInit()
    {
        var rig = new Rig { HoldCreate = new() };
        var a = rig.Minter.MintAsync("visitor-a");
        var b = rig.Minter.MintAsync("visitor-b");
        await Until(() => rig.Creates == 1, "no init");
        var c = rig.Minter.MintAsync("visitor-c");
        rig.HoldCreate.SetResult();
        await Task.WhenAll(a, b, c);
        Assert.Equal(Token("visitor-a", 1), await a);
        Assert.Equal(Token("visitor-b", 1), await b);
        Assert.Equal(Token("visitor-c", 1), await c);
        Assert.Single(rig.Pages);
        Assert.Equal(1, rig.Creates);
        Assert.Equal(3, rig.Page.Mints);
    }

    [Fact]
    public async Task OneCallerGivingUpDoesNotStopTheSharedInit()
    {
        var rig = new Rig { HoldCreate = new() };
        using var cts = new CancellationTokenSource();
        var a = rig.Minter.MintAsync("a", cts.Token);
        var b = rig.Minter.MintAsync("b");
        await Until(() => rig.Creates == 1, "no init");
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
        rig.HoldCreate.SetResult();
        Assert.Equal(Token("b", 1), await b);
        Assert.Equal(1, rig.Creates);
        Assert.False(rig.Page.Disposed);
    }

    [Fact]
    public async Task ASecondMintOfTheSameIdIsCached()
    {
        var rig = new Rig();
        var first = await rig.Minter.MintAsync("v");
        var second = await rig.Minter.MintAsync("v");
        Assert.Equal(first, second);
        Assert.Equal(1, rig.Page.Mints);
    }

    [Fact]
    public async Task AFailedInitClosesThePageAndFailsFastForTwoMinutes()
    {
        var rig = new Rig { FailGenerateIt = true };
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Minter.MintAsync("v"));
        Assert.Equal("BotGuard GenerateIT HTTP 403", e.Message);
        Assert.True(rig.Page.Disposed, "no orphaned page");
        Assert.Equal(0, rig.Page.Mints);

        // within the cooldown it fails fast without trying again
        var fast = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Minter.MintAsync("v"));
        Assert.Equal("PO token provider unavailable", fast.Message);
        Assert.Single(rig.Pages);

        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        rig.FailGenerateIt = false;
        Assert.Equal(Token("v", 2), await rig.Minter.MintAsync("v"));
        Assert.Equal(2, rig.Pages.Count);
    }

    [Fact]
    public async Task InitGivesUpAfter25SecondsAndClosesThePage()
    {
        var rig = new Rig { HoldCreate = new() };
        var t = rig.Minter.MintAsync("v");
        await Until(() => rig.Creates == 1 && rig.Clock.Pending >= 1, "no init");
        rig.Clock.Advance(TimeSpan.FromSeconds(24));
        await Task.Delay(20);
        Assert.False(t.IsCompleted);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        var e = await Assert.ThrowsAsync<TimeoutException>(() => t);
        Assert.Equal("PO token init timed out", e.Message);
        await Until(() => rig.Page.Disposed, "the page was left open");
        Assert.Equal(1, rig.Page.DisposeCount);
    }

    [Fact]
    public async Task APageThatArrivesAfterTheTimeoutIsClosed()
    {
        var rig = new Rig { HoldFactory = new() };
        var t = rig.Minter.MintAsync("v");
        await Until(() => rig.Clock.Pending >= 1, "no init");
        rig.Clock.Advance(PoTokenMinter.InitTimeout);
        await Assert.ThrowsAsync<TimeoutException>(() => t);
        Assert.Empty(rig.Pages);
        rig.HoldFactory.SetResult();   // the WebView finally comes up
        await Until(() => rig.Pages.Count == 1 && rig.Page.Disposed, "the late page was left open");
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task AFailedMintDoesNotBreakAMintInFlightNorANewerMinter()
    {
        var rig = new Rig();
        var holds = new Dictionary<string, TaskCompletionSource<JsonNode?>>();
        rig.SetUp = page => page.OnMint = (id, ct) =>
        {
            if (id.StartsWith("ok", StringComparison.Ordinal)) return Task.FromResult<JsonNode?>(page.Answer(id));
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (holds) holds[id + page.Id] = tcs;
            return tcs.Task;
        };
        await rig.Minter.MintAsync("ok-1");
        var first = rig.Page;

        // two mints in flight on the first minter
        var a = rig.Minter.MintAsync("a");
        var b = rig.Minter.MintAsync("b");
        await Until(() => { lock (holds) return holds.Count == 2; }, "mints not in flight");

        // a fails: it retires the first minter, but b, still in flight on it, isn't broken
        holds["a1"].SetException(new InvalidOperationException("MNT:Failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => a);
        Assert.False(first.Disposed, "closed under a mint in flight");

        // the next request gets a new minter
        Assert.Equal(Token("ok-2", 2), await rig.Minter.MintAsync("ok-2"));
        Assert.Equal(2, rig.Pages.Count);
        var second = rig.Page;

        // b finishes on the old one, then its page closes; a late failure there never touches the newer minter
        holds["b1"].SetResult(first.Answer("b"));
        Assert.Equal(Token("b", 1), await b);
        await Until(() => first.Disposed, "the retired page was left open");
        var c = rig.Minter.MintAsync("c");
        await Until(() => { lock (holds) return holds.ContainsKey("c2"); }, "c not in flight");
        holds["c2"].SetException(new InvalidOperationException("MNT:Failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c);
        Assert.True(second.Disposed);   // nothing else was in flight on it
        Assert.Equal(Token("ok-3", 3), await rig.Minter.MintAsync("ok-3"));
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task AMintThatHangsGivesUpAfter20Seconds()
    {
        var rig = new Rig();
        rig.SetUp = page => page.OnMint = (id, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => (JsonNode?)null, TaskScheduler.Default);
        var t = rig.Minter.MintAsync("v");
        await Until(() => rig.Pages.Count == 1 && rig.Page.Mints == 1 && rig.Clock.Pending >= 1, "no mint");
        rig.Clock.Advance(PoTokenMinter.ScriptTimeout);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => t);
        Assert.IsType<TimeoutException>(e.InnerException);
        await Until(() => rig.Page.Disposed, "the page was left open");
    }

    [Fact]
    public async Task TheIdleCloseClosesThePageAfterTenMinutesWithoutARequest()
    {
        var rig = new Rig();
        await rig.Minter.MintAsync("v");
        await Until(() => rig.Clock.Pending >= 1, "no idle timer");
        rig.Clock.Advance(TimeSpan.FromMinutes(9));
        await rig.Minter.MintAsync("w");   // a request: the 10 minutes start again
        rig.Clock.Advance(TimeSpan.FromMinutes(9));
        await Task.Delay(20);
        Assert.False(rig.Page.Disposed);
        await Until(() => rig.Clock.Pending >= 1, "no idle timer");
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        await Until(() => rig.Page.Disposed, "the idle page was left open");
        // the next request rebuilds it
        Assert.Equal(Token("x", 2), await rig.Minter.MintAsync("x"));
    }

    [Fact]
    public async Task AnExpiredMinterIsReplacedAndItsPageClosed()
    {
        var rig = new Rig { Ttl = 900 };   // lives max(300, 900 - 600) s
        await rig.Minter.MintAsync("v");
        rig.Clock.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal(Token("w", 1), await rig.Minter.MintAsync("w"));
        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(Token("v", 2), await rig.Minter.MintAsync("v"));   // the cache went with the old minter
        Assert.True(rig.Pages[0].Disposed);
        Assert.False(rig.Pages[1].Disposed);
    }

    [Fact]
    public async Task ResetClosesThePage()
    {
        var rig = new Rig();
        await rig.Minter.MintAsync("v");
        rig.Minter.Reset();
        Assert.True(rig.Page.Disposed);
        Assert.Equal(Token("v", 2), await rig.Minter.MintAsync("v"));
    }

    [Fact]
    public async Task AResetDuringInitDoesNotArmTheTwoMinuteCooldown()
    {
        var rig = new Rig { HoldCreate = new() };
        var first = rig.Minter.MintAsync("v");
        await Until(() => rig.Creates == 1, "no init");
        rig.Minter.Reset();
        await Assert.ThrowsAnyAsync<Exception>(() => first);
        Assert.True(rig.Page.Disposed, "the reset init closed its page");

        // the next caller starts a fresh init right away instead of failing fast
        rig.HoldCreate = null;
        Assert.Equal(Token("v", 2), await rig.Minter.MintAsync("v"));
        Assert.Equal(2, rig.Pages.Count);
    }

    [Fact]
    public async Task TheTtlIsTheNumberOrAnHourAndTheMinterLivesAtLeastFiveMinutes()
    {
        Assert.Equal(3600, BotGuard.Ttl(null));
        Assert.Equal(3600, BotGuard.Ttl(JsonValue.Create(0)));
        Assert.Equal(3600, BotGuard.Ttl(JsonValue.Create("abc")));
        Assert.Equal(43200, BotGuard.Ttl(JsonValue.Create("43200")));
        Assert.Equal(1800, BotGuard.Ttl(JsonValue.Create(1800)));
        // and a short TTL still keeps the minter 5 minutes
        var rig = new Rig { Ttl = 60 };
        await rig.Minter.MintAsync("v");
        rig.Clock.Advance(TimeSpan.FromSeconds(299));
        await rig.Minter.MintAsync("w");
        Assert.Single(rig.Pages);
    }
}
