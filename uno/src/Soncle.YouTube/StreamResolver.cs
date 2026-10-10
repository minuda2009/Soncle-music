// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.RegularExpressions;

namespace Soncle.YouTube;

/// <summary>One audio format returned by a player response.</summary>
public sealed record AudioFormat(string Url, string MimeType, int Bitrate, long ContentLength, bool IsDrc = false, bool IsDefaultAudioTrack = true, string? LoudnessDb = null);

/// <summary>A resolved, playable stream (the shape the downloader and engine consume).</summary>
public sealed record ResolvedStream(string Url, string Client, IReadOnlyDictionary<string, string> Headers, string UserAgent, string Mime, long Length, int Bitrate, string? LoudnessDb, long ExpiresAtMs);

/// <summary>A player response for one client: the playability status and its audio formats.</summary>
public sealed record PlayerResponse(string Status, string? Reason, IReadOnlyList<AudioFormat> AudioFormats);

/// <summary>
/// A stream client (the InnerTube + decipher side, a head/platform concern). Tests inject a fake.
/// <paramref name="potToken"/> is the player PO token when the spec needs one.
/// </summary>
public interface IStreamClient
{
    string UserAgent { get; }
    /// <summary>The User-Agent stream requests must carry for <paramref name="client"/> (<c>uaFor</c> in yt.mjs). Defaults to <see cref="UserAgent"/>.</summary>
    string UserAgentFor(string client) => UserAgent;
    Task<PlayerResponse> GetPlayerAsync(string videoId, string client, string? potToken, CancellationToken ct);
    /// <summary>Whether [start,end] of the URL is fetchable (the seek probe). Returns null on success, else the failure reason.</summary>
    Task<string?> CanSeekAsync(string client, string url, IReadOnlyDictionary<string, string> headers, long length, CancellationToken ct);
    Task<long> ProbeLengthAsync(string client, string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct);
}

/// <summary>Mints a PO token for an identifier (a visitor id or video id).</summary>
public delegate Task<string> PoTokenProvider(string identifier, CancellationToken ct);

/// <summary>One client spec in the resolution order.</summary>
public sealed record StreamSpec(string Client, bool Pot);

/// <summary>
/// Resolves a playable audio stream with the same client order, format choice, probing, cache and
/// rotating retry as <c>resolveStream</c>/<c>resolveInOrder</c> in <c>src/yt.mjs</c>. The InnerTube
/// and decipher calls are an <see cref="IStreamClient"/> (a head/platform concern); this class holds
/// the portable rules, so they are unit-testable with a fake client.
/// </summary>
public sealed class StreamResolver
{
    private const int CacheMax = 64;
    private const long MidProbe = 1_500_000;

    private readonly List<StreamSpec> _specs;
    private readonly IStreamClient _client;
    private readonly PoTokenProvider? _pot;
    private readonly Func<long> _nowMs;
    private readonly Action<string> _log;
    private readonly Dictionary<string, ResolvedStream> _cache = new();
    private readonly Dictionary<string, Task<ResolvedStream>> _pending = new();

    public StreamResolver(IStreamClient client, PoTokenProvider? pot = null, Func<long>? nowMs = null, Action<string>? log = null)
    {
        _client = client;
        _pot = pot;
        _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _log = log ?? (_ => { });
        _specs = new List<StreamSpec>
        {
            new("YTMUSIC", true), new("IOS", false), new("ANDROID_VR", false),
            new("WEB", true), new("TV", false), new("WEB_EMBEDDED", false),
        };
    }

    public IReadOnlyList<StreamSpec> Specs => _specs;
    public IReadOnlyList<string> Clients => _specs.Select(s => s.Client).ToList();

    /// <summary>Keeps only the named clients, in the given order (the phone has no BotGuard window).</summary>
    public void SetClients(IReadOnlyList<string> names)
    {
        var keep = _specs.Where(s => names.Contains(s.Client)).OrderBy(s => names.ToList().IndexOf(s.Client)).ToList();
        if (keep.Count == 0) return;
        _specs.Clear();
        _specs.AddRange(keep);
        _cache.Clear();
    }

    public void Invalidate(string videoId)
    {
        foreach (var k in _cache.Keys.Where(k => k.StartsWith(videoId + "|")).ToList()) _cache.Remove(k);
    }

    /// <summary>Resolve a playable stream, using the cache unless <paramref name="force"/>.</summary>
    public Task<ResolvedStream> ResolveAsync(string videoId, string quality = "best", bool force = false, CancellationToken ct = default)
    {
        var key = videoId + "|" + quality;
        if (!force && _cache.TryGetValue(key, out var cached) && cached.ExpiresAtMs > _nowMs()) return Task.FromResult(cached);
        if (!force && _pending.TryGetValue(key, out var pending)) return pending;
        var job = ResolveInOrderAsync(videoId, _specs, quality, ct);
        _pending[key] = job;
        return job.ContinueWith(t => { _pending.Remove(key); return t.GetAwaiter().GetResult(); }, ct);
    }

    /// <summary>Re-resolve with a client different from <paramref name="exclude"/> (a mid-playback 403).</summary>
    public Task<ResolvedStream> ResolveRotatingAsync(string videoId, string exclude, string quality = "best", CancellationToken ct = default)
    {
        var order = _specs.Where(s => s.Client != exclude).Concat(_specs.Where(s => s.Client == exclude)).ToList();
        return ResolveInOrderAsync(videoId, order, quality, ct);
    }

    private async Task<ResolvedStream> ResolveInOrderAsync(string videoId, IReadOnlyList<StreamSpec> specs, string quality, CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var spec in specs)
        {
            foreach (var swap in spec.Pot ? new[] { false, true } : new[] { false })
            {
                try
                {
                    var outv = await TryClientAsync(videoId, spec, quality, swap, ct);
                    if (_cache.Count >= CacheMax) _cache.Remove(_cache.Keys.First());
                    _cache[videoId + "|" + quality] = outv;
                    _log($"stream {videoId} len={outv.Length} via {outv.Client}{(spec.Pot ? (swap ? "+pot(swapped)" : "+pot") : "")}");
                    return outv;
                }
                catch (Exception e)
                {
                    errors.Add($"{spec.Client}{(spec.Pot ? "+pot" : "")}: {e.Message}");
                    // a PO-token failure means no point retrying the token clients
                    if (Regex.IsMatch(e.Message, "PO token|BotGuard|provider")) break;
                }
            }
        }
        throw new InvalidOperationException("Could not load a playable stream. " + string.Join(" | ", errors));
    }

    private async Task<ResolvedStream> TryClientAsync(string videoId, StreamSpec spec, string quality, bool swapPot, CancellationToken ct)
    {
        string? playerPot = null, streamPot = null;
        if (spec.Pot)
        {
            if (_pot is null) throw new InvalidOperationException("no PO token provider");
            var visitor = "";
            var a = await _pot(visitor, ct);
            var b = await _pot(videoId, ct);
            (playerPot, streamPot) = swapPot ? (b, a) : (a, b);
        }
        var info = await _client.GetPlayerAsync(videoId, spec.Client, playerPot, ct);
        if (info.Status.Length > 0 && info.Status != "OK") throw new InvalidOperationException(info.Reason ?? info.Status);
        var fmts = info.AudioFormats.ToList();
        if (fmts.Count == 0) throw new InvalidOperationException("no audio formats");
        // prefer original-language, non-DRC tracks
        var pool = fmts.Where(f => !f.IsDrc && f.IsDefaultAudioTrack).ToList();
        var cands = (pool.Count > 0 ? pool : fmts).OrderByDescending(f => f.Bitrate).ToList();
        var fmt = quality == "low" ? cands[^1] : cands[0];
        var url = fmt.Url;
        if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("no url");
        if (streamPot is not null) url = WithQuery(url, "pot", streamPot);
        var ua = _client.UserAgentFor(spec.Client);
        var headers = HeadersFor(spec.Client, ua);
        var length = fmt.ContentLength;
        if (length == 0 && TryQueryLong(url, "clen") is { } clen && clen > 0) length = clen;
        if (length == 0) length = await _client.ProbeLengthAsync(spec.Client, url, headers, ct);
        var seek = await _client.CanSeekAsync(spec.Client, url, headers, length, ct);
        if (seek is not null) throw new InvalidOperationException($"range probe failed ({seek})");
        var expSec = TryQueryLong(url, "expire") ?? 0;
        var expires = expSec > 0 ? expSec * 1000 : _nowMs() + 3 * 3600_000;
        return new ResolvedStream(url, spec.Client, headers, ua, fmt.MimeType, length, fmt.Bitrate, fmt.LoudnessDb, expires - 10 * 60_000);
    }

    private static IReadOnlyDictionary<string, string> HeadersFor(string client, string ua)
    {
        var h = new Dictionary<string, string> { ["User-Agent"] = ua };
        if (Regex.IsMatch(client, "^(WEB|YTMUSIC|MWEB|WEB_EMBEDDED)$"))
        {
            h["Origin"] = client == "YTMUSIC" ? "https://music.youtube.com" : "https://www.youtube.com";
            h["Referer"] = h["Origin"] + "/";
        }
        return h;
    }

    private static string WithQuery(string url, string key, string value)
    {
        var sep = url.Contains('?') ? '&' : '?';
        return url + sep + key + "=" + Uri.EscapeDataString(value);
    }

    private static long? TryQueryLong(string url, string key)
    {
        var m = Regex.Match(url, $"[?&]{Regex.Escape(key)}=(\\d+)");
        return m.Success ? long.Parse(m.Groups[1].Value) : null;
    }
}
