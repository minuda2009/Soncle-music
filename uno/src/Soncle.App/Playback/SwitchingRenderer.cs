// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Audio.Output;

namespace Soncle.App.Playback;

/// <summary>
/// One loaded song, built off the audio thread: its own <see cref="Engine"/> (so the master chain
/// and the limiter stay last), the <see cref="OutputPump"/> over it and the (decoded or streaming) source.
/// </summary>
internal sealed class LoadedSong
{
    public LoadedSong(IAudioSource source, double? lufs, double startAtSeconds)
    {
        Source = source;
        Lufs = lufs;
        SampleRate = source.SampleRate;
        Engine = new Engine(source.SampleRate);
        Engine.SetNormalize(true, EnginePlayer.NormTarget);
        Engine.Load(source, lufs, startAtSeconds);
        Pump = new OutputPump(Engine) { Source = source };
        Flush = new float[Engine.Master.Limiter.Delay * 2];
    }

    /// <summary>
    /// Scratch for one limiter delay of output, rendered and dropped after a seek so the limiter's
    /// look-ahead (audio from before the jump) is never heard.
    /// </summary>
    public float[] Flush { get; }

    public Engine Engine { get; }
    public OutputPump Pump { get; }
    public IAudioSource Source { get; }
    public double? Lufs { get; }
    public int SampleRate { get; }

    /// <summary>The song's length, frames (0 = unknown). Live: a streaming source's estimate becomes exact at its end.</summary>
    public long LengthFrames => Source.LengthFrames ?? 0;

    /// <summary>Stops a streaming source's decoding (reading it afterwards stays safe).</summary>
    public void Release() => (Source as IDisposable)?.Dispose();

    /// <summary>
    /// A seek waiting for the render thread, in frames (-1 = none). Written by the UI thread with
    /// <see cref="Interlocked.Exchange(ref long, long)"/>, consumed at the top of a render block.
    /// </summary>
    public long SeekFrame = -1;

    /// <summary>
    /// Whether the hand-over (to this song, or to <see cref="SeekFrame"/>) plays one block of what
    /// was playing ramped down first. Only when the device is running: after a pause there is
    /// nothing audible to fade, and a stale block would be heard as a blip.
    /// </summary>
    public volatile bool RampOut;
}

/// <summary>
/// The one stable <see cref="Soncle.Core.Platform.AudioRenderCallback"/> an <c>IAudioOutput</c>
/// pulls from. A new song or a seek is handed over through a single reference/long exchange, so
/// the audio thread never allocates, locks or raises events:
/// <list type="bullet">
/// <item>a published song replaces the current one after one block of the old song ramped 1 → 0
/// (the new engine fades itself in over <see cref="Deck.FadeFrames"/>);</item>
/// <item>a seek renders one block ramped 1 → 0 at the old position, then reloads the source on
/// the same engine at the new frame, which fades in again, and drops one limiter delay of output
/// (the look-ahead still holding pre-jump audio): no click either way.</item>
/// <item>while the device is stopped (paused) both skip the ramp-out block, so resuming never
/// plays a stale block first.</item>
/// </list>
/// Only <see cref="GainRamp"/> (attenuation) runs after the engine, so the limiter stays last.
/// </summary>
internal sealed class SwitchingRenderer
{
    private const int Channels = 2;

    private LoadedSong? _next;      // published by the UI thread, taken by the render thread
    private LoadedSong? _current;   // render thread only

    /// <summary>
    /// Hands <paramref name="song"/> to the render thread (any thread; never blocks).
    /// <paramref name="rampOut"/>: the device is running, so fade the old song out over one block.
    /// </summary>
    public void Publish(LoadedSong song, bool rampOut)
    {
        song.RampOut = rampOut;
        Volatile.Write(ref _next, song);
    }

    /// <summary>The song the render thread is playing (diagnostics and tests).</summary>
    public LoadedSong? Current => Volatile.Read(ref _current);

    /// <summary>The render callback. Never allocates, locks or throws for a valid buffer.</summary>
    public int Render(Span<float> interleaved)
    {
        var frames = interleaved.Length / Channels;
        var cur = _current;
        var next = Interlocked.Exchange(ref _next, null);
        if (next is not null)
        {
            if (cur is not null && frames > 0 && next.RampOut)
            {
                // play the old song out over this block, then switch on the next one
                cur.Pump.Fill(interleaved);
                GainRamp.Apply(interleaved, Channels, 1, 0);
                Volatile.Write(ref _current, next);
                return frames;
            }
            Volatile.Write(ref _current, next);
            cur = next;
        }

        if (cur is null)
        {
            interleaved.Clear();
            return frames;
        }

        var seek = Interlocked.Exchange(ref cur.SeekFrame, -1);
        if (seek >= 0)
        {
            var rampOut = cur.RampOut;
            if (rampOut && frames > 0)
            {
                // fade out at the old position first
                cur.Pump.Fill(interleaved);
                GainRamp.Apply(interleaved, Channels, 1, 0);
            }
            // restart the deck at the new frame (the engine fades it in from 0), then drop one
            // limiter delay of output: it still holds audio from before the jump
            cur.Source.Seek(seek);
            cur.Engine.Load(cur.Source, cur.Lufs, seek / (double)cur.SampleRate);
            cur.Source.Seek(seek);
            if (cur.Flush.Length > 0) cur.Pump.Fill(cur.Flush);
            if (rampOut && frames > 0) return frames;
        }
        cur.Pump.Fill(interleaved);
        return frames;
    }
}
