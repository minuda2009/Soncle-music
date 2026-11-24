// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Core.Platform;

namespace Soncle.Windows.Playback;

/// <summary>
/// The player bar's volume, applied after the engine (and so after its limiter): it can only make
/// the signal quieter, so the true-peak ceiling still holds. The slider is mapped on a squared curve,
/// which feels even to the ear; 0 is silence, 1 is the engine's own level. The multiply runs on the
/// device thread and does not allocate.
/// </summary>
internal sealed class VolumeOutput : IAudioOutput
{
    private readonly IAudioOutput _inner;
    private double _gain = 1;

    public VolumeOutput(IAudioOutput inner) => _inner = inner;

    /// <summary>0..1, as on the slider.</summary>
    public double Level { get; private set; } = 1;

    public bool Muted { get; private set; }

    public void SetLevel(double level)
    {
        Level = Math.Clamp(double.IsFinite(level) ? level : 1, 0, 1);
        Apply();
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        Apply();
    }

    private void Apply() => Volatile.Write(ref _gain, Muted ? 0 : Level * Level);

    public int SampleRate => _inner.SampleRate;

    public int Channels => _inner.Channels;

    public bool IsRunning => _inner.IsRunning;

    public void Start(AudioRenderCallback render)
    {
        ArgumentNullException.ThrowIfNull(render);
        _inner.Start(buffer =>
        {
            var frames = render(buffer);
            var g = (float)Volatile.Read(ref _gain);
            if (g < 0.99999f)
            {
                var n = Math.Min(buffer.Length, frames * 2);
                for (var i = 0; i < n; i++) buffer[i] *= g;
            }
            return frames;
        });
    }

    public void Stop() => _inner.Stop();

    /// <summary>The host owns and disposes the device output.</summary>
    public void Dispose() { }
}
