// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Soncle.Core.Models;
using Soncle.Services;

namespace Soncle.Services.Tests;

/// <summary>
/// Discord Rich Presence: the wire framing, the activity built from a track (artwork only from
/// known YouTube hosts), the enable/disable rules and the retry when the socket drops. A fake
/// transport stands in for the IPC pipe, so nothing here touches Discord.
/// </summary>
public class DiscordRpcTests
{
    [Fact]
    public void FrameIsLittleEndianOpcodeAndLengthThenJson()
    {
        var json = Encoding.UTF8.GetBytes("{\"a\":1}");
        var frame = DiscordRpc.Frame(1, json);
        Assert.Equal(1, BitConverter.ToInt32(frame, 0));
        Assert.Equal(json.Length, BitConverter.ToInt32(frame, 4));
        Assert.Equal(json, frame[8..]);
    }

    [Fact]
    public void FramesWalksAPartialChunk()
    {
        var a = DiscordRpc.Frame(0, Encoding.UTF8.GetBytes("{\"v\":1}"));
        var b = DiscordRpc.Frame(1, Encoding.UTF8.GetBytes("{\"evt\":\"READY\"}"));
        var both = a.Concat(b).ToArray();
        var frames = DiscordRpc.Frames(both);
        Assert.Equal(2, frames.Count);
        Assert.Equal(0, frames[0].Op);
        Assert.Equal("{\"v\":1}", frames[0].Json);
        Assert.Equal(1, frames[1].Op);
        // a trailing partial frame is ignored
        var partial = both.Concat(new byte[] { 1, 0, 0 }).ToArray();
        Assert.Equal(2, DiscordRpc.Frames(partial).Count);
    }

    [Fact]
    public void NoClientIdMeansUnavailableAndStaysOff()
    {
        var fake = new FakeTransport();
        var rpc = new DiscordRpc("", _ => fake);
        Assert.False(rpc.Available);
        rpc.SetEnabled(true);
        Assert.False(rpc.Enabled);
        Assert.Empty(fake.Sent);
    }

    [Fact]
    public async Task ConnectsSendsHandshakeAndSetsActivity()
    {
        var fake = new FakeTransport();
        var rpc = new DiscordRpc("123456", _ => fake);
        rpc.SetEnabled(true);
        await WaitUntil(() => fake.Sent.Count >= 1);

        // opcode 0 handshake with the client id
        var handshake = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(fake.Sent[0][8..]));
        Assert.Equal(1, handshake.GetProperty("v").GetInt32());
        Assert.Equal("123456", handshake.GetProperty("client_id").GetString());

        // the pipe reports READY, then an update goes out
        await fake.Emit(DiscordRpc.Frame(1, Encoding.UTF8.GetBytes("{\"evt\":\"READY\"}")));
        rpc.Update(new DiscordState(Track("dQw4w9WgXcQ", "Song", "https://i.ytimg.com/vi/x/hq.jpg"), true, 12, 200));
        await WaitUntil(() => fake.Sent.Count >= 2);

        var activity = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(fake.Sent[^1][8..]));
        Assert.Equal("SET_ACTIVITY", activity.GetProperty("cmd").GetString());
        var act = activity.GetProperty("args").GetProperty("activity");
        Assert.Equal("Song", act.GetProperty("details").GetString());
        Assert.Equal("Artist", act.GetProperty("state").GetString());
        Assert.Equal("https://i.ytimg.com/vi/x/hq.jpg", act.GetProperty("assets").GetProperty("large_image").GetString());
        Assert.Contains("dQw4w9WgXcQ", act.GetProperty("buttons")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task ArtworkFromAnUnknownHostIsNotForwarded()
    {
        var fake = new FakeTransport();
        var rpc = new DiscordRpc("123456", _ => fake);
        rpc.SetEnabled(true);
        await WaitUntil(() => fake.Sent.Count >= 1);
        await fake.Emit(DiscordRpc.Frame(1, Encoding.UTF8.GetBytes("{\"evt\":\"READY\"}")));
        rpc.Update(new DiscordState(Track("id", "Song", "https://evil.example.com/x.jpg"), true, 0, 100));
        await WaitUntil(() => fake.Sent.Count >= 2);
        var act = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(fake.Sent[^1][8..]))
            .GetProperty("args").GetProperty("activity");
        Assert.Equal("soncle", act.GetProperty("assets").GetProperty("large_image").GetString());
    }

    [Fact]
    public async Task PausedMeansNoActivity()
    {
        var fake = new FakeTransport();
        var rpc = new DiscordRpc("123456", _ => fake);
        rpc.SetEnabled(true);
        await WaitUntil(() => fake.Sent.Count >= 1);
        await fake.Emit(DiscordRpc.Frame(1, Encoding.UTF8.GetBytes("{\"evt\":\"READY\"}")));
        rpc.Update(new DiscordState(Track("id", "Song", "https://i.ytimg.com/vi/x/hq.jpg"), Playing: false, 0, 100));
        await WaitUntil(() => fake.Sent.Count >= 2);
        var activity = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(fake.Sent[^1][8..]))
            .GetProperty("args").GetProperty("activity");
        Assert.Equal(JsonValueKind.Null, activity.ValueKind);
    }

    [Fact]
    public async Task ACloseFrameRetiresTheSocketAndSchedulesARetry()
    {
        var fake = new FakeTransport();
        var rpc = new DiscordRpc("123456", _ => fake);
        rpc.SetEnabled(true);
        await WaitUntil(() => fake.Sent.Count >= 1);

        // opcode 2 = close: the transport is disposed and a retry is scheduled
        await fake.Emit(DiscordRpc.Frame(2, Encoding.UTF8.GetBytes("{}")));
        await WaitUntil(() => fake.Disposed);
        Assert.True(fake.Disposed);

        await rpc.DestroyAsync();
    }

    private static Track Track(string id, string title, string thumb) => new()
    {
        Id = id,
        Title = title,
        Thumb = thumb,
        Artists = new List<ArtistRef> { new() { Name = "Artist" } },
    };

    private static async Task WaitUntil(Func<bool> cond, int ms = 3000)
    {
        var start = Environment.TickCount64;
        while (!cond())
        {
            if (Environment.TickCount64 - start > ms) throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }

    private sealed class FakeTransport : IDiscordTransport
    {
        private readonly Queue<byte[]> _inbox = new();
        public List<byte[]> Sent { get; } = new();
        public bool Connected { get; private set; }
        public bool Disposed { get; private set; }

        public Task<bool> ConnectAsync(int index, CancellationToken ct)
        {
            Connected = true;
            return Task.FromResult(true);
        }

        public Task SendAsync(byte[] frame, CancellationToken ct)
        {
            lock (Sent) Sent.Add(frame);
            return Task.CompletedTask;
        }

        public async Task<byte[]?> ReceiveAsync(CancellationToken ct)
        {
            while (true)
            {
                lock (_inbox) { if (_inbox.Count > 0) return _inbox.Dequeue(); }
                if (Disposed) return null;
                await Task.Delay(5, ct);
            }
        }

        public Task Emit(byte[] frame)
        {
            lock (_inbox) _inbox.Enqueue(frame);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
