// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Audio.Opus;
using Soncle.Audio.Output;
using Soncle.TestKit;

namespace Soncle.Audio.Opus.Tests;

/// <summary>
/// The clip helper heads use for their bundled sample: the fixture WebM decodes to 48 kHz stereo
/// of the length ffmpeg reports, loops seamlessly and sounds through the pump.
/// </summary>
public class WebmOpusClipTests
{
    private static int OracleFrames() =>
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("flow/opus-oracle.json"))).RootElement
            .GetProperty("files").GetProperty("sample.webm").GetProperty("decoded").GetProperty("frames").GetInt32();

    private static byte[] Sample() => File.ReadAllBytes(Fixtures.Path("flow/sample.webm"));

    [Fact]
    public void DecodesTheFixtureToTheOracleLength()
    {
        var clip = WebmOpusClip.Load(Sample());
        Assert.Equal(48_000, clip.SampleRate);
        Assert.Equal((long)OracleFrames(), clip.LengthFrames);
        Assert.False(clip.Loop);
    }

    [Fact]
    public void ALoopingClipWrapsToItsFirstFrame()
    {
        var frames = OracleFrames();
        var once = WebmOpusClip.Load(Sample());
        var loop = WebmOpusClip.Load(Sample(), loop: true);
        Assert.True(loop.Loop);

        var headL = new float[100];
        var headR = new float[100];
        Assert.Equal(100, once.Read(headL, headR, 100));

        var l = new float[frames + 100];
        var r = new float[frames + 100];
        Assert.Equal(frames + 100, loop.Read(l, r, frames + 100));
        Assert.Equal(headL, l.AsSpan(frames, 100).ToArray());
        Assert.Equal(headR, r.AsSpan(frames, 100).ToArray());
        Assert.Equal(100L, loop.Position);
    }

    [Fact]
    public void TheClipSoundsThroughThePumpUnderTheCeiling()
    {
        var clip = WebmOpusClip.Load(Sample());
        var engine = new Engine(clip.SampleRate);
        engine.Load(clip, lufs: null);
        var pump = new OutputPump(engine) { Source = clip };
        var got = new float[(int)clip.LengthFrames!.Value * 2];
        for (var done = 0; done < got.Length; done += 3840)
            pump.Fill(got.AsSpan(done, Math.Min(3840, got.Length - done)));

        Assert.True(AudioAssert.RmsDb(got) > -40, AudioAssert.RmsDb(got).ToString());
        Assert.True(AudioAssert.TruePeakDb(got) <= -0.8, AudioAssert.TruePeakDb(got).ToString());
        Assert.True(pump.Ended);
    }
}
