// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Decode;
using Soncle.Audio.Opus;
using Soncle.TestKit;

namespace Soncle.Audio.Opus.Tests;

/// <summary>
/// The golden check of streaming start with the real codec: <see cref="StreamingOpusSource"/>
/// reading <c>fixtures/flow/sample.webm</c> through Concentus, start to end, is the whole-file
/// <see cref="OpusWebmDecoder.Decode"/> sample for sample (same pre-skip, gain, padding, length).
/// </summary>
public class StreamingGoldenTests
{
    [Fact]
    public async Task StreamingDecodeOfTheSampleEqualsTheWholeFileDecode()
    {
        var bytes = File.ReadAllBytes(Fixtures.Path("flow/sample.webm"));
        var whole = OpusWebmDecoder.Decode(bytes, new ConcentusOpusDecoderFactory());
        using var source = new StreamingOpusSource(
            _ => new MemoryStream(bytes, writable: false), new ConcentusOpusDecoderFactory(), ringFrames: 1 << 15);
        await source.Ready;

        var l = new float[480];
        var r = new float[480];
        var got = new List<float>(whole.InterleavedStereo.Length);
        for (var spins = 0; spins < 100_000; spins++)
        {
            if (!source.DecodeComplete && source.DecodedEndFrame - source.Position < l.Length)
            {
                await Task.Delay(1);
                continue;
            }
            var n = source.Read(l, r, l.Length);
            for (var i = 0; i < n; i++) { got.Add(l[i]); got.Add(r[i]); }
            if (n < l.Length) break;
        }

        Assert.True(source.Ended);
        Assert.Null(source.Failure);
        Assert.Equal(whole.Frames, got.Count / 2);
        Assert.Equal(whole.Frames, source.LengthFrames);
        var bad = -1;
        for (var i = 0; i < got.Count && bad < 0; i++)
            if (whole.InterleavedStereo[i] != got[i]) bad = i;
        Assert.True(bad < 0, bad < 0 ? "" : $"sample {bad}: streamed {got[bad]}, whole file {whole.InterleavedStereo[bad]}");
    }
}
