// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Soncle.App.Playback;
using Soncle.App.ViewModels;
using Soncle.Audio;
using Soncle.Core.Playback;
using Soncle.Streams;
using Soncle.YouTube;
using Soncle.YouTube.InnerTube;
using Soncle.YouTube.Parsing;

namespace Soncle.Windows.Playback;

/// <summary>
/// The composition root of the playback slice: search -> pick -> resolve -> download -> decode ->
/// engine -> WASAPI. Signed out, always: one <see cref="HttpClient"/> that never stores or sends
/// cookies, no account, no credentials.
/// <para>
/// Streams resolve with the clients that return plain URLs (ANDROID_VR, then IOS) and only
/// WebM/Opus formats (<see cref="YouTubeStreams.OpusOnly"/>): deciphering and PO tokens are not in
/// this head yet, so YTMUSIC / WEB would yield nothing playable, and AAC has no decoder yet.
/// </para>
/// <para>
/// The app must open offline, so nothing here touches the network at start-up: the InnerTube
/// session (visitor data) is created on the first search or song, and a failed attempt is retried
/// by the next one. Create it and use it on the UI thread (the view models capture it).
/// </para>
/// </summary>
internal sealed partial class PlaybackHost : IDisposable
{
    private static readonly string[] Clients = { "ANDROID_VR", "IOS" };

    private readonly HttpClient _http;
    private readonly SongDownloader _downloader;
    private readonly object _gate = new();
    private Task<Backend>? _backend;
    private bool _disposed;

    public PlaybackHost()
    {
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        // The preview's own folder, never the shipping app's %APPDATA%\Soncle. SongDownloader
        // empties it when it is created, so nothing from an earlier run is reused.
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Soncle Preview", "streams");
        _downloader = new SongDownloader(cache, new HttpStreamFetcher(_http));

        Output = new WasapiAudioOutput();
        Player = new EnginePlayer(Output, LoadSongAsync);
        Controller = new PlaybackController(Player, new LazyStreams(this), options: new PlaybackOptions());
        Search = new SearchViewModel((q, ct) => SearchAsync(q, ct), Controller);
        Bar = new PlayerBarViewModel(Controller, Player, Search.Find);
        Controller.Failed += (_, reason) => Log($"play-failed {reason}");
    }

    public WasapiAudioOutput Output { get; }
    public EnginePlayer Player { get; }
    public PlaybackController Controller { get; }
    public SearchViewModel Search { get; }
    public PlayerBarViewModel Bar { get; }

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
        Player.Dispose();
        Output.Dispose();
        _http.Dispose();
    }

    /// <summary>One line for the debugger / trace listeners; URLs are cut out (they carry tokens).</summary>
    public static void Log(string message) =>
        Trace.WriteLine("Soncle " + UrlPattern().Replace(message, "<url>"));

    [GeneratedRegex(@"\b\w+://\S+")]
    private static partial Regex UrlPattern();

    private async Task<YtSearchPage> SearchAsync(string query, CancellationToken ct)
    {
        var backend = await GetBackendAsync(ct);
        return await backend.Catalog.SearchAsync(query, "all", ct);
    }

    private async Task<IAudioSource> LoadSongAsync(string id, CancellationToken ct)
    {
        var backend = await GetBackendAsync(ct);
        return await backend.Streams.LoadSongAsync(id, ct);
    }

    private async Task<double?> PrefetchAsync(string id, CancellationToken ct)
    {
        var backend = await GetBackendAsync(ct);
        return await backend.Streams.PrefetchAsync(id, ct);
    }

    /// <summary>The session and everything built on it, created once; a failed attempt is forgotten.</summary>
    private async Task<Backend> GetBackendAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task<Backend> task;
        lock (_gate) task = _backend ??= CreateBackendAsync();
        try
        {
            return await task.WaitAsync(ct);
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
        return new Backend(new InnerTubeCatalogClient(_http, session), new YouTubeStreams(resolver, _downloader));
    }

    private sealed record Backend(InnerTubeCatalogClient Catalog, YouTubeStreams Streams);

    /// <summary>The controller's <see cref="IPlaybackStreams"/> until the session exists.</summary>
    private sealed class LazyStreams(PlaybackHost host) : IPlaybackStreams
    {
        public Task<double?> PrefetchAsync(string id, CancellationToken ct) => host.PrefetchAsync(id, ct);
    }
}
