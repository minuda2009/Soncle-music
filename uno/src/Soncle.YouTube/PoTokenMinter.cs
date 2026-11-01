// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;
using System.Text.Json.Nodes;

namespace Soncle.YouTube;

/// <summary>
/// The hidden BotGuard page (a head-provided WebView showing <c>src/po_token.html</c> with a
/// youtube.com origin) the minter drives. The head only loads the page and runs JS in it; the
/// BotGuard flow itself is <see cref="PoTokenMinter"/>. Tests use a fake.
/// </summary>
public interface IBotGuardPage : IDisposable
{
    /// <summary>
    /// Runs a JS expression in the helper page (it may evaluate to a promise, which is awaited) and
    /// returns its result as JSON (<c>executeJavaScript(code, true)</c> in <c>potoken.mjs</c>).
    /// Faults with the page's error message when the expression throws or the promise rejects.
    /// </summary>
    Task<JsonNode?> ExecAsync(string code, CancellationToken ct);
    /// <summary>False once the page (its WebView) is gone.</summary>
    bool IsAlive { get; }
}

/// <summary>Creates a fresh BotGuard page with the helper page loaded (the head builds a hidden WebView; tests return a fake).</summary>
public delegate Task<IBotGuardPage> BotGuardPageFactory(CancellationToken ct);

/// <summary>An HTTP request to the BotGuard endpoints: a POST with <paramref name="body"/>, or a GET when it is null (the interpreter script); the default uses HttpClient.</summary>
public delegate Task<(int Status, string Text)> BotGuardFetch(string url, IReadOnlyDictionary<string, string> headers, string? body, CancellationToken ct);

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
/// PO token generation with Google's BotGuard, a port of <c>src/potoken.mjs</c> (<c>init</c>,
/// <c>ready</c>, <c>mint</c>, <c>reset</c>, the idle close) over <c>src/botguard.mjs</c>
/// (<see cref="BotGuard"/>). <c>init</c>: fetch the challenge (<c>Create</c>), run it in the page
/// (<c>runBotGuard</c>, which keeps <c>webPoSignalOutput</c> as <c>window.__wpso</c>), send the
/// BotGuard response to <c>GenerateIT</c> for the integrity token, then
/// <c>createPoTokenMinter(window.__wpso, integrityToken)</c> once; <c>mint</c> calls
/// <c>obtainPoToken</c> per identifier and caches the token for the minter's lifetime (the TTL
/// minus 10 minutes, at least 5).
/// <para>The 0.1.3 rules: one minter shared by concurrent requests (a single init in flight, which
/// one caller's cancellation doesn't stop), init gives up after 25 s, a failed init fails fast for
/// 2 minutes, a failed mint retires its minter without breaking a mint still in flight on it (its
/// page closes once the last one finishes) and never touches a newer minter, and no helper page is
/// left behind (closed on a failed or timed-out init, also when it arrives late, on expiry, on
/// <see cref="Reset"/> and after 10 minutes without a request). Tokens are never logged.</para>
/// </summary>
public sealed class PoTokenMinter
{
    /// <summary>The user agent of the BotGuard requests and the helper page (<c>UA</c> in potoken.mjs).</summary>
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    /// <summary>The helper page's URL (<c>PAGE</c> in potoken.mjs); the head serves po_token.html there.</summary>
    public const string PageUrl = "https://www.youtube.com/soncle-potoken";
    public const string Origin = "https://www.youtube.com";
    public static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(25);
    /// <summary>Each script run in the page (<c>exec</c> in potoken.mjs).</summary>
    public static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan FailedCooldown = TimeSpan.FromMinutes(2);
    /// <summary><c>IDLE_CLOSE</c>: the hidden page is closed after this long without a request.</summary>
    public static readonly TimeSpan IdleClose = TimeSpan.FromMinutes(10);

    private readonly BotGuardPageFactory _factory;
    private readonly BotGuardFetch _fetch;
    private readonly IMinterClock _clock;
    private readonly Action<string> _log;
    private readonly object _gate = new();

    private sealed class Minter(IBotGuardPage page, long expiresMs)
    {
        public readonly IBotGuardPage Page = page;
        public readonly long ExpiresMs = expiresMs;
        public readonly Dictionary<string, string> Cache = new();
        public readonly CancellationTokenSource Idle = new();
        public int InFlight;
        public bool Retired;
    }

    private Minter? _state;
    private Task<Minter>? _starting;
    private CancellationTokenSource? _initCts;
    private long _failedAt = long.MinValue;
    private long _idleAt;

    public PoTokenMinter(BotGuardPageFactory factory, BotGuardFetch fetch, IMinterClock? clock = null, Action<string>? log = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        _clock = clock ?? new RealMinterClock();
        _log = log ?? (_ => { });
    }

    /// <summary>The BotGuard requests over <paramref name="http"/> (<c>net.fetch</c> in potoken.mjs): signed out, no cookies are added here.</summary>
    public static BotGuardFetch HttpFetch(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return async (url, headers, body, ct) =>
        {
            using var req = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url);
            if (body is not null) req.Content = new StringContent(body, Encoding.UTF8);
            foreach (var (k, v) in headers)
            {
                if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (req.Content is null) continue;
                    req.Content.Headers.Remove("Content-Type");
                    req.Content.Headers.TryAddWithoutValidation("Content-Type", v);
                }
                else req.Headers.TryAddWithoutValidation(k, v);
            }
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        };
    }

    /// <summary>A PO token for a visitor id or video id (<c>mint</c>). Fails fast for 2 minutes after a failed start.</summary>
    public async Task<string> MintAsync(string identifier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        Minter st;
        while (true)
        {
            st = await ReadyAsync().WaitAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (st.Retired) continue;   // retired (a failed mint, idle, Reset) since: get the next one
                Touch(st);
                if (st.Cache.TryGetValue(identifier, out var cached)) return cached;
                st.InFlight++;
                break;
            }
        }
        var bytes = Encoding.UTF8.GetBytes(identifier);
        var failed = false;
        try
        {
            var outNode = await Exec(st.Page, $"obtainPoToken(new Uint8Array({JsonArrayOf(bytes)})).then(function (u) {{ return Array.from(u); }})", ct).ConfigureAwait(false);
            var tok = BotGuard.TokenFromBytes(BytesOf(outNode) ?? throw new InvalidOperationException("PO token: the page returned no token"));
            lock (_gate) st.Cache[identifier] = tok;
            return tok;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            failed = true;
            throw new InvalidOperationException("PO token: mint failed", e);
        }
        finally
        {
            Release(st, retire: failed);
        }
    }

    /// <summary><c>ready()</c>: the current minter, or the one being started (shared by all callers).</summary>
    private Task<Minter> ReadyAsync()
    {
        IBotGuardPage? stale = null;
        try
        {
            lock (_gate)
            {
                var now = _clock.NowMs;
                if (_state is { } s)
                {
                    if (s.Page.IsAlive && now < s.ExpiresMs) return Task.FromResult(s);
                    stale = Retire(s);
                }
                if (_starting is { IsCompleted: false } running) return running;
                if (_failedAt != long.MinValue && now - _failedAt < FailedCooldown.TotalMilliseconds)
                    return Task.FromException<Minter>(new InvalidOperationException("PO token provider unavailable"));
                var cts = new CancellationTokenSource();
                _initCts = cts;
                _starting = StartAsync(cts);
                return _starting;
            }
        }
        finally
        {
            stale?.Dispose();
        }
    }

    private async Task<Minter> StartAsync(CancellationTokenSource cts)
    {
        await Task.Yield();   // never run the init under the caller's lock
        var init = InitAsync(cts.Token);
        using var timer = new CancellationTokenSource();
        var timeout = _clock.DelayAsync(InitTimeout, timer.Token);
        try
        {
            if (await Task.WhenAny(init, timeout).ConfigureAwait(false) != init)
            {
                cts.Cancel();
                // no orphan: a page that still arrives is closed; the late fault is observed
                _ = init.ContinueWith(static t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion) t.Result.Page.Dispose();
                    else _ = t.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new TimeoutException("PO token init timed out");
            }
            var st = await init.ConfigureAwait(false);
            var dispose = false;
            lock (_gate)
            {
                if (cts.IsCancellationRequested) dispose = true;   // Reset() during init
                else
                {
                    _state = st;
                    _failedAt = long.MinValue;
                    Touch(st);
                }
            }
            if (dispose)
            {
                st.Page.Dispose();
                throw new OperationCanceledException("PO token init was reset");
            }
            _ = IdleLoopAsync(st);
            return st;
        }
        catch
        {
            lock (_gate) _failedAt = _clock.NowMs;
            throw;
        }
        finally
        {
            timer.Cancel();
            lock (_gate) if (ReferenceEquals(_initCts, cts)) _initCts = null;
            cts.Dispose();
        }
    }

    /// <summary><c>init()</c> in potoken.mjs; the page is closed if anything fails.</summary>
    private async Task<Minter> InitAsync(CancellationToken ct)
    {
        var page = await _factory(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            string origin;
            try { origin = (await Exec(page, "location.origin", ct).ConfigureAwait(false))?.GetValue<string>() ?? "?"; }
            catch (Exception) when (!ct.IsCancellationRequested) { origin = "?"; }
            if (origin != Origin) _log("helper page origin is " + origin);

            var ch = BotGuard.ParseChallenge(await BgRequest(BotGuard.CreateUrl, ArrayOf(BotGuard.RequestKey), ct).ConfigureAwait(false));
            if (ch.Script is null && ch.ResourceUrl is { } resource)
            {
                var u = resource.StartsWith("//", StringComparison.Ordinal) ? "https:" + resource : resource;
                var (status, text) = await _fetch(u, new Dictionary<string, string> { ["User-Agent"] = UserAgent }, null, ct).ConfigureAwait(false);
                if (status is < 200 or >= 300 || string.IsNullOrEmpty(text)) throw new InvalidOperationException($"BotGuard interpreter HTTP {status}");
                ch = ch with { Script = text };
            }
            var bgResponse = await Exec(page, $"runBotGuard({BotGuard.ChallengeJson(ch).ToJsonString()}).then(function (r) {{ window.__wpso = r.webPoSignalOutput; return r.botguardResponse; }})", ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("BotGuard gave no response");
            var it = await BgRequest(BotGuard.GenerateItUrl, new JsonArray(BotGuard.RequestKey, bgResponse.DeepClone()).ToJsonString(), ct).ConfigureAwait(false);
            var token = it is JsonArray a && a.Count > 0 && a[0] is JsonValue v && v.TryGetValue<string>(out var s) ? s
                : throw new InvalidOperationException("BotGuard GenerateIT gave no integrity token");
            var bytes = BotGuard.B64Bytes(token);
            var ttl = BotGuard.Ttl(it is JsonArray b && b.Count > 1 ? b[1] : null);
            await Exec(page, $"createPoTokenMinter(window.__wpso, new Uint8Array({JsonArrayOf(bytes)})).then(function () {{ return true; }})", ct).ConfigureAwait(false);
            _log($"PO token minter ready (ttl {ttl}s)");
            return new Minter(page, _clock.NowMs + Math.Max(300, ttl - 600) * 1000L);
        }
        catch
        {
            page.Dispose();
            throw;
        }
    }

    /// <summary><c>exec</c>: a script in the page, given up after <see cref="ScriptTimeout"/>.</summary>
    private async Task<JsonNode?> Exec(IBotGuardPage page, string code, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var run = page.ExecAsync(code, timer.Token);
        var timeout = _clock.DelayAsync(ScriptTimeout, timer.Token);
        try
        {
            if (await Task.WhenAny(run, timeout).ConfigureAwait(false) != run)
            {
                ct.ThrowIfCancellationRequested();
                _ = run.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new TimeoutException("BotGuard script timed out");
            }
            return await run.ConfigureAwait(false);
        }
        finally
        {
            timer.Cancel();
        }
    }

    private async Task<JsonNode?> BgRequest(string url, string body, CancellationToken ct)
    {
        var (status, text) = await _fetch(url, BotGuard.Headers(UserAgent), body, ct).ConfigureAwait(false);
        if (status is < 200 or >= 300 || string.IsNullOrEmpty(text))
            throw new InvalidOperationException($"BotGuard {url.Split('/').Last()} HTTP {status}");
        return BotGuard.ParseBody(text);
    }

    private static string ArrayOf(params string[] items)
    {
        var arr = new JsonArray();
        foreach (var s in items) arr.Add(s);
        return arr.ToJsonString();
    }

    private static string JsonArrayOf(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 4 + 2).Append('[');
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(bytes[i]);
        }
        return sb.Append(']').ToString();
    }

    private static byte[]? BytesOf(JsonNode? node)
    {
        if (node is not JsonArray arr || arr.Count == 0) return null;
        var bytes = new byte[arr.Count];
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not JsonValue v || !v.TryGetValue<int>(out var b) || b is < 0 or > 255) return null;
            bytes[i] = (byte)b;
        }
        return bytes;
    }

    // ------------------------------------------------------------------ lifetime (under _gate)

    private void Touch(Minter st)
    {
        if (ReferenceEquals(_state, st)) _idleAt = _clock.NowMs + (long)IdleClose.TotalMilliseconds;
    }

    /// <summary>Takes <paramref name="st"/> out of use; returns its page if nothing is in flight on it (the caller disposes it outside the lock).</summary>
    private IBotGuardPage? Retire(Minter st)
    {
        if (ReferenceEquals(_state, st)) _state = null;
        if (st.Retired) return null;
        st.Retired = true;
        st.Idle.Cancel();
        return st.InFlight == 0 ? st.Page : null;
    }

    /// <summary>A mint on <paramref name="st"/> finished; a failure retires it (never a newer minter), and the last one out closes a retired page.</summary>
    private void Release(Minter st, bool retire)
    {
        IBotGuardPage? close = null;
        lock (_gate)
        {
            st.InFlight--;
            if (retire && !st.Retired) close = Retire(st);
            else if (st.Retired && st.InFlight == 0) close = st.Page;
        }
        close?.Dispose();
    }

    /// <summary>The idle close: 10 minutes after the last request the page is closed (rebuilt on the next one).</summary>
    private async Task IdleLoopAsync(Minter st)
    {
        try
        {
            while (true)
            {
                long wait;
                lock (_gate)
                {
                    if (st.Retired || !ReferenceEquals(_state, st)) return;
                    wait = _idleAt - _clock.NowMs;
                }
                if (wait <= 0)
                {
                    IBotGuardPage? close;
                    lock (_gate)
                    {
                        if (st.Retired || !ReferenceEquals(_state, st)) return;
                        close = Retire(st);
                    }
                    _log("closing idle PO token window");
                    close?.Dispose();
                    return;
                }
                await _clock.DelayAsync(TimeSpan.FromMilliseconds(wait), st.Idle.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary><c>reset()</c>: closes the minter and its page (a mint still in flight finishes first) and stops an init under way.</summary>
    public void Reset()
    {
        IBotGuardPage? close = null;
        CancellationTokenSource? init;
        lock (_gate)
        {
            if (_state is { } s) close = Retire(s);
            init = _initCts;
        }
        try { init?.Cancel(); } catch (ObjectDisposedException) { }   // the init just finished
        close?.Dispose();
    }
}
