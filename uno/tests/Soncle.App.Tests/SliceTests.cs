// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App.Playback;
using Soncle.App.ViewModels;
using Soncle.Core.Playback;
using Soncle.Streams;
using Soncle.TestKit;
using Soncle.YouTube;

namespace Soncle.App.Tests;

/// <summary>
/// The whole Windows slice without a window: search (recorded page) → pick → resolve (Opus-only)
/// → download (fake googlevideo serving fixtures/flow/sample.webm) → decode → engine → device.
/// The codec is the TOC-sized fake, so this runs anywhere; the real codec is covered by
/// <see cref="EnginePlayerTests.TheRecordedSampleSongPlaysThroughTheEngine"/>.
/// </summary>
public class SliceTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "soncle-slice-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task SearchPickAndPlayReachesTheDevice()
    {
        var webm = File.ReadAllBytes(Fixtures.Path("flow/sample.webm"));
        var client = new FakeStreamClient(
            new AudioFormat("https://gv/m4a?clen=1", "audio/mp4; codecs=\"mp4a.40.2\"", 256_000, 1),
            new AudioFormat($"https://gv/opus?clen={webm.Length}", "audio/webm; codecs=\"opus\"", 160_000, webm.Length, LoudnessDb: "-2"));
        var resolver = new StreamResolver(client, accept: YouTubeStreams.OpusOnly);
        resolver.SetClients(new[] { "IOS", "ANDROID_VR" });
        var streams = new YouTubeStreams(resolver, new SongDownloader(_cache, new FakeFetcher(webm)), new FakeOpusCodec());
        var output = new FakeAudioOutput();
        using var player = new EnginePlayer(output, streams.LoadSongAsync);
        var controller = new PlaybackController(player, streams, new InstantClock());
        var search = new SearchViewModel((q, ct) => Task.FromResult(SearchViewModelTests.RecordedSongs(q)), controller);
        var bar = new PlayerBarViewModel(controller, player, search.Find);

        Assert.True(bar.IsEmpty);
        search.Query = "anything";
        await search.SearchAsync();
        var pick = search.Results[1];
        await search.PlayAsync(pick);

        Assert.Equal(PlaybackState.Playing, player.State);
        Assert.Equal(pick.VideoId, player.CurrentId);
        bar.Refresh();
        Assert.False(bar.IsEmpty);
        Assert.Equal(pick.Title, bar.Title);
        Assert.Equal(pick.Artists, bar.Subtitle);
        Assert.True(bar.IsPlaying);
        Assert.Null(bar.ErrorMessage);
        Assert.InRange(bar.DurationSeconds, 7, 9);   // the decoded song's length, not the row's

        var got = output.Record(100);
        // the fake codec's constant through the engine: normalised (-16 LUFS → +2 dB) and limited
        Assert.True(AudioAssert.RmsDb(got) > -30, AudioAssert.RmsDb(got).ToString());
        Assert.True(AudioAssert.TruePeakDb(got) <= -0.8, AudioAssert.TruePeakDb(got).ToString());
        bar.Refresh();
        Assert.Equal("0:01", bar.PositionText);
    }
}
