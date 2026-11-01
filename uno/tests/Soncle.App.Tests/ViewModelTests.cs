// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.App.ViewModels;
using Soncle.Core.Playback;
using Soncle.TestKit;
using Soncle.YouTube.Parsing;

namespace Soncle.App.Tests;

public class SearchViewModelTests
{
    /// <summary>
    /// The recorded 'all' search (what the head asks for: the recorded "song" filter came back as
    /// an empty Message section, and its fallback drops these rows because they parse as videos).
    /// </summary>
    internal static YtSearchPage RecordedSongs(string query = "q")
    {
        using var all = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("yt/raw/search-all.json")));
        return Pages.Search(all.RootElement, query, "all");
    }

    [Fact]
    public void TheRecordedSongFilterIsEmptySoTheHeadSearchesAll()
    {
        using var songs = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("yt/raw/search-songs.json")));
        using var all = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("yt/raw/search-all.json")));
        Assert.Empty(SongItem.FromPage(Pages.Search(songs.RootElement, "q", "song", _ => all.RootElement)));
        Assert.NotEmpty(SongItem.FromPage(RecordedSongs()));
    }

    private static (SearchViewModel Vm, PlaybackController Controller, FakePlayer Player) Build(SearchFunc search)
    {
        var player = new FakePlayer();
        var controller = new PlaybackController(player, new FakeStreams(), new InstantClock());
        return (new SearchViewModel(search, controller), controller, player);
    }

    [Fact]
    public async Task MapsTheRecordedSongSearchToRows()
    {
        var page = RecordedSongs();
        var (vm, _, _) = Build((q, ct) => Task.FromResult(page));
        vm.Query = "  something  ";
        await vm.SearchAsync();

        Assert.False(vm.IsSearching);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.EmptyMessage);
        Assert.NotEmpty(vm.Results);
        var songs = page.Sections.SelectMany(s => s.Items).Where(i => i.Type is "song" or "video" && !string.IsNullOrEmpty(i.Id)).ToList();
        var first = vm.Results.First(r => r.VideoId == songs[0].Id);
        Assert.Equal(songs[0].Title, first.Title);
        Assert.Equal(string.Join(", ", songs[0].Artists!.Select(a => a.Name)), first.Artists);
        Assert.All(vm.Results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.VideoId));
            Assert.False(string.IsNullOrEmpty(r.Title));
        });
        Assert.Equal(vm.Results.Count, vm.Results.Select(r => r.VideoId).Distinct().Count());
        Assert.All(vm.Results, r => Assert.Equal(r.DurationSeconds > 0 ? TimeText.Format(r.DurationSeconds) : "", r.DurationText));
        // only playable rows: the fixture's artists and playlists are left out
        Assert.Equal(songs.Select(s => s.Id), vm.Results.Select(r => r.VideoId));
    }

    [Fact]
    public async Task ABlankQueryIsIgnored()
    {
        var calls = 0;
        var (vm, _, _) = Build((q, ct) => { calls++; return Task.FromResult(new YtSearchPage()); });
        vm.Query = "   ";
        await vm.SearchAsync();
        Assert.Equal(0, calls);
        Assert.False(vm.IsSearching);
    }

    [Fact]
    public async Task AStaleResultIsDroppedWhenANewerSearchFinishesFirst()
    {
        var pending = new Dictionary<string, (TaskCompletionSource<YtSearchPage> Tcs, CancellationToken Ct)>();
        var (vm, _, _) = Build((q, ct) =>
        {
            var tcs = new TaskCompletionSource<YtSearchPage>();
            pending[q] = (tcs, ct);
            return tcs.Task;
        });
        static YtSearchPage Page(string id) => new()
        {
            Sections = { new YtShelf { Items = { new YtItem { Type = "song", Id = id, Title = id } } } },
        };

        vm.Query = "old";
        var oldSearch = vm.SearchAsync();
        vm.Query = "new";
        var newSearch = vm.SearchAsync();
        Assert.True(pending["old"].Ct.IsCancellationRequested, "a newer search cancels the one in flight");

        pending["new"].Tcs.SetResult(Page("newer"));
        await newSearch;
        pending["old"].Tcs.SetResult(Page("stale"));   // the old one ignored its cancellation
        await oldSearch;

        Assert.Equal(new[] { "newer" }, vm.Results.Select(r => r.VideoId));
        Assert.False(vm.IsSearching);
    }

    [Fact]
    public async Task AFailedOrEmptySearchSaysSoPlainly()
    {
        var (vm, _, _) = Build((q, ct) => throw new HttpRequestException("HTTP 500"));
        vm.Query = "x";
        await vm.SearchAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.DoesNotContain("500", vm.ErrorMessage);
        Assert.Equal("HTTP 500", vm.ErrorDetail);
        Assert.False(vm.IsSearching);

        var (empty, _, _) = Build((q, ct) => Task.FromResult(new YtSearchPage()));
        empty.Query = "nothing";
        await empty.SearchAsync();
        Assert.Empty(empty.Results);
        Assert.Contains("nothing", empty.EmptyMessage);
    }

    [Fact]
    public async Task PlayingAResultQueuesAllResultsFromThePick()
    {
        var (vm, controller, player) = Build((q, ct) => Task.FromResult(RecordedSongs()));
        vm.Query = "q";
        await vm.SearchAsync();
        Assert.True(vm.Results.Count >= 3);

        var pick = vm.Results[2];
        await vm.PlayAsync(pick);
        Assert.Equal(vm.Results.Select(r => r.VideoId), controller.Queue.Select(t => t.Id));
        Assert.Equal(2, controller.Index);
        Assert.Equal(pick.Title, controller.Current!.Title);
        Assert.Equal(pick.DurationSeconds, controller.Current.Duration);
        Assert.Equal(new[] { "mstream://" + pick.VideoId }, player.Loads);
        Assert.Same(pick, vm.Find(pick.VideoId));

        await vm.PlayAsync(new SongItem("elsewhere", "t", "", "", 0, ""));   // not a result: ignored
        Assert.Single(player.Loads);
    }
}

public class PlayerBarViewModelTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5.9, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(600, "10:00")]
    [InlineData(3662, "1:01:02")]
    [InlineData(-3, "0:00")]
    [InlineData(double.NaN, "0:00")]
    public void TimesAreMinutesAndSeconds(double seconds, string text) => Assert.Equal(text, TimeText.Format(seconds));

    [Fact]
    public async Task EmptyUntilASongIsChosenThenShowsIt()
    {
        var player = new FakePlayer();
        var controller = new PlaybackController(player, new FakeStreams(), new InstantClock());
        var row = new SongItem("v1", "Song", "Artist A, Artist B", "Album", 200, "https://img/1");
        var bar = new PlayerBarViewModel(controller, player, id => id == "v1" ? row : null);
        Assert.True(bar.IsEmpty);
        bar.Refresh();
        Assert.True(bar.IsEmpty);
        Assert.Equal("", bar.Title);

        controller.SetQueue(new[] { new QueueTrack("v1", "Song", 200) });
        await controller.PlayAtAsync(0);
        Assert.False(bar.IsEmpty);
        Assert.Equal("Song", bar.Title);
        Assert.Equal("Artist A, Artist B", bar.Subtitle);
        Assert.Equal("https://img/1", bar.ThumbnailUrl);

        player.Duration = 200;
        player.Position = 65;
        bar.Refresh();
        Assert.True(bar.IsPlaying);
        Assert.False(bar.IsLoading);
        Assert.Equal("1:05", bar.PositionText);
        Assert.Equal("3:20", bar.DurationText);

        bar.Seek(500);   // clamped to the song
        Assert.Equal(200, player.Position);
        Assert.Equal(200, bar.PositionSeconds);

        await bar.TogglePlayAsync();
        Assert.False(bar.IsPlaying);
    }

    [Fact]
    public async Task AFailureShowsAPlainMessageAndKeepsTheReason()
    {
        var player = new FakePlayer { Fail = "no playable audio format" };
        var controller = new PlaybackController(player, new FakeStreams(), new InstantClock());
        var bar = new PlayerBarViewModel(controller, player);
        var changed = new List<string?>();
        bar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        controller.SetQueue(new[] { new QueueTrack("v1", "Song") });
        await controller.PlayAtAsync(0);
        Assert.Equal(PlayerBarViewModel.CouldNotPlay, bar.ErrorMessage);
        Assert.Equal("Couldn't play this song.", bar.ErrorMessage);
        Assert.Equal("no playable audio format", bar.ErrorDetail);
        Assert.False(bar.IsLoading);
        Assert.False(bar.IsPlaying);
        Assert.Contains(nameof(PlayerBarViewModel.ErrorMessage), changed);

        // choosing another song clears the error
        player.Fail = null;
        await controller.PlayAtAsync(0);
        Assert.Null(bar.ErrorMessage);
    }
}
