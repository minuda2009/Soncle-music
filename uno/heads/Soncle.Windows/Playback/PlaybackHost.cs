// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Soncle.App.Playback;
using Soncle.App.ViewModels;
using Soncle.Audio;
using Soncle.Core.Playback;
using Soncle.Services;
using Soncle.Streams;
using Soncle.YouTube;
using Soncle.YouTube.InnerTube;
using Soncle.YouTube.Parsing;

namespace Soncle.Windows.Playback;

/// <summary>
/// The composition root of the playback slice: search -> pick -> resolve -> download + streaming decode ->
/// engine -> WASAPI. Signed out, always: one <see cref="HttpClient"/> that never stores or sends
/// cookies, no account, no credentials.
/// <para>
/// Streams resolve with the clients that return plain URLs (ANDROID_VR, then IOS) and only
/// WebM/Opus formats (<see cref="YouTubeStreams.OpusOnly"/>): deciphering and PO tokens are not in
/// this head yet, so YTMUSIC / WEB would yield nothing playable, and AAC has no decoder yet.
/// </para>
/// <para>
/// The PO-token minter (<see cref="PoTokens"/>, over the hidden WebView2 page in
/// <see cref="WebViewBotGuardPage"/>) is built but not handed to the resolver: YTMUSIC / WEB stay
/// off until an <c>IPlayerScript</c> decipherer exists, since their URLs need deciphering too.
/// Nothing mints a token unless <c>SONCLE_POTOKEN_SELFTEST=1</c> is set, which mints one for the
/// session's visitor id once and logs only whether it worked and the token's length.
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

    public PlaybackHost(BotGuardPageFactory? botGuard = null)
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
        Volume = new VolumeOutput(Output);
        Player = new EnginePlayer(Volume, LoadSongAsync);
        Controller = new PlaybackController(Player, new LazyStreams(this), options: new PlaybackOptions());
        Search = new SearchViewModel((q, ct) => SearchAsync(q, ct), Controller);
        Bar = new PlayerBarViewModel(Controller, Player, Describe);
        Controller.Failed += (_, reason) => Log($"play-failed {reason}");
        if (botGuard is not null) PoTokens = new PoTokenMinter(botGuard, PoTokenMinter.HttpFetch(_http), log: Log);
    }

    /// <summary>The PO-token minter, or null without a BotGuard page; not used for playback yet.</summary>
    public PoTokenMinter? PoTokens { get; }

    public WasapiAudioOutput Output { get; }
    /// <summary>The player bar's volume, between the engine and the device.</summary>
    public VolumeOutput Volume { get; }
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
        PoTokens?.Reset();
        Player.Dispose();
        Output.Dispose();
        _http.Dispose();
    }

    /// <summary>One line for the debugger / trace listeners; URLs are cut out (they carry tokens).</summary>
    public static void Log(string message)
    {
        var line = "Soncle " + Redact(message);
        Trace.WriteLine(line);
        try
        {
            // %LOCALAPPDATA%\Soncle Preview\soncle.log, so a tester can send it; capped at ~1 MB
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soncle Preview");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "soncle.log");
            lock (LogGate)
            {
                if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Delete(file);
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never break the app
        }
    }

    private static readonly object LogGate = new();

    /// <summary>A message with URLs cut out (they carry tokens).</summary>
    public static string Redact(string message) => UrlPattern().Replace(message ?? "", "<url>");

    [GeneratedRegex(@"\b\w+://\S+")]
    private static partial Regex UrlPattern();

    private async Task<YtSearchPage> SearchAsync(string query, CancellationToken ct)
    {
        var backend = await GetBackendAsync(ct);
        return await backend.Catalog.SearchAsync(query, "all", ct);
    }

    // ---- catalog (the pages) -----------------------------------------------------------------

    /// <summary>The InnerTube catalog, once the session exists (created on first use).</summary>
    public async Task<InnerTubeCatalogClient> CatalogAsync(CancellationToken ct = default) =>
        (await GetBackendAsync(ct)).Catalog;

    /// <summary>A full search page: songs, albums, artists, playlists.</summary>
    public async Task<YtSearchPage> SearchPageAsync(string query, CancellationToken ct = default) =>
        await (await CatalogAsync(ct)).SearchAsync(query, "all", ct);

    // ---- queue --------------------------------------------------------------------------------

    private readonly Dictionary<string, SongItem> _known = new(StringComparer.Ordinal);

    /// <summary>What the player bar, Now Playing and the media session show for a queued id.</summary>
    public SongItem? Describe(string id) =>
        _known.TryGetValue(id, out var s) ? s : Search.Find(id);

    /// <summary>Raised on the UI thread when the queue's contents change (not on every song change).</summary>
    public event EventHandler? QueueChanged;

    /// <summary>Plays <paramref name="songs"/> from <paramref name="index"/> as the queue.</summary>
    public Task PlaySongsAsync(IReadOnlyList<SongItem> songs, int index)
    {
        if (songs.Count == 0) return Task.CompletedTask;
        foreach (var s in songs) _known[s.VideoId] = s;
        index = Math.Clamp(index, 0, songs.Count - 1);
        Controller.SetQueue(songs.Select(s => new QueueTrack(s.VideoId, s.Title, s.DurationSeconds)), index);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        return Controller.PlayAtAsync(index);
    }

    /// <summary>
    /// Plays one song now and fills the queue behind it with its YouTube Music radio, the way the
    /// YouTube Music app does. The radio arrives a moment later; if the user has already moved on,
    /// it is dropped.
    /// </summary>
    public async Task PlayWithRadioAsync(SongItem song)
    {
        await PlaySongsAsync(new[] { song }, 0);
        try
        {
            var catalog = await CatalogAsync();
            var radio = await catalog.UpNextAsync(song.VideoId);
            var rest = radio.Select(SongItem.From).OfType<SongItem>().Where(s => s.VideoId != song.VideoId).ToList();
            AppendIfStill(song.VideoId, rest);
        }
        catch (Exception e)
        {
            Log($"radio-failed {e.GetType().Name}");
        }
    }

    /// <summary>Plays a playlist's / album's radio (YouTube Music's shuffle or radio id).</summary>
    public async Task PlayRadioAsync(string playlistId, string? parameters = null)
    {
        var catalog = await CatalogAsync();
        var list = (await catalog.UpNextAsync(null, playlistId, parameters)).Select(SongItem.From).OfType<SongItem>().ToList();
        await PlaySongsAsync(list, 0);
    }

    /// <summary>Puts <paramref name="songs"/> right after the current song.</summary>
    public void PlayNext(IReadOnlyList<SongItem> songs)
    {
        if (songs.Count == 0) return;
        if (Controller.Current is null)
        {
            _ = PlaySongsAsync(songs, 0);
            return;
        }
        foreach (var s in songs) _known[s.VideoId] = s;
        var q = Controller.Queue.ToList();
        var at = Controller.Index;
        q.InsertRange(at + 1, songs.Select(s => new QueueTrack(s.VideoId, s.Title, s.DurationSeconds)));
        Controller.SetQueue(q, at);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds <paramref name="songs"/> at the end of the queue.</summary>
    public void AddToQueue(IReadOnlyList<SongItem> songs)
    {
        if (songs.Count == 0) return;
        if (Controller.Current is null)
        {
            _ = PlaySongsAsync(songs, 0);
            return;
        }
        foreach (var s in songs) _known[s.VideoId] = s;
        var q = Controller.Queue.ToList();
        q.AddRange(songs.Select(s => new QueueTrack(s.VideoId, s.Title, s.DurationSeconds)));
        Controller.SetQueue(q, Controller.Index);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AppendIfStill(string currentId, List<SongItem> rest)
    {
        if (rest.Count == 0 || Controller.Current?.Id != currentId) return;
        foreach (var s in rest) _known[s.VideoId] = s;
        var q = Controller.Queue.ToList();
        var at = Controller.Index;
        var have = new HashSet<string>(q.Select(t => t.Id), StringComparer.Ordinal);
        q.AddRange(rest.Where(s => have.Add(s.VideoId)).Select(s => new QueueTrack(s.VideoId, s.Title, s.DurationSeconds)));
        Controller.SetQueue(q, at);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- lyrics -------------------------------------------------------------------------------

    /// <summary>Synced (LRCLIB) or plain lyrics for a song; null when none were found.</summary>
    public async Task<LyricsFetchResult?> LyricsAsync(SongItem song, CancellationToken ct = default)
    {
        var artists = song.Artists.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await Lyrics.GetAsync(song.Title, string.IsNullOrEmpty(song.Album) ? null : song.Album, artists,
            song.DurationSeconds > 0 ? song.DurationSeconds : null,
            async (url, headers) =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                req.Headers.TryAddWithoutValidation("User-Agent", "Soncle (https://github.com/minuda2009/Soncle-music)");
                using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ((int)res.StatusCode, text);
            });
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
        if (PoTokens is { } minter && Environment.GetEnvironmentVariable("SONCLE_POTOKEN_SELFTEST") == "1")
            _ = PoTokenSelfTestAsync(minter, session.VisitorData);
        return new Backend(new InnerTubeCatalogClient(_http, session), new YouTubeStreams(resolver, _downloader));
    }

    /// <summary>Mints one token for the visitor id; logs the outcome and the length, never the token.</summary>
    private static async Task PoTokenSelfTestAsync(PoTokenMinter minter, string visitorData)
    {
        if (string.IsNullOrEmpty(visitorData))
        {
            Log("potoken-selftest skipped (no visitor id)");
            return;
        }
        var watch = Stopwatch.StartNew();
        try
        {
            var token = await minter.MintAsync(visitorData).ConfigureAwait(false);
            Log($"potoken-selftest ok length={token.Length} ms={watch.ElapsedMilliseconds}");
        }
        catch (Exception e)
        {
            Log($"potoken-selftest failed {e.GetType().Name}: {e.Message}" + (e.InnerException is { } inner ? $" ({inner.GetType().Name}: {inner.Message})" : ""));
        }
    }

    private sealed record Backend(InnerTubeCatalogClient Catalog, YouTubeStreams Streams);

    /// <summary>The controller's <see cref="IPlaybackStreams"/> until the session exists.</summary>
    private sealed class LazyStreams(PlaybackHost host) : IPlaybackStreams
    {
        public Task<double?> PrefetchAsync(string id, CancellationToken ct) => host.PrefetchAsync(id, ct);
    }
}
