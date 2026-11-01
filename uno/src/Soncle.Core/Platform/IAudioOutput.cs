// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Core.Platform;

/// <summary>
/// Fills <paramref name="interleaved"/> (L, R, L, R, ...) with rendered audio and returns the number
/// of frames written. Frames past the returned count are played as silence; 0 means nothing more to
/// play right now. Called on the device's audio thread: it must not allocate, block or throw.
/// </summary>
public delegate int AudioRenderCallback(Span<float> interleaved);

/// <summary>
/// The sound device seam (WINUI_PLAN §1, platform seams). Each head implements it on its own
/// audio API (AudioTrack on Android, WASAPI on Windows) and pulls float PCM from a
/// <see cref="AudioRenderCallback"/>, normally <c>Soncle.Audio.Output.OutputPump.Fill</c>, so the
/// engine and its limiter stay in the shared libraries and no platform code leaks into them.
/// </summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>The device rate in Hz the callback is rendered at (the engine's rate).</summary>
    int SampleRate { get; }

    /// <summary>Interleaved channels per frame; Soncle always renders stereo, so 2.</summary>
    int Channels { get; }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Opens the device and starts pulling from <paramref name="render"/> on the device thread.
    /// Calling it while already running is ignored (the first callback stays).
    /// </summary>
    void Start(AudioRenderCallback render);

    /// <summary>Stops pulling and releases the device thread; safe to call when not running.</summary>
    void Stop();
}
