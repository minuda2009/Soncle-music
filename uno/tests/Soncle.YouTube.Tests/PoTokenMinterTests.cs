// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json.Nodes;
using Soncle.YouTube;

namespace Soncle.YouTube.Tests;

/// <summary>Fakes for the minter rules (one minter shared, fast-fail, timeout, idle close).</summary>
internal sealed class FakePage : IBotGuardPage
{
    public int Execs;
    public bool FailMint;
    public JsonNode? ExecResult = new JsonArray(1, 2, 3);
    public bool Disposed;
    public bool IsAlive => !Disposed;
    public Task<JsonNode?> ExecAsync(string code, CancellationToken ct)
    {
        Interlocked.Increment(ref Execs);
        if (FailMint) return Task.FromException<JsonNode?>(new InvalidOperationException("mint failed"));
        return Task.FromResult(ExecResult);
    }
    public void Dispose() => Disposed = true;
}

internal sealed class FakeClock : IMinterClock
{
    public long NowMs { get; private set; }
    public void Advance(TimeSpan d) => NowMs += (long)d.TotalMilliseconds;
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) { NowMs += (long)delay.TotalMilliseconds; return Task.CompletedTask; }
}

public class PoTokenMinterTests
{
    private static (PoTokenMinter M, Func<FakePage> Page, FakeClock Clock) New(int ttl = 3600, bool failCreate = false)
    {
        var clock = new FakeClock();
        var page = new FakePage();
        var created = 0;
        var minter = new PoTokenMinter(
            ct => { created++; return Task.FromResult<IBotGuardPage>(page); },
            (url, headers, body, ct) =>
            {
                if (failCreate) return Task.FromResult((500, ""));
                if (url.EndsWith("Create")) return Task.FromResult((200, CreateBody()));
                // GenerateIT: [tokenBytes, ttl]
                return Task.FromResult((200, $"""[[1,2,3],{ttl}]"""));
            },
            clock, _ => { });
        return (minter, () => page, clock);
    }

    // a valid /Create body: ["x", base64(challenge shifted by -97)]
    private static string CreateBody()
    {
        var cd = new JsonArray("msg", new JsonArray("js"), null, "hash", "prog", "gName", null, "blob").ToJsonString();
        var shifted = System.Text.Encoding.UTF8.GetBytes(cd).Select(b => (byte)((b - 97) & 0xff)).ToArray();
        var arr = new JsonArray("x", Convert.ToBase64String(shifted));
        return arr.ToJsonString();
    }

    [Fact]
    public async Task ConcurrentMintsShareOneInit()
    {
        var (m, page, _) = New();
        var a = m.MintAsync("visitor-a");
        var b = m.MintAsync("visitor-b");
        await Task.WhenAll(a, b);
        Assert.False(string.IsNullOrEmpty(await a));
        Assert.False(string.IsNullOrEmpty(await b));
        Assert.Equal(2, page().Execs);               // one exec per mint, but only one init
    }

    [Fact]
    public async Task ASecondMintOfTheSameIdIsCached()
    {
        var (m, page, _) = New();
        var first = await m.MintAsync("v");
        var second = await m.MintAsync("v");
        Assert.Equal(first, second);
        Assert.Equal(1, page().Execs);               // the second call is served from the cache
    }

    [Fact]
    public async Task AFailedInitFailsFastForTwoMinutes()
    {
        var (m, _, clock) = New(failCreate: true);
        await Assert.ThrowsAnyAsync<Exception>(() => m.MintAsync("v"));
        // within the cooldown it fails fast without trying again
        await Assert.ThrowsAsync<InvalidOperationException>(() => m.MintAsync("v"));
        clock.Advance(TimeSpan.FromMinutes(3));
        // after the cooldown it tries again (still failing here, but it did try)
        await Assert.ThrowsAnyAsync<Exception>(() => m.MintAsync("v"));
    }

    [Fact]
    public async Task AFailedMintDoesNotNullANewerMinter()
    {
        var (m, page, _) = New();
        await m.MintAsync("ok");                     // establishes the minter
        page().FailMint = true;
        await Assert.ThrowsAnyAsync<Exception>(() => m.MintAsync("boom"));
        // the minter is still there for the next identifier
        page().FailMint = false;
        var tok = await m.MintAsync("next");
        Assert.False(string.IsNullOrEmpty(tok));
    }

    [Fact]
    public async Task TheIdleCloseClosesThePage()
    {
        var (m, page, clock) = New();
        await m.MintAsync("v");
        Assert.False(page().Disposed);
        clock.Advance(TimeSpan.FromMinutes(11));
        m.IdleTick();
        Assert.True(page().Disposed);
    }
}
