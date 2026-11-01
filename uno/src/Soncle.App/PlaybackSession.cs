// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Soncle.App.Playback;
using Soncle.App.ViewModels;
using Soncle.Audio;
using Soncle.Core.Platform;
using Soncle.Core.Playback;
using Soncle.Streams;
using Soncle.YouTube;
using Soncle.YouTube.InnerTube;
using Soncle.YouTube.Parsing;

namespace Soncle.App;

/// <summary>
/// The composition root of the playback slice, shared by every head: search -> pick -> resolve ->
/// download + streaming decode -> engine -> the head's <see cref="IAudioOutput"/>. Signed out,
/// always: one <see cref="HttpClient"/> that never stores or sends cookies, no account, no
/// credentials.
/// <para>
/// Streams resolve with the clients that return plain URLs (ANDROID_VR, then IOS) and only
/// WebM/Opus formats (<see cref="YouTubeStreams.OpusOnly"/>): deciphering and PO tokens are not in
/// the heads yet, so YTMUSIC / WEB would yield nothing playable, and AAC has no decoder yet.
/// </para>
/// <para>
/// The app must open offline, so nothing here touches the network at start-up: the InnerTube
/// session (visitor data) is created on the first search or song, and a failed attempt is retried
/// by the next one. Create it and use it on the UI thread (the view models capture it).
/// </para>
/// </summary>
public sealed partial class PlaybackSession : IDisposable
{
    private static readonly string[] Clients = { "ANDROID_VR", "IOS" };

    private readonly HttpClient _http;
    private readonly SongDownloader _downloader;
    private readonly object _gate = new();
    private Task<Backend>? _backend;
    private bool _disposed;

    /// <param name="output">The head's sound device (WASAPI, AudioTrack); its rate is the engine's.</param>
    /// <param name="cacheDirectory">Where finished songs are kept; the head picks a private folder.</param>
    /// <param name="botGuard">The head's hidden BotGuard page, if it has one (PO tokens are not used for playback yet).</param>
    /// <param name="log">One-line diagnostics; URLs are stripped before they reach it.</param>
    /// <param name="handler">
    /// The HTTP handler; null = a real <see cref="HttpClientHandler"/>. Tests inject one to prove the
    /// app opens without touching the network.
    /// </param>
    public PlaybackSession(IAudioOutput output, string cacheDirectory, BotGuardPageFactory? botGuard = null, Action<string>? log = null, HttpMessageHandler? handler = null)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Log = log ?? (_ => { });
        _http = new HttpClient(handler ?? new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        _downloader = new SongDownloader(cacheDirectory, new HttpStreamFetcher(_http));
        Player = new EnginePlayer(Output, LoadSongAsync);
        Controller = new PlaybackController(Player, new LazyStreams(this), options: new PlaybackOptions());
        Search = new SearchViewModel((q, ct) => SearchAsync(q, ct), Controller);
        Bar = new PlayerBarViewModel(Controller, Player, Search.Find);
        Controller.Failed += (_, reason) => Log($"play-failed {reason}");
        if (botGuard is not null) PoTokens = new PoTokenMinter(botGuard, PoTokenMinter.HttpFetch(_http), log: Log);
    }

    /// <summary>The PO-token minter, or null without a BotGuard page; not used for playback yet.</summary>
    public PoTokenMinter? PoTokens { get; }

    public IAudioOutput Output { get; }
    public EnginePlayer Player { get; }
    public PlaybackController Controller { get; }
    public SearchViewModel Search { get; }
    public PlayerBarViewModel Bar { get; }

    /// <summary>One line for the debugger / trace listeners; URLs are cut out (they carry tokens).</summary>
    public Action<string> Log { get; }

    /// <summary>Writes a diagnostic line with URLs removed, the way the heads do.</summary>
    public static string Redact(string message) => UrlPattern().Replace(message, "<url>");

    [GeneratedRegex(@"\b\w+://\S+")]
    private static partial Regex UrlPattern();

    /// <summary>The UI timer's work (~4 Hz): end-of-song detection, then the player bar.</summary>
    public void Tick()
    {
        Player.Tick();
        Bar.Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PoTokens?.Reset();
        Player.Dispose();
        Output.Dispose();
        _http.Dispose();
    }

    /// <summary>Mints one token for the visitor id; logs the outcome and the length, never the token.</summary>
    /// <remarks>Only runs when <c>SONCLE_POTOKEN_SELFTEST=1</c>; a check, not part of playback.</remarks>
    public async Task PoTokenSelfTestAsync()
    {
        if (PoTokens is not { } minter) { Log("potoken-selftest skipped (no BotGuard page)"); return; }
        var backend = await GetBackendAsync(CancellationToken.None).ConfigureAwait(false);
        if (string.IsNullOrEmpty(backend.VisitorData)) { Log("potoken-selftest skipped (no visitor id)"); return; }
        var watch = Stopwatch.StartNew();
        try
        {
            var token = await minter.MintAsync(backend.VisitorData).ConfigureAwait(false);
            Log($"potoken-selftest ok length={token.Length} ms={watch.ElapsedMilliseconds}");
        }
        catch (Exception e)
        {
            Log($"potoken-selftest failed {e.GetType().Name}: {e.Message}"
                + (e.InnerException is { } inner ? $" ({inner.GetType().Name}: {inner.Message})" : ""));
        }
    }

    private async Task<YtSearchPage> SearchAsync(string query, CancellationToken ct)
    {
        var backend = await GetBackendAsync(ct).ConfigureAwait(false);
        return await backend.Catalog.SearchAsync(query, "all", ct).ConfigureAwait(false);
    }

    private async Task<IAudioSource> LoadSongAsync(string id, CancellationToken ct)
    {
        var backend = await GetBackendAsync(ct).ConfigureAwait(false);
        return await backend.Streams.LoadSongAsync(id, ct).ConfigureAwait(false);
    }

    private async Task<double?> PrefetchAsync(string id, CancellationToken ct)
    {
        var backend = await GetBackendAsync(ct).ConfigureAwait(false);
        return await backend.Streams.PrefetchAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>The session and everything built on it, created once; a failed attempt is forgotten.</summary>
    private async Task<Backend> GetBackendAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task<Backend> task;
        lock (_gate) task = _backend ??= CreateBackendAsync();
        try
        {
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_backend, task)) _backend = null;
            }
            Log($"session-failed {e.GetType().Name}");
            throw;
        }
    }

    private async Task<Backend> CreateBackendAsync()
    {
        // not tied to one caller's token: a cancelled search must not spoil the shared session
        var session = await InnerTubeSession.CreateAsync(_http).ConfigureAwait(false);
        var resolver = new StreamResolver(new InnerTubeStreamClient(_http, session), accept: YouTubeStreams.OpusOnly);
        resolver.SetClients(Clients);
        Log($"session-ready from-server={session.FromServer} clients={string.Join(',', Clients)}");
        return new Backend(new InnerTubeCatalogClient(_http, session), new YouTubeStreams(resolver, _downloader), session.VisitorData);
    }

    private sealed record Backend(InnerTubeCatalogClient Catalog, YouTubeStreams Streams, string VisitorData);

    /// <summary>The controller's <see cref="IPlaybackStreams"/> until the session exists.</summary>
    private sealed class LazyStreams(PlaybackSession session) : IPlaybackStreams
    {
        public Task<double?> PrefetchAsync(string id, CancellationToken ct) => session.PrefetchAsync(id, ct);
    }
}
