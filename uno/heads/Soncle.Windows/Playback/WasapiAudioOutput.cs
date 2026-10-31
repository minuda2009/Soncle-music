// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Soncle.Audio.Output;
using Soncle.Core.Platform;

namespace Soncle.Windows.Playback;

/// <summary>
/// <see cref="IAudioOutput"/> on WASAPI (NAudio's <see cref="WasapiOut"/>): shared mode,
/// event-driven, 32-bit float, stereo, at the engine's rate (48 kHz) on the default render device.
/// <para>
/// The device's mix format is often 44.1 kHz or 24-bit: shared mode converts for us (NAudio 2.2
/// opens the stream with <c>AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | SRC_DEFAULT_QUALITY</c>), so the
/// engine always renders 48 kHz float. Exclusive mode and device choice are later settings
/// (docs/WINUI_PLAN.md §5).
/// </para>
/// <para>
/// Same session contract as the Android head's <c>AudioTrackOutput</c>: each <see cref="Start"/>
/// opens one session; its first block is ramped up from silence and, after <see cref="Stop"/>, one
/// last block is rendered ramped down to silence (<see cref="GainRamp"/>), played out, and only
/// then is the device stopped and released on a pool thread, so pause and resume neither click
/// nor block the UI thread. The render callback runs on NAudio's playback thread inside
/// <see cref="IWaveProvider.Read"/>; that path does not allocate (the byte buffer is reinterpreted
/// as floats in place) and never lets an exception reach NAudio. A failing callback or a device
/// that goes away (unplugged, taken in exclusive mode) ends the session and raises
/// <see cref="Failed"/>; <see cref="Start"/> may be called again.
/// </para>
/// </summary>
public sealed class WasapiAudioOutput : IAudioOutput
{
    /// <summary>The shared-mode buffer, ms: large enough that a GC pause on the UI side never underruns.</summary>
    private const int LatencyMs = 80;

    /// <summary>How long a stopping session waits for its faded last block before it stops anyway.</summary>
    private const int FinalBlockWaitMs = 500;

    private readonly SynchronizationContext? _ui;
    private readonly object _gate = new();
    private Session? _session;
    private bool _disposed;

    public WasapiAudioOutput(int sampleRate = 48000)
    {
        SampleRate = sampleRate;
        _ui = SynchronizationContext.Current;
    }

    public int SampleRate { get; }

    public int Channels => 2;

    public bool IsRunning
    {
        get
        {
            var s = Volatile.Read(ref _session);
            return s is not null && !s.StopRequested && !s.HasFailed;
        }
    }

    /// <summary>Why the last session ended on its own, or null (cleared by the next <see cref="Start"/>).</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Raised (on the thread that created the output, normally the UI thread) when the device
    /// stopped without being asked to: the device failed or the render callback threw. The session
    /// is released; <see cref="Start"/> may be called again.
    /// </summary>
    public event EventHandler<string>? Failed;

    /// <summary>
    /// Opens the default render device and starts pulling from <paramref name="render"/>. Ignored
    /// while running. Throws when no device can be opened (nothing stays open).
    /// </summary>
    public void Start(AudioRenderCallback render)
    {
        ArgumentNullException.ThrowIfNull(render);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRunning) return;

            WasapiOut? device = null;
            try
            {
                var session = new Session(this, render, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels));
                device = new WasapiOut(AudioClientShareMode.Shared, true, LatencyMs);
                session.Device = device;
                device.PlaybackStopped += (_, e) => OnPlaybackStopped(session, e.Exception);
                device.Init(session);
                FailureReason = null;
                _session = session;
                device.Play();
                Trace.WriteLine($"Soncle audio-started sr={SampleRate} latency-ms={LatencyMs}");
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Soncle audio-start-failed {e.GetType().Name}");
                _session = null;
                device?.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Asks the session to fade out and returns at once; a pool thread waits for the faded block
    /// to play out and then stops and releases the device.
    /// </summary>
    public void Stop()
    {
        Session? s;
        lock (_gate)
        {
            s = _session;
            if (s is null || s.StopRequested) return;
            s.StopRequested = true;
        }
        ThreadPool.QueueUserWorkItem<Session>(static st => st.Owner.Teardown(st, playOut: true), s, preferLocal: false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }

    private void OnPlaybackStopped(Session session, Exception? error)
    {
        // A stop we asked for is the normal end; only an exception means the device went away.
        if (error is null || session.StopRequested) return;
        Fail(session, $"device: {error.GetType().Name}");
    }

    /// <summary>Ends <paramref name="session"/> after a failure and tells the owner, once.</summary>
    private void Fail(Session session, string reason)
    {
        if (Interlocked.Exchange(ref session.FailedFlag, 1) != 0) return;
        Trace.WriteLine($"Soncle audio-failed {reason}");
        lock (_gate)
        {
            if (ReferenceEquals(_session, session)) FailureReason = reason;
            session.StopRequested = true;
        }
        ThreadPool.QueueUserWorkItem<Session>(static st => st.Owner.Teardown(st, playOut: false), session, preferLocal: false);
        if (_ui is null) Failed?.Invoke(this, reason);
        else _ui.Post(_ => Failed?.Invoke(this, reason), null);
    }

    /// <summary>Pool thread: lets the faded last block play out, then stops and releases the device.</summary>
    private void Teardown(Session session, bool playOut)
    {
        if (Interlocked.Exchange(ref session.TornDown, 1) != 0) return;
        try
        {
            if (playOut && session.FinalBlock.Wait(FinalBlockWaitMs))
            {
                // the faded block is in WASAPI's buffer: give it the buffer's length to be heard
                Thread.Sleep(LatencyMs + 20);
            }
            session.Device?.Stop();
        }
        catch (Exception e)
        {
            Trace.WriteLine($"Soncle audio-stop-failed {e.GetType().Name}");
        }
        finally
        {
            try
            {
                session.Device?.Dispose();
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Soncle audio-dispose-failed {e.GetType().Name}");
            }
            lock (_gate)
            {
                if (ReferenceEquals(_session, session)) _session = null;
            }
            Trace.WriteLine($"Soncle audio-stopped frames={session.Frames}");
        }
    }

    /// <summary>
    /// One Start..Stop, and the <see cref="IWaveProvider"/> WASAPI pulls from. <see cref="Read"/>
    /// runs on NAudio's playback thread: no allocation, no lock, no exception out of it.
    /// </summary>
    private sealed class Session(WasapiAudioOutput owner, AudioRenderCallback render, WaveFormat format) : IWaveProvider
    {
        private const int BytesPerFrame = 2 * sizeof(float);

        private readonly AudioRenderCallback _render = render;
        private volatile bool _stopRequested;
        private bool _first = true;
        private bool _finished;

        public WasapiAudioOutput Owner { get; } = owner;

        public WaveFormat WaveFormat { get; } = format;

        public WasapiOut? Device { get; set; }

        /// <summary>Set once the faded last block has been handed to the device.</summary>
        public ManualResetEventSlim FinalBlock { get; } = new(false);

        public int FailedFlag;
        public int TornDown;

        public bool HasFailed => Volatile.Read(ref FailedFlag) != 0;

        public bool StopRequested
        {
            get => _stopRequested;
            set => _stopRequested = value;
        }

        /// <summary>Frames handed to the device; touched only by the playback thread.</summary>
        public long Frames { get; private set; }

        public int Read(byte[] buffer, int offset, int count)
        {
            var whole = count - count % BytesPerFrame;
            var floats = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, whole));
            var frames = 0;

            if (!_finished && !HasFailed)
            {
                var stopping = _stopRequested;
                try
                {
                    frames = Math.Clamp(_render(floats), 0, floats.Length / 2);
                }
                catch (Exception e)
                {
                    // never into NAudio: play silence and end the session from a pool thread
                    frames = 0;
                    Owner.Fail(this, $"render: {e.GetType().Name}");
                }
                // frames past the returned count are silence (the IAudioOutput contract)
                floats[(frames * 2)..].Clear();
                if (_first) GainRamp.Apply(floats, 2, 0f, 1f);
                if (stopping)
                {
                    GainRamp.Apply(floats, 2, 1f, 0f);
                    _finished = true;
                    FinalBlock.Set();
                }
                _first = false;
            }
            else
            {
                floats.Clear();
            }

            if (whole < count) buffer.AsSpan(offset + whole, count - whole).Clear();
            Frames += whole / BytesPerFrame;
            // always the full count: a short read makes WasapiOut stop on its own
            return count;
        }
    }
}
