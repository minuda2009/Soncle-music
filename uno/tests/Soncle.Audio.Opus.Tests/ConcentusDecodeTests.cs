// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Audio.Decode;
using Soncle.Audio.Opus;
using Soncle.TestKit;

namespace Soncle.Audio.Opus.Tests;

/// <summary>
/// The real codec on the real WebM files, against what ffmpeg (libopus) says
/// (<c>fixtures/flow/opus-oracle.json</c>): same length to the sample, same level per channel.
/// Concentus and libopus are both Opus-conformant decoders, so the numbers agree to a small tolerance.
/// </summary>
public class ConcentusDecodeTests
{
    private static readonly JsonElement Oracle =
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("flow/opus-oracle.json"))).RootElement.GetProperty("files");

    private static (double RmsDb, double Peak) Level(float[] pcm, int channel)
    {
        double sum = 0, peak = 0;
        var frames = pcm.Length / 2;
        for (var i = channel; i < pcm.Length; i += 2) { sum += (double)pcm[i] * pcm[i]; peak = Math.Max(peak, Math.Abs(pcm[i])); }
        return (10 * Math.Log10(sum / frames), peak);
    }

    [Fact]
    public void DecodesTheSameLengthAndLevelAsFfmpeg()
    {
        foreach (var f in Oracle.EnumerateObject())
        {
            var want = f.Value.GetProperty("decoded");
            var got = OpusWebmDecoder.Decode(File.ReadAllBytes(Fixtures.Path("flow/" + f.Name)), new ConcentusOpusDecoderFactory());
            Assert.Equal(want.GetProperty("frames").GetInt32(), got.Frames);
            Assert.Equal(48_000, got.SampleRate);
            foreach (var (name, ch) in new[] { ("left", 0), ("right", 1) })
            {
                var (rms, peak) = Level(got.InterleavedStereo, ch);
                Assert.InRange(rms, want.GetProperty(name).GetProperty("rmsDb").GetDouble() - 0.05, want.GetProperty(name).GetProperty("rmsDb").GetDouble() + 0.05);
                Assert.InRange(peak, want.GetProperty(name).GetProperty("peak").GetDouble() - 0.01, want.GetProperty(name).GetProperty("peak").GetDouble() + 0.01);
            }
        }
    }

    [Fact]
    public void MonoBecomesDualMono()
    {
        var got = OpusWebmDecoder.Decode(File.ReadAllBytes(Fixtures.Path("flow/mono.webm")), new ConcentusOpusDecoderFactory());
        Assert.Equal(48_000, got.Frames);
        for (var i = 0; i < got.Frames; i += 97) Assert.Equal(got.InterleavedStereo[2 * i], got.InterleavedStereo[2 * i + 1]);
        Assert.Contains(got.InterleavedStereo, s => Math.Abs(s) > 0.1f);
    }
}
