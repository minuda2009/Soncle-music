// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Soncle.Core.Models;

namespace Soncle.Services;

/// <summary>What the player is doing, as Discord Rich Presence needs it (a port of the JS state).</summary>
public sealed record DiscordState(Track? Track, bool Playing, double Position, double Duration);

/// <summary>The local Discord IPC socket (the seam, so tests use a fake and never touch Discord).</summary>
public interface IDiscordTransport : IAsyncDisposable
{
    /// <summary>True when the socket opened.</summary>
    bool Connected { get; }
    Task<bool> ConnectAsync(int index, CancellationToken ct);
    Task SendAsync(byte[] frame, CancellationToken ct);
    Task<byte[]?> ReceiveAsync(CancellationToken ct);
}

/// <summary>Creates a transport for IPC pipe <paramref name="index"/> (0–9).</summary>
public delegate IDiscordTransport DiscordTransportFactory(int index);

/// <summary>
/// Discord Rich Presence over the local IPC pipe (no dependencies). A port of
/// <c>src/discord.mjs</c>: opcode-framed JSON, a READY handshake, a 20 s retry when the socket
/// drops, and artwork forwarded only from known YouTube image hosts. Soncle has no Discord
/// application id registered yet, so this stays off until one is configured (as in the JS, an empty
/// id disables it).
/// </summary>
public sealed partial class DiscordRpc : IAsyncDisposable
{
    private const int MaxPipe = 9;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);

    private readonly string _clientId;
    private readonly DiscordTransportFactory _factory;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();

    private IDiscordTransport? _transport;
    private bool _ready;
    private bool _enabled;
    private object? _pending;
    private bool _hasPending;
    private CancellationTokenSource? _retryCts;

    public DiscordRpc(string? clientId, DiscordTransportFactory factory, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _clientId = clientId ?? "";
        _factory = factory;
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    /// <summary>True when an application id is configured (the JS's <c>DISCORD_AVAILABLE</c>).</summary>
    public bool Available => _clientId.Length > 0;

    public bool Enabled { get { lock (_gate) return _enabled; } }

    /// <summary>Reads the id from the environment, as the JS does (<c>SONCLE_DISCORD_CLIENT_ID</c>).</summary>
    public static DiscordRpc FromEnvironment(DiscordTransportFactory factory) =>
        new(Environment.GetEnvironmentVariable("SONCLE_DISCORD_CLIENT_ID"), factory);

    public void SetEnabled(bool on)
    {
        lock (_gate)
        {
            if (on && !Available) { _enabled = false; return; }   // no app id configured
            _enabled = on;
        }
        if (on) _ = ConnectAsync(0, CancellationToken.None);
        else
        {
            _ = ClearThenDestroyAsync();
        }
    }

    private async Task ClearThenDestroyAsync()
    {
        await ClearAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);
        await DestroyAsync().ConfigureAwait(false);
    }

    public async Task DestroyAsync()
    {
        CancellationTokenSource? cts;
        IDiscordTransport? t;
        lock (_gate)
        {
            cts = _retryCts; _retryCts = null;
            _ready = false;
            t = _transport; _transport = null;
        }
        cts?.Cancel();
        if (t is not null) await t.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) _enabled = false;
        await DestroyAsync().ConfigureAwait(false);
    }

    public async Task ConnectAsync(int index, CancellationToken ct)
    {
        IDiscordTransport? existing;
        bool enabled;
        lock (_gate) { existing = _transport; enabled = _enabled; }
        if (!enabled || existing is not null) return;
        if (index > MaxPipe) { ScheduleRetry(); return; }

        var transport = _factory(index);
        var connected = await transport.ConnectAsync(index, ct).ConfigureAwait(false);
        if (!connected)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            // try the next pipe index, as the JS walks 0..9
            await ConnectAsync(index + 1, ct).ConfigureAwait(false);
            return;
        }

        lock (_gate) { _transport = transport; }
        await transport.SendAsync(Frame(0, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = _clientId })), ct).ConfigureAwait(false);
        _ = ReadLoopAsync(transport, ct);
    }

    private async Task ReadLoopAsync(IDiscordTransport transport, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var chunk = await transport.ReceiveAsync(ct).ConfigureAwait(false);
                if (chunk is null || chunk.Length == 0) break;
                foreach (var (op, json) in Frames(chunk))
                {
                    if (op == 1 && JsonEvtIsReady(json))
                    {
                        bool resend;
                        lock (_gate) { _ready = true; resend = _hasPending; }
                        if (resend) await SendPendingAsync(ct).ConfigureAwait(false);
                    }
                    else if (op == 2)
                    {
                        await DestroyAsync().ConfigureAwait(false);
                        ScheduleRetry();
                        return;
                    }
                }
            }
        }
        catch { /* a dropped socket is retried below */ }
        lock (_gate) { if (ReferenceEquals(_transport, transport)) { _transport = null; _ready = false; } }
        ScheduleRetry();
    }

    private void ScheduleRetry()
    {
        bool enabled;
        lock (_gate) enabled = _enabled;
        if (!enabled) return;
        var cts = new CancellationTokenSource();
        lock (_gate) { _retryCts?.Cancel(); _retryCts?.Dispose(); _retryCts = cts; }
        _ = Task.Run(async () =>
        {
            try
            {
                await _delay(RetryDelay, cts.Token).ConfigureAwait(false);
                await ConnectAsync(0, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        });
    }

    private async Task SendPendingAsync(CancellationToken ct)
    {
        object? activity;
        lock (_gate) { activity = _pending; }
        await SendAsync(activity, ct).ConfigureAwait(false);
    }

    private async Task SendAsync(object? activity, CancellationToken ct)
    {
        IDiscordTransport? t;
        bool ready;
        lock (_gate) { t = _transport; ready = _ready; }
        // Not connected yet: leave the pending update queued, and the READY handler sends it.
        if (t is null || !ready) return;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity },
            nonce = Guid.NewGuid().ToString(),
        });
        try
        {
            await t.SendAsync(Frame(1, payload), ct).ConfigureAwait(false);
            lock (_gate) _hasPending = false;
        }
        catch { }
    }

    /// <summary>Updates the presence. A playing track shows title/artists and a live timer.</summary>
    public void Update(DiscordState state)
    {
        bool enabled;
        lock (_gate) enabled = _enabled;
        if (!enabled) return;

        var t = state.Track;
        var activity = t is not null && state.Playing ? (object)BuildActivity(state) : null;

        IDiscordTransport? transport;
        lock (_gate)
        {
            // Queue it even when it is a clear (null): the JS's `pending !== undefined` rule, so an
            // update that arrives before READY is still sent.
            _pending = activity;
            _hasPending = true;
            transport = _transport;
        }
        if (transport is null) _ = ConnectAsync(0, CancellationToken.None);
        else _ = SendAsync(activity, CancellationToken.None);
    }

    /// <summary>Clears the presence (song stopped / paused).</summary>
    public Task ClearAsync()
    {
        IDiscordTransport? transport;
        lock (_gate)
        {
            _pending = null;
            _hasPending = true;
            transport = _transport;
        }
        if (transport is null) { _ = ConnectAsync(0, CancellationToken.None); return Task.CompletedTask; }
        return SendAsync(null, CancellationToken.None);
    }

    private static object BuildActivity(DiscordState state)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var start = now - (long)(state.Position * 1000);
        var t = state.Track!;
        var artists = t.Artists.Count > 0 ? string.Join(", ", t.Artists.Select(a => a.Name)) : "Soncle";
        return new Dictionary<string, object?>
        {
            ["type"] = 2,
            ["details"] = Clip(t.Title.Length > 0 ? t.Title : "Unknown", 127),
            ["state"] = Clip(artists.Length > 0 ? artists : "Soncle", 127),
            ["timestamps"] = state.Duration > 0
                ? new Dictionary<string, object?> { ["start"] = start, ["end"] = start + (long)(state.Duration * 1000) }
                : new Dictionary<string, object?> { ["start"] = start },
            ["assets"] = new Dictionary<string, object?>
            {
                ["large_image"] = t.Thumb is not null && ThumbHostRx().IsMatch(t.Thumb) ? t.Thumb : "soncle",
                ["large_text"] = Clip(t.Album ?? t.Title, 127),
                ["small_text"] = "Soncle",
            },
            ["buttons"] = new[] { new Dictionary<string, object?> { ["label"] = "Listen on YouTube Music", ["url"] = $"https://music.youtube.com/watch?v={t.Id}" } },
            ["instance"] = false,
        };
    }

    [GeneratedRegex(@"^https://(i\.ytimg\.com|lh\d\.googleusercontent\.com|yt\d\.ggpht\.com)/")]
    private static partial Regex ThumbHostRx();

    private static string Clip(string? s, int max) => (s ?? "")[..Math.Min((s ?? "").Length, max)];

    // ---------- framing (little-endian opcode + length, then JSON) ----------

    public static byte[] Frame(int op, byte[] json)
    {
        var buf = new byte[8 + json.Length];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), op);
        BitConverter.TryWriteBytes(buf.AsSpan(4, 4), json.Length);
        json.CopyTo(buf, 8);
        return buf;
    }

    /// <summary>Walks the frames in a received chunk.</summary>
    public static List<(int Op, string Json)> Frames(byte[] data)
    {
        var outList = new List<(int, string)>();
        var offset = 0;
        while (offset + 8 <= data.Length)
        {
            var op = BitConverter.ToInt32(data, offset);
            var len = BitConverter.ToInt32(data, offset + 4);
            if (len < 0 || offset + 8 + len > data.Length) break;
            var json = Encoding.UTF8.GetString(data, offset + 8, len);
            outList.Add((op, json));
            offset += 8 + len;
        }
        return outList;
    }

    private static bool JsonEvtIsReady(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("evt", out var e) && e.GetString() == "READY";
        }
        catch { return false; }
    }
}

/// <summary>Parses the environment flag the same way for every head.</summary>
public static class DiscordSettings
{
    public static bool EnabledFromEnvironment() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SONCLE_DISCORD_CLIENT_ID"));
}
