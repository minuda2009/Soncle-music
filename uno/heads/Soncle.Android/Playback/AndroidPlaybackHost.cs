// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App;
using Soncle.App.Playback;
using Soncle.App.ViewModels;
using Soncle.Core.Playback;
using Soncle.Droid;

namespace Soncle.Droid.Playback;

/// <summary>
/// The Android head's composition root: the shared <see cref="PlaybackSession"/> with this head's
/// sound device (<see cref="AudioTrackOutput"/>), a cache folder in the app's own private storage,
/// and no BotGuard page yet (PO tokens are not used for playback). Everything else - search,
/// resolve, download, streaming decode, the engine and the view models - lives in
/// <c>Soncle.App</c> and is shared with the Windows head.
/// </summary>
internal sealed class AndroidPlaybackHost : IDisposable
{
    private readonly PlaybackSession _session;

    public AndroidPlaybackHost(string cacheDirectory)
    {
        _session = new PlaybackSession(new AudioTrackOutput(48000), cacheDirectory, botGuard: null, Log);
    }

    public PlaybackSession Session => _session;
    public EnginePlayer Player => _session.Player;
    public PlaybackController Controller => _session.Controller;
    public SearchViewModel Search => _session.Search;
    public PlayerBarViewModel Bar => _session.Bar;
    public AudioTrackOutput Output => (AudioTrackOutput)_session.Output;

    /// <summary>The UI timer's work (~4 Hz): end-of-song detection, then the player bar.</summary>
    public void Tick() => _session.Tick();

    public void Dispose() => _session.Dispose();

    /// <summary>One line to logcat; URLs are cut out (they carry tokens).</summary>
    public static void Log(string message) =>
        global::Android.Util.Log.Info(StartupClock.LogTag, PlaybackSession.Redact(message));
}
