// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using Soncle.Audio;
using Soncle.Audio.Opus;
using Soncle.Audio.Output;
using Soncle.Core.Platform;

namespace Soncle.Droid.Playback;

/// <summary>
/// Plays the bundled 8 s test clip (fixtures/flow/sample.webm, embedded as
/// <see cref="ResourceName"/>) looped, through the shared path every real song will take:
/// Concentus decode -> <see cref="Engine"/> (master chain, limiter last) -> <see cref="OutputPump"/>
/// -> the head's <see cref="IAudioOutput"/>. Looping keeps it playing so memory can be measured
/// while sound is running. Nothing here is Android-specific; only the output is.
/// </summary>
public sealed class SamplePlayer : IDisposable
{
    public const string ResourceName = "Soncle.sample.webm";

    private readonly IAudioOutput _output;
    private readonly Action<string> _log;
    private OutputPump? _pump;
    private AudioRenderCallback? _render;

    /// <param name="output">The device output; its rate is the engine's rate.</param>
    /// <param name="log">One-line diagnostics (the head writes them to logcat).</param>
    public SamplePlayer(IAudioOutput output, Action<string> log)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsPlaying => _output.IsRunning;

    /// <summary>True once the clip is decoded and loaded into the engine.</summary>
    public bool IsLoaded => _pump is not null;

    /// <summary>Time rendered since the first play (pauses excluded); the clip loops inside it.</summary>
    public TimeSpan Elapsed => _pump is { } p
        ? TimeSpan.FromSeconds((double)p.Engine.Position / p.SampleRate)
        : TimeSpan.Zero;

    /// <summary>Decodes the clip off the calling thread on first use, then starts the output.</summary>
    public async Task PlayAsync()
    {
        if (_pump is null)
        {
            var sampleRate = _output.SampleRate;
            var (pump, ms, frames) = await Task.Run(() =>
            {
                var clock = Stopwatch.StartNew();
                var source = WebmOpusClip.Load(ReadClip(), loop: true);
                if (source.SampleRate != sampleRate)
                {
                    throw new NotSupportedException(
                        $"The test clip decodes at {source.SampleRate} Hz; the output runs at {sampleRate} Hz.");
                }
                var engine = new Engine(sampleRate);
                engine.Load(source, lufs: null);
                var p = new OutputPump(engine) { Source = source };
                return (p, clock.ElapsedMilliseconds, source.LengthFrames ?? 0);
            }).ConfigureAwait(true);

            if (_pump is null)
            {
                _pump = pump;
                _render = pump.Fill;
                _log($"sample-decoded ms={ms} frames={frames}");
            }
        }

        _output.Start(_render!);
    }

    /// <summary>Stops the device; the engine keeps its place so <see cref="PlayAsync"/> resumes.</summary>
    public void Pause() => _output.Stop();

    public void Dispose() => _output.Stop();

    private static byte[] ReadClip()
    {
        using var stream = typeof(SamplePlayer).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var copy = new MemoryStream(checked((int)stream.Length));
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
