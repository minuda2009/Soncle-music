// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Collections.ObjectModel;
using Soncle.Core.Playback;
using Soncle.YouTube.Parsing;

namespace Soncle.App.ViewModels;

/// <summary>
/// Runs a search; in a head this is <c>InnerTubeCatalogClient.SearchAsync(query, "all", ct)</c>.
/// 'all' rather than the "song" filter: the songs and videos are taken from it here, while the
/// filter can come back empty (see the recorded <c>fixtures/yt/raw/search-songs.json</c>).
/// </summary>
public delegate Task<YtSearchPage> SearchFunc(string query, CancellationToken ct);

/// <summary>
/// The title-bar search (WINUI_PLAN §3): type, search, pick a song to play it with the results as
/// the queue. Only the latest search counts: starting one cancels the one in flight and a stale
/// answer is dropped. Create it and call it on the UI thread; it resumes there after awaits.
/// </summary>
public sealed class SearchViewModel : ObservableObject
{
    private readonly SearchFunc _search;
    private readonly PlaybackController _controller;
    private readonly Dictionary<string, SongItem> _known = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private long _seq;

    private string _query = "";
    private bool _isSearching;
    private string? _errorMessage;
    private string? _errorDetail;
    private string? _emptyMessage;

    public SearchViewModel(SearchFunc search, PlaybackController controller)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string Query { get => _query; set => SetProperty(ref _query, value ?? ""); }
    public bool IsSearching { get => _isSearching; private set => SetProperty(ref _isSearching, value); }

    /// <summary>A plain, user-facing message when the search failed (null otherwise).</summary>
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    /// <summary>The underlying reason, for diagnostics (not for the UI).</summary>
    public string? ErrorDetail { get => _errorDetail; private set => SetProperty(ref _errorDetail, value); }

    /// <summary>Shown instead of an empty list when a search found no songs (null otherwise).</summary>
    public string? EmptyMessage { get => _emptyMessage; private set => SetProperty(ref _emptyMessage, value); }

    public ObservableCollection<SongItem> Results { get; } = new();

    /// <summary>Searches for <see cref="Query"/>; a blank query does nothing.</summary>
    public async Task SearchAsync()
    {
        var q = Query.Trim();
        if (q.Length == 0) return;
        _cts?.Cancel();   // not disposed: the search it belonged to may still be using its token
        var cts = _cts = new CancellationTokenSource();
        var seq = ++_seq;
        IsSearching = true;
        ErrorMessage = null;
        ErrorDetail = null;
        EmptyMessage = null;
        try
        {
            var page = await _search(q, cts.Token);
            if (seq != _seq) return;   // a newer search owns the list
            var songs = SongItem.FromPage(page);
            Results.Clear();
            foreach (var s in songs) Results.Add(s);
            if (songs.Count == 0) EmptyMessage = $"No songs found for \u201c{q}\u201d.";
        }
        catch (OperationCanceledException) when (seq != _seq || cts.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (seq != _seq) return;
            ErrorMessage = "Couldn't search right now. Check your connection and try again.";
            ErrorDetail = e.Message;
        }
        finally
        {
            if (seq == _seq) IsSearching = false;
        }
    }

    /// <summary>Plays <paramref name="item"/> with all current results as the queue, starting at it.</summary>
    public Task PlayAsync(SongItem item)
    {
        var index = Results.IndexOf(item);
        if (index < 0) return Task.CompletedTask;
        foreach (var r in Results) _known[r.VideoId] = r;
        _controller.SetQueue(Results.Select(r => new QueueTrack(r.VideoId, r.Title, r.DurationSeconds)), index);
        return _controller.PlayAtAsync(index);
    }

    /// <summary>The row a queued id came from (artists, album, artwork for the player bar), if known.</summary>
    public SongItem? Find(string videoId) =>
        videoId is not null && _known.TryGetValue(videoId, out var s) ? s : null;
}
