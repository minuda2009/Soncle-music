// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Android.Media;
using Soncle.Audio.Output;
using Soncle.Core.Platform;

namespace Soncle.Droid;

/// <summary>Why <see cref="AudioTrackOutput"/> stopped without being asked to.</summary>
public enum AudioInterruption
{
    /// <summary>Another app took audio focus.</summary>
    FocusLost,

    /// <summary>The device rejected a write or the audio thread failed.</summary>
    DeviceFailed,
}

/// <summary>
/// <see cref="IAudioOutput"/> on Android's <see cref="AudioTrack"/>: 32-bit float PCM, stereo, at
/// the engine's rate (48 kHz), streamed from one dedicated thread that pulls the render callback
/// into a preallocated buffer and writes it blocking. It asks for audio focus before it starts and
/// stops itself when another app takes focus.
/// <para>
/// Each <see cref="Start"/> opens one session (track, thread, buffer). The session's own thread
/// owns its teardown: <see cref="Stop"/> only signals it, so callers on the main thread never wait
/// for the device. On a stop the thread renders one last block ramped down to silence, lets the
/// track play out what is queued, then releases the track and the audio focus; the first block of
/// a session is ramped up from silence (<see cref="GainRamp"/>). Nothing already rendered is
/// dropped, so pause and resume neither click nor skip. A failed write or a failing callback ends
/// the session the same way and raises <see cref="Interrupted"/> with
/// <see cref="AudioInterruption.DeviceFailed"/>.
/// </para>
/// Media session, foreground service and lock screen controls are a later increment
/// (SoncleMediaService binding).
/// </summary>
public sealed class AudioTrackOutput : IAudioOutput
{
    private const string LogTag = StartupClock.LogTag;
    private const int BytesPerFrame = 2 * sizeof(float);

    /// <summary>How long <see cref="Start"/> waits for a previous session to finish its play-out.</summary>
    private const int PreviousSessionWaitMs = 1500;

    private readonly AudioManager? _audioManager;
    private readonly AudioAttributes _attributes;
    private readonly FocusListener _focusListener;
    private readonly global::Android.OS.Handler _main;
    private readonly object _gate = new();

    // Guards the focus objects between Teardown (render thread) and Dispose. Separate from _gate,
    // because Start and Dispose hold _gate while they join the render thread.
    private readonly object _focusGate = new();
    private AudioFocusRequestClass? _focusRequest;
    private bool _focusClosed;

    // The latest session; once stopped it may still be playing out on its own thread.
    private volatile Session? _session;
    private bool _disposed;

    public AudioTrackOutput(int sampleRate = 48000)
    {
        SampleRate = sampleRate;
        var context = global::Android.App.Application.Context;
        _audioManager = context.GetSystemService(global::Android.Content.Context.AudioService) as AudioManager;
        _attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!
            .Build()!;
        _focusListener = new FocusListener(this);
        _main = new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!);
    }

    public int SampleRate { get; }

    public int Channels => 2;

    public bool IsRunning => _session is { Running: true };

    /// <summary>
    /// Raised on the main thread when the output stopped itself: another app took audio focus, or
    /// the device failed. In both cases the track and the focus are released; Start may be called again.
    /// </summary>
    public event EventHandler<AudioInterruption>? Interrupted;

    /// <summary>
    /// Opens the device and starts the render thread. Ignored while running and when audio focus
    /// is denied (<see cref="IsRunning"/> stays false). Throws if the device can't be opened; the
    /// focus it took is given back first.
    /// </summary>
    public void Start(AudioRenderCallback render)
    {
        ArgumentNullException.ThrowIfNull(render);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is { Running: true }) return;

            // A paused session plays out its last block (well under a second) and releases its
            // track and focus on its own thread; wait for that, so two tracks never coexist and
            // the old session can't abandon the focus the new one is about to take.
            if (_session is { Thread: { } previous } && !previous.Join(PreviousSessionWaitMs))
            {
                global::Android.Util.Log.Error(LogTag, "audio-previous-session-stuck");
                throw new InvalidOperationException("The previous audio session did not finish.");
            }
            _session = null;

            if (!RequestFocus())
            {
                global::Android.Util.Log.Warn(LogTag, "audio-focus-denied");
                return;
            }

            var minBytes = AudioTrack.GetMinBufferSize(SampleRate, ChannelOut.Stereo, global::Android.Media.Encoding.PcmFloat);
            var minFrames = minBytes > 0 ? minBytes / BytesPerFrame : 0;
            // Twice the device minimum, never under ~40 ms, so a GC pause doesn't underrun.
            var bufferFrames = Math.Max(minFrames * 2, SampleRate * 40 / 1000);

            AudioTrack? track = null;
            try
            {
                track = BuildTrack(bufferFrames);
                track.Play();
            }
            catch
            {
                // e.g. a device that rejects float PCM: release what was opened and don't keep
                // focus we can't use
                track?.Release();
                track?.Dispose();
                lock (_focusGate) AbandonFocus();
                throw;
            }

            // Render and write half the track buffer at a time, so the device always holds at
            // least half a buffer while the next block renders.
            var session = new Session(track!, render, new float[bufferFrames / 2 * 2], bufferFrames);
            session.Thread = new Thread(() => RenderLoop(session))
            {
                IsBackground = true,
                Name = "Soncle audio",
            };
            _session = session;
            session.Thread.Start();

            global::Android.Util.Log.Info(LogTag, $"audio-started sr={SampleRate} buffer-frames={bufferFrames}");
        }
    }

    /// <summary>
    /// Signals the render thread to fade out and finish; returns at once. The thread plays out the
    /// queued audio and then releases the track and the focus.
    /// </summary>
    public void Stop()
    {
        if (_session is { } session) session.Running = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_session is { } session)
            {
                session.Running = false;
                session.Thread?.Join(PreviousSessionWaitMs);
            }
        }
        lock (_focusGate)
        {
            // Normally the session's teardown gave focus back already; this covers a stuck thread.
            _focusClosed = true;
            AbandonFocus();
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) _focusRequest?.Dispose();
            _focusListener.Dispose();
        }
        // The main-looper handler stays: a stuck session may still post its DeviceFailed to it.
        _attributes.Dispose();
    }

    private AudioTrack BuildTrack(int bufferFrames)
    {
        using var format = new AudioFormat.Builder()
            .SetEncoding(global::Android.Media.Encoding.PcmFloat)!
            .SetSampleRate(SampleRate)!
            .SetChannelMask(ChannelOut.Stereo)!
            .Build()!;
        return new AudioTrack.Builder()
            .SetAudioAttributes(_attributes)!
            .SetAudioFormat(format)!
            .SetTransferMode(AudioTrackMode.Stream)!
            .SetBufferSizeInBytes(bufferFrames * BytesPerFrame)!
            .Build()!;
    }

    /// <summary>
    /// The device thread: render and write whole blocks until stopped, then one last block ramped
    /// to silence, play out, and release. Every exit path goes through the teardown.
    /// </summary>
    private void RenderLoop(Session session)
    {
        global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Audio);
        var track = session.Track;
        var render = session.Render;
        var buffer = session.Buffer;
        var first = true;
        var failed = false;

        try
        {
            while (true)
            {
                var stopping = !session.Running;
                var frames = render(buffer);
                var floats = Math.Clamp(frames, 0, buffer.Length / 2) * 2;
                // Frames past the returned count are silence (the IAudioOutput contract).
                if (floats < buffer.Length) Array.Clear(buffer, floats, buffer.Length - floats);

                if (first) GainRamp.Apply(buffer, 2, 0f, 1f);
                if (stopping) GainRamp.Apply(buffer, 2, 1f, 0f);
                first = false;

                if (!WriteAll(track, buffer, session))
                {
                    failed = true;
                    break;
                }
                if (stopping) break;
            }
            if (!failed) PlayOut(track, session);
        }
        catch (Exception e)
        {
            // Never let the device thread take the process down.
            failed = true;
            global::Android.Util.Log.Error(LogTag, $"audio-thread-failed {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            session.Running = false;
            Teardown(session);
            if (failed) _main.Post(() => Interrupted?.Invoke(this, AudioInterruption.DeviceFailed));
        }
    }

    private static bool WriteAll(AudioTrack track, float[] buffer, Session session)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var written = track.Write(buffer, offset, buffer.Length - offset, WriteMode.Blocking);
            if (written <= 0)
            {
                global::Android.Util.Log.Error(LogTag, $"audio-write-failed code={written}");
                return false;
            }
            offset += written;
        }
        session.Frames += offset / 2;
        return true;
    }

    /// <summary>
    /// Lets the still-playing track consume what is queued (it ends on the faded block), bounded
    /// by twice the buffer's duration, so the flush in the teardown drops nothing audible. Waits on
    /// the playback head while playing, because AudioTrack.stop() resets it.
    /// </summary>
    private void PlayOut(AudioTrack track, Session session)
    {
        // Read before the play-out: the track underruns once on purpose when the queue runs dry.
        session.Underruns = track.UnderrunCount;
        var limitMs = (int)(2L * session.BufferFrames * 1000 / SampleRate) + 200;
        var waited = 0;
        var written = unchecked((int)session.Frames);   // the head is a wrapping 32-bit frame count
        while (waited < limitMs && unchecked(written - track.PlaybackHeadPosition) > 0)
        {
            Thread.Sleep(5);
            waited += 5;
        }
    }

    private void Teardown(Session session)
    {
        var track = session.Track;
        try
        {
            session.Underruns ??= track.UnderrunCount;
            if (track.PlayState != PlayState.Stopped)
            {
                track.Pause();
                track.Flush();
                track.Stop();
            }
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn(LogTag, $"audio-stop-failed {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            track.Release();
            track.Dispose();
        }

        // Start waits for this thread before it takes focus again, so this never drops a newer
        // session's focus; after Dispose the focus objects are gone and Dispose gave it back.
        lock (_focusGate)
        {
            if (!_focusClosed) AbandonFocus();
        }

        global::Android.Util.Log.Info(LogTag, $"audio-stopped frames={session.Frames} underruns={session.Underruns ?? -1}");
    }

    private bool RequestFocus()
    {
        if (_audioManager is null) return true;
        AudioFocusRequest result;
        lock (_focusGate)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                _focusRequest ??= new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                    .SetAudioAttributes(_attributes)!
                    .SetOnAudioFocusChangeListener(_focusListener)!
                    .Build()!;
                result = _audioManager.RequestAudioFocus(_focusRequest);
            }
            else
            {
#pragma warning disable CS0618, CA1422 // the only focus API below API 26
                result = _audioManager.RequestAudioFocus(_focusListener, global::Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CS0618, CA1422
            }
        }
        return result == AudioFocusRequest.Granted;
    }

    private void AbandonFocus()
    {
        if (_audioManager is null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (_focusRequest is { } request) _audioManager.AbandonAudioFocusRequest(request);
        }
        else
        {
#pragma warning disable CS0618, CA1422 // the only focus API below API 26
            _audioManager.AbandonAudioFocus(_focusListener);
#pragma warning restore CS0618, CA1422
        }
    }

    private void OnFocusChange(AudioFocus change)
    {
        // Permanent or transient loss: be polite and stop (faded, without blocking the main
        // thread). Ducking is left to the system (API 26+).
        if (change is AudioFocus.Loss or AudioFocus.LossTransient && IsRunning)
        {
            global::Android.Util.Log.Info(LogTag, $"audio-focus-lost {change}");
            Stop();
            Interrupted?.Invoke(this, AudioInterruption.FocusLost);
        }
    }

    /// <summary>One Start..Stop: the track, its thread and its buffer, owned by that thread.</summary>
    private sealed class Session(AudioTrack track, AudioRenderCallback render, float[] buffer, int bufferFrames)
    {
        public AudioTrack Track { get; } = track;

        public AudioRenderCallback Render { get; } = render;

        public float[] Buffer { get; } = buffer;

        public int BufferFrames { get; } = bufferFrames;

        public Thread? Thread { get; set; }

        private volatile bool _running = true;

        public bool Running
        {
            get => _running;
            set => _running = value;
        }

        /// <summary>Frames written to the track; touched only by the session's thread.</summary>
        public long Frames { get; set; }

        /// <summary>Underruns before the play-out; touched only by the session's thread.</summary>
        public int? Underruns { get; set; }
    }

    private sealed class FocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        private readonly AudioTrackOutput _owner;

        public FocusListener(AudioTrackOutput owner) => _owner = owner;

        public void OnAudioFocusChange(AudioFocus focusChange) => _owner.OnFocusChange(focusChange);
    }
}
