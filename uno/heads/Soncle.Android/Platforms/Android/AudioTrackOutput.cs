// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Android.Media;
using Soncle.Core.Platform;

namespace Soncle.Droid;

/// <summary>
/// <see cref="IAudioOutput"/> on Android's <see cref="AudioTrack"/>: 32-bit float PCM, stereo, at
/// the engine's rate (48 kHz), streamed from one dedicated thread that pulls the render callback
/// into a preallocated buffer and writes it blocking. It asks for audio focus before it starts and
/// stops itself when another app takes focus. Media session, foreground service and lock screen
/// controls are a later increment (SoncleMediaService binding).
/// </summary>
public sealed class AudioTrackOutput : IAudioOutput
{
    private const string LogTag = StartupClock.LogTag;
    private const int BytesPerFrame = 2 * sizeof(float);

    private readonly AudioManager? _audioManager;
    private readonly AudioAttributes _attributes;
    private readonly FocusListener _focusListener;
    private readonly object _gate = new();
    private AudioFocusRequestClass? _focusRequest;

    private AudioTrack? _track;
    private Thread? _thread;
    private AudioRenderCallback? _render;
    private float[] _buffer = [];
    private volatile bool _running;
    private long _frames;
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
    }

    public int SampleRate { get; }

    public int Channels => 2;

    public bool IsRunning => _running;

    /// <summary>
    /// Raised (on the main thread) when another app took audio focus and the output stopped itself.
    /// </summary>
    public event EventHandler? Interrupted;

    public void Start(AudioRenderCallback render)
    {
        ArgumentNullException.ThrowIfNull(render);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running) return;

            if (!RequestFocus())
            {
                global::Android.Util.Log.Warn(LogTag, "audio-focus-denied");
                return;
            }

            var minBytes = AudioTrack.GetMinBufferSize(SampleRate, ChannelOut.Stereo, global::Android.Media.Encoding.PcmFloat);
            var minFrames = minBytes > 0 ? minBytes / BytesPerFrame : 0;
            // Twice the device minimum, never under ~40 ms, so a GC pause doesn't underrun.
            var bufferFrames = Math.Max(minFrames * 2, SampleRate * 40 / 1000);

            var format = new AudioFormat.Builder()
                .SetEncoding(global::Android.Media.Encoding.PcmFloat)!
                .SetSampleRate(SampleRate)!
                .SetChannelMask(ChannelOut.Stereo)!
                .Build()!;
            var track = new AudioTrack.Builder()
                .SetAudioAttributes(_attributes)!
                .SetAudioFormat(format)!
                .SetTransferMode(AudioTrackMode.Stream)!
                .SetBufferSizeInBytes(bufferFrames * BytesPerFrame)!
                .Build()!;

            // Render and write half the track buffer at a time, so the device always holds at
            // least half a buffer while the next block renders.
            var blockFrames = bufferFrames / 2;
            if (_buffer.Length != blockFrames * 2) _buffer = new float[blockFrames * 2];
            _render = render;
            _track = track;
            _frames = 0;
            _running = true;
            track.Play();

            _thread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "Soncle audio",
            };
            _thread.Start();

            global::Android.Util.Log.Info(LogTag, $"audio-started sr={SampleRate} buffer-frames={bufferFrames}");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_track is not { } track) return;

            _running = false;
            // A blocking write returns once the playing track drains one buffer (~40-100 ms).
            if (_thread is { } thread && !thread.Join(500))
            {
                track.Pause();
                thread.Join(500);
            }
            _thread = null;

            var underruns = track.UnderrunCount;
            track.Pause();
            track.Flush();
            track.Stop();
            track.Release();
            track.Dispose();
            _track = null;
            _render = null;
            AbandonFocus();

            global::Android.Util.Log.Info(LogTag, $"audio-stopped frames={Interlocked.Read(ref _frames)} underruns={underruns}");
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _attributes.Dispose();
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) _focusRequest?.Dispose();
            _focusListener.Dispose();
        }
    }

    /// <summary>The device thread: render, then write the whole block, until <see cref="Stop"/>.</summary>
    private void RenderLoop()
    {
        global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Audio);
        var track = _track;
        var render = _render;
        var buffer = _buffer;
        if (track is null || render is null) return;

        try
        {
            while (_running)
            {
                var frames = render(buffer);
                var floats = Math.Clamp(frames, 0, buffer.Length / 2) * 2;
                // Frames past the returned count are silence (the IAudioOutput contract).
                if (floats < buffer.Length) Array.Clear(buffer, floats, buffer.Length - floats);

                var offset = 0;
                while (offset < buffer.Length && _running)
                {
                    var written = track.Write(buffer, offset, buffer.Length - offset, WriteMode.Blocking);
                    if (written <= 0)
                    {
                        global::Android.Util.Log.Error(LogTag, $"audio-write-failed code={written}");
                        _running = false;
                        return;
                    }
                    offset += written;
                }
                Interlocked.Add(ref _frames, offset / 2);
            }
        }
        catch (Exception e)
        {
            // Never let the device thread take the process down; the page sees IsRunning=false.
            _running = false;
            global::Android.Util.Log.Error(LogTag, $"audio-thread-failed {e.GetType().Name}: {e.Message}");
        }
    }

    private bool RequestFocus()
    {
        if (_audioManager is null) return true;
        AudioFocusRequest result;
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
        // Permanent or transient loss: be polite and stop. Ducking is left to the system (API 26+).
        if (change is AudioFocus.Loss or AudioFocus.LossTransient && IsRunning)
        {
            global::Android.Util.Log.Info(LogTag, $"audio-focus-lost {change}");
            Stop();
            Interrupted?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        private readonly AudioTrackOutput _owner;

        public FocusListener(AudioTrackOutput owner) => _owner = owner;

        public void OnAudioFocusChange(AudioFocus focusChange) => _owner.OnFocusChange(focusChange);
    }
}
