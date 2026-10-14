// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Cli;
using Soncle.TestKit;

namespace Soncle.Cli.Tests;

/// <summary>The CLI's WebM/Opus decoder on the real fixture files (the codec is checked in depth in Soncle.Audio.Opus.Tests).</summary>
public class OpusDecoderTests
{
    [Theory]
    [InlineData("audio/webm", true)]
    [InlineData("audio/webm; codecs=\"opus\"", true)]
    [InlineData("AUDIO/WEBM;codecs=opus", true)]
    [InlineData("audio/webm; codecs=\"vorbis\"", false)]
    [InlineData("audio/mp4; codecs=\"mp4a.40.2\"", false)]
    [InlineData("", false)]
    public void KnowsWhichMimesItCanDecode(string mime, bool expected) =>
        Assert.Equal(expected, new OpusDecoder().CanDecode(mime));

    [Fact]
    public void DecodesAWebmSongToPlayablePcm()
    {
        using var f = File.OpenRead(Fixtures.Path("flow/sample.webm"));
        var src = new OpusDecoder().Decode(f, "audio/webm; codecs=\"opus\"");
        Assert.Equal(48_000, src.SampleRate);
        Assert.Equal(384_000, src.LengthFrames);
        var l = new float[4800]; var r = new float[4800];
        Assert.Equal(4800, src.Read(l, r, 4800));
        Assert.Contains(l, s => Math.Abs(s) > 0.01f);
    }

    [Fact]
    public void AnotherMimeIsRefused()
    {
        using var f = File.OpenRead(Fixtures.Path("flow/sample.webm"));
        Assert.Throws<NotSupportedException>(() => new OpusDecoder().Decode(f, "audio/mp4"));
    }
}
