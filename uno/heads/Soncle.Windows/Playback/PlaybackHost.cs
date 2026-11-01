// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App;
using Soncle.App.Playback;
using Soncle.App.ViewModels;
using Soncle.Core.Playback;
using Soncle.YouTube;

namespace Soncle.Windows.Playback;

/// <summary>
/// The Windows head's composition root: the shared <see cref="PlaybackSession"/> with this head's
/// sound device (<see cref="WasapiAudioOutput"/>), its own cache folder and its hidden WebView2
/// BotGuard page. Everything else - search, resolve, download, streaming decode, the engine and the
/// view models - lives in <c>Soncle.App</c> and is shared with the Android head.
/// </summary>
internal sealed class PlaybackHost : IDisposable
{
    private readonly PlaybackSession _session;

    public PlaybackHost(BotGuardPageFactory? botGuard = null)
    {
        // The preview's own folder, never the shipping app's %APPDATA%\Soncle.
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Soncle Preview", "streams");
        _session = new PlaybackSession(new WasapiAudioOutput(), cache, botGuard, Log);
        if (botGuard is not null && Environment.GetEnvironmentVariable("SONCLE_POTOKEN_SELFTEST") == "1")
            _ = _session.PoTokenSelfTestAsync();
    }

    public PlaybackSession Session => _session;
    public EnginePlayer Player => _session.Player;
    public PlaybackController Controller => _session.Controller;
    public SearchViewModel Search => _session.Search;
    public PlayerBarViewModel Bar => _session.Bar;
    public WasapiAudioOutput Output => (WasapiAudioOutput)_session.Output;

    /// <summary>The UI timer's work (~4 Hz): end-of-song detection, then the player bar.</summary>
    public void Tick() => _session.Tick();

    public void Dispose() => _session.Dispose();

    /// <summary>One line for the debugger / trace listeners; URLs are cut out (they carry tokens).</summary>
    public static void Log(string message) =>
        System.Diagnostics.Trace.WriteLine("Soncle " + PlaybackSession.Redact(message));
}
