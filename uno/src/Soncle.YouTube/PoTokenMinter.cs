// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;
using System.Text.Json.Nodes;

namespace Soncle.YouTube;

/// <summary>
/// The hidden BotGuard page (a head-provided WebView) the minter drives: it runs JS with a
/// youtube.com origin and can be torn down. The heads implement this; tests use a fake.
/// </summary>
public interface IBotGuardPage : IDisposable
{
    /// <summary>Loads the helper page and runs a JS expression that may return a promise; returns the JSON result.</summary>
    Task<JsonNode?> ExecAsync(string code, CancellationToken ct);
    bool IsAlive { get; }
}

/// <summary>Creates a fresh BotGuard page (the head builds a hidden WebView; tests return a fake).</summary>
public delegate Task<IBotGuardPage> BotGuardPageFactory(CancellationToken ct);

/// <summary>Posts to the BotGuard endpoints; the default uses HttpClient.</summary>
public delegate Task<(int Status, string Text)> BotGuardFetch(string url, IReadOnlyDictionary<string, string> headers, string body, CancellationToken ct);

/// <summary>A clock so the timeout and idle rules are testable without real waits.</summary>
public interface IMinterClock
{
    long NowMs { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

public sealed class RealMinterClock : IMinterClock
{
    public long NowMs => Environment.TickCount64;
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}

/// <summary>
/// PO token generation with Google's BotGuard, a port of the rules in <c>src/potoken.mjs</c> and
/// <c>mobile/src/native.js</c>: one minter shared by concurrent requests, a failed init fails fast
/// for 2 minutes, init times out, an idle close, and a failed mint never nulls a newer minter. The
/// WebView itself is a head (injected as <see cref="IBotGuardPage"/>).
/// </summary>
public sealed class PoTokenMinter
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan FailedCooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IdleClose = TimeSpan.FromMinutes(10);

    private readonly BotGuardPageFactory _factory;
    private readonly BotGuardFetch _fetch;
    private readonly IMinterClock _clock;
    private readonly Action<string> _log;

    private sealed class Minter
    {
        public required IBotGuardPage Page;
        public required long ExpiresMs;
        public readonly Dictionary<string, string> Cache = new();
    }

    private Minter? _state;
    private Task<Minter>? _starting;
    private long _failedAt = long.MinValue;
    private long _idleAt;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PoTokenMinter(BotGuardPageFactory factory, BotGuardFetch fetch, IMinterClock? clock = null, Action<string>? log = null)
    {
        _factory = factory;
        _fetch = fetch;
        _clock = clock ?? new RealMinterClock();
        _log = log ?? (_ => { });
    }

    /// <summary>A PO token for a visitor id or video id. Fails fast for 2 minutes after a failed start.</summary>
    public async Task<string> MintAsync(string identifier, CancellationToken ct = default)
    {
        if (_state is null || _clock.NowMs > _state.ExpiresMs)
        {
            if (_failedAt != long.MinValue && _clock.NowMs - _failedAt < FailedCooldown.TotalMilliseconds) throw new InvalidOperationException("PO token provider unavailable");
            if (_starting is null)
            {
                _starting = StartAsync(ct);
            }
        }
        var st = _state is not null && _clock.NowMs <= _state.ExpiresMs ? _state : await _starting!;
        Touch();
        if (st.Cache.TryGetValue(identifier, out var cached)) return cached;
        var bytes = Encoding.UTF8.GetBytes(identifier);
        var code = $"obtainPoToken(new Uint8Array({JsonArrayOf(bytes)})).then(function (u) {{ return Array.from(u); }})";
        JsonNode? outNode;
        try
        {
            outNode = await st.Page.ExecAsync(code, ct);
        }
        catch
        {
            if (ReferenceEquals(_state, st)) _state = null;
            throw new InvalidOperationException("PO token: mint failed");
        }
        var outBytes = outNode is JsonArray arr ? arr.Select(v => (byte)v!.GetValue<int>()).ToArray() : Array.Empty<byte>();
        var tok = BotGuard.TokenFromBytes(outBytes);
        st.Cache[identifier] = tok;
        return tok;
    }

    private async Task<Minter> StartAsync(CancellationToken ct)
    {
        try
        {
            var st = await WithTimeout(InitAsync(ct), InitTimeout, "PO token init");
            _state = st;
            return st;
        }
        catch
        {
            _failedAt = _clock.NowMs;
            _state = null;
            throw;
        }
        finally
        {
            _starting = null;
        }
    }

    private async Task<Minter> InitAsync(CancellationToken ct)
    {
        var page = await _factory(ct);
        try
        {
            var ch = BotGuard.ParseChallenge(await BgRequest(BotGuard.CreateUrl, JsonSerializer_Array(BotGuard.RequestKey), ct));
            var it = await BgRequest(BotGuard.GenerateItUrl, JsonSerializer_Array(BotGuard.RequestKey, "response"), ct);
            var ttl = it is JsonArray a && a.Count > 1 ? a[1]!.GetValue<int>() : 3600;
            _log($"PO token minter ready (ttl {ttl}s)");
            return new Minter { Page = page, ExpiresMs = _clock.NowMs + Math.Max(300, ttl - 600) * 1000L };
        }
        catch
        {
            page.Dispose();
            throw;
        }
    }

    private async Task<JsonNode?> BgRequest(string url, string body, CancellationToken ct)
    {
        var (status, text) = await _fetch(url, BotGuard.Headers(UserAgent), body, ct);
        if (status is < 200 or >= 300 || string.IsNullOrEmpty(text))
            throw new InvalidOperationException($"BotGuard {url.Split('/').Last()} HTTP {status}");
        return BotGuard.ParseBody(text);
    }

    private static string JsonSerializer_Array(params string[] items)
    {
        var arr = new JsonArray();
        foreach (var s in items) arr.Add(s);
        return arr.ToJsonString();
    }

    private static string JsonArrayOf(byte[] bytes)
    {
        var arr = new JsonArray();
        foreach (var b in bytes) arr.Add(b);
        return arr.ToJsonString();
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task) throw new TimeoutException($"{what} timed out");
        return await task;
    }

    /// <summary>Closes the hidden page after 10 minutes without a request.</summary>
    private void Touch()
    {
        _idleAt = _clock.NowMs + (long)IdleClose.TotalMilliseconds;
    }

    /// <summary>Test seam: if the idle deadline has passed, close the minter.</summary>
    internal void IdleTick()
    {
        if (_state is not null && _clock.NowMs >= _idleAt) Reset();
    }

    /// <summary>Closes the minter and its page.</summary>
    public void Reset()
    {
        _state?.Page.Dispose();
        _state = null;
    }
}
