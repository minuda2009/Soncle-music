// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App.ViewModels;
using Soncle.Windows.Playback;
using Soncle.YouTube.Parsing;

namespace Soncle.Windows.Shell;

/// <summary>
/// The content pages, each a <see cref="PageEntry"/>: Home, Search, album, playlist, artist and a
/// generic "more" page. They read the catalog through <see cref="PlaybackHost"/> and play through
/// its queue helpers, so every page plays songs the same way.
/// </summary>
internal sealed class Views
{
    private readonly PlaybackHost _host;
    private readonly Navigator _nav;

    public Views(PlaybackHost host, Navigator nav)
    {
        _host = host;
        _nav = nav;
    }

    // ---- entries ------------------------------------------------------------------------------

    public PageEntry Home() => new("home", "Home", BuildHomeAsync);

    public PageEntry Search(string query) => new("search:" + query, query, ct => BuildSearchAsync(query, ct));

    public PageEntry Album(string id) => new("album:" + id, "Album", ct => BuildAlbumAsync(id, ct));

    public PageEntry Playlist(string id) => new("playlist:" + id, "Playlist", ct => BuildPlaylistAsync(id, ct));

    public PageEntry Artist(string id) => new("artist:" + id, "Artist", ct => BuildArtistAsync(id, ct));

    public PageEntry Browse(string id, string? parameters, string title) =>
        new("browse:" + id + ":" + parameters, title, ct => BuildBrowseAsync(id, parameters, title, ct));

    /// <summary>What a card or a row does when it is picked.</summary>
    public void Open(YtItem it)
    {
        if (string.IsNullOrEmpty(it.Id)) return;
        var id = it.Id!;
        switch (Kind(it))
        {
            case "song":
                if (SongItem.From(it) is { } s) Run(_host.PlayWithRadioAsync(s), "play");
                break;
            case "album":
                _nav.Push(Album(id));
                break;
            case "artist":
                _nav.Push(Artist(id));
                break;
            case "playlist":
                _nav.Push(Playlist(id));
                break;
            default:
                _nav.Push(Browse(id, it.Params, it.Title ?? ""));
                break;
        }
    }

    private static string Kind(YtItem it)
    {
        if (it.Type is "song" or "video") return "song";
        if (it.Type is "album" or "artist" or "playlist") return it.Type;
        var id = it.Id ?? "";
        if (id.StartsWith("MPRE", StringComparison.Ordinal)) return "album";
        if (id.StartsWith("UC", StringComparison.Ordinal)) return "artist";
        if (id.StartsWith("VL", StringComparison.Ordinal) || id.StartsWith("PL", StringComparison.Ordinal)
            || id.StartsWith("RD", StringComparison.Ordinal) || id.StartsWith("OLAK", StringComparison.Ordinal)) return "playlist";
        return "browse";
    }

    private static async void Run(Task task, string what)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            PlaybackHost.Log($"{what}-failed {e.GetType().Name}");
        }
    }

    // ---- shared pieces ------------------------------------------------------------------------

    /// <summary>The context menu of a song: play next, add to queue, go to album / artist.</summary>
    private Action<FrameworkElement> SongMenu(SongItem s, YtItem? source)
    {
        return target =>
        {
            var items = new List<(string, string, Action)>
            {
                ("", "Play next", () => _host.PlayNext(new[] { s })),
                ("", "Add to queue", () => _host.AddToQueue(new[] { s })),
                ("", "Start radio", () => Run(_host.PlayWithRadioAsync(s), "radio")),
            };
            if (source?.Album is { Id.Length: > 0 } al) items.Add(("", "Go to album", () => _nav.Push(Album(al.Id))));
            if (source?.Artists?.FirstOrDefault(a => !string.IsNullOrEmpty(a.Id)) is { } ar) items.Add(("", "Go to artist", () => _nav.Push(Artist(ar.Id!))));
            Ui.Menu(target, items.ToArray());
        };
    }

    private Action<FrameworkElement>? CardMenu(YtItem it)
    {
        if (Kind(it) == "song" && SongItem.From(it) is { } s) return SongMenu(s, it);
        return null;
    }

    /// <summary>A list of song rows; picking one plays the whole list from there.</summary>
    private StackPanel SongList(IReadOnlyList<YtItem> items, bool numbered = false, bool showAlbum = true)
    {
        var pairs = items.Select(i => (Item: i, Song: SongItem.From(i))).Where(p => p.Song is not null).ToList();
        var songs = pairs.Select(p => p.Song!).ToList();
        var list = new StackPanel { Spacing = 0 };
        for (var i = 0; i < pairs.Count; i++)
        {
            var index = i;
            var (item, song) = pairs[i];
            list.Children.Add(Ui.SongRow(song!, () => Run(_host.PlaySongsAsync(songs, index), "play"),
                SongMenu(song!, item), numbered ? index + 1 : null, showAlbum));
        }
        // rows that are not songs (an album or artist inside a list shelf) open like cards
        foreach (var other in items.Where(i => SongItem.From(i) is null && !string.IsNullOrEmpty(i.Id)))
        {
            var it = other;
            var row = new Grid { ColumnSpacing = 16, MinHeight = 56 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Ui.ArtTile(it.Thumb, 48, it.Type == "artist"));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            text.Children.Add(Ui.Text(it.Title ?? "", "TitleSmall"));
            text.Children.Add(Ui.Text(Ui.Subtitle(it), "BodySmall", muted: true));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Children.Add(Ui.Pressable(row, () => Open(it)));
        }
        return list;
    }

    /// <summary>Shelves as YouTube Music lays them out: carousels of cards, lists of songs, text.</summary>
    private StackPanel Shelves(IEnumerable<YtShelf> shelves)
    {
        var page = new StackPanel { Spacing = 0 };
        foreach (var sh in shelves)
        {
            if (sh.Layout == "text")
            {
                if (string.IsNullOrWhiteSpace(sh.Text)) continue;
                page.Children.Add(Ui.SectionHeader(sh.Title));
                var t = Ui.Text(sh.Text!, "BodyMedium", muted: true, wrap: true);
                t.MaxLines = 8;
                t.Margin = new Thickness(8, 0, 8, 0);
                t.MaxWidth = 900;
                t.HorizontalAlignment = HorizontalAlignment.Left;
                page.Children.Add(t);
                continue;
            }
            if (sh.Items.Count == 0) continue;
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Ui.SectionHeader(sh.Title, sh.Strapline));
            if (sh.More is { } more && more.TryGetValue("id", out var moreId) && !string.IsNullOrEmpty(moreId))
            {
                more.TryGetValue("params", out var moreParams);
                var title = sh.Title ?? "";
                var b = new HyperlinkButton { Content = "More", VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, 8) };
                b.Click += (_, _) => _nav.Push(Browse(moreId, string.IsNullOrEmpty(moreParams) ? null : moreParams, title));
                Grid.SetColumn(b, 1);
                header.Children.Add(b);
            }
            page.Children.Add(header);

            var allSongs = sh.Items.All(i => i.Type is "song" or "video");
            if (sh.Layout == "list" || (allSongs && sh.Items.Count > 0 && sh.Layout != "carousel"))
            {
                page.Children.Add(SongList(sh.Items));
            }
            else if (sh.Layout is "grid" or "moods")
            {
                var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = sh.Layout == "moods" ? 200 : 184 };
                foreach (var it in sh.Items)
                {
                    var item = it;
                    wrap.Children.Add(sh.Layout == "moods" ? MoodTile(item) : Ui.Card(item, () => Open(item), CardMenu(item)));
                }
                page.Children.Add(wrap);
            }
            else
            {
                page.Children.Add(Ui.Carousel(sh.Items.Select(it =>
                {
                    var item = it;
                    return (UIElement)Ui.Card(item, () => Open(item), CardMenu(item), allSongs ? 152 : 168);
                })));
            }
        }
        return page;
    }

    private UIElement MoodTile(YtItem it)
    {
        var border = new Border
        {
            Height = 56,
            Width = 184,
            CornerRadius = new CornerRadius(12),
            Background = Ui.Brush("SurfaceVariantBrush"),
            Padding = new Thickness(16, 0, 16, 0),
            Child = Ui.Text(it.Title ?? "", "TitleSmall"),
        };
        ((TextBlock)border.Child).VerticalAlignment = VerticalAlignment.Center;
        return Ui.Pressable(border, () => Open(it));
    }

    /// <summary>A collection header: big artwork, title, subtitle lines and the actions.</summary>
    private static Grid CollectionHeader(string thumb, string title, IEnumerable<string> lines, IEnumerable<UIElement> actions, bool round = false)
    {
        var g = new Grid { ColumnSpacing = 32, Margin = new Thickness(8, 24, 8, 16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(Ui.ArtTile(thumb, 224, round));
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 6 };
        info.Children.Add(Ui.Text(title, "HeadlineLarge", maxLines: 2));
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            info.Children.Add(Ui.Text(line, "BodyMedium", muted: true, maxLines: 3));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var a in actions) row.Children.Add(a);
        info.Children.Add(row);
        Grid.SetColumn(info, 1);
        g.Children.Add(info);
        return g;
    }

    // ---- pages --------------------------------------------------------------------------------

    private async Task<UIElement> BuildHomeAsync(CancellationToken ct)
    {
        var catalog = await _host.CatalogAsync(ct);
        var home = await catalog.HomeAsync(ct);
        var page = new StackPanel();
        page.Children.Add(Ui.Text(Greeting(), "HeadlineMedium"));
        ((TextBlock)page.Children[0]).Margin = new Thickness(8, 16, 8, 0);
        if (home.Sections.Count == 0)
        {
            page.Children.Add(Ui.Message("Nothing here yet", "YouTube Music sent an empty home page. Search for something to play in the box at the top."));
            return page;
        }
        page.Children.Add(Shelves(home.Sections));
        return page;
    }

    private static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return h < 5 ? "Good night" : h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
    }

    private async Task<UIElement> BuildSearchAsync(string query, CancellationToken ct)
    {
        var page = await _host.SearchPageAsync(query, ct);
        var root = new StackPanel();
        var title = Ui.Text($"Results for “{query}”", "HeadlineSmall");
        title.Margin = new Thickness(8, 16, 8, 0);
        root.Children.Add(title);
        if (!string.IsNullOrEmpty(page.Correction))
        {
            var fix = new HyperlinkButton { Content = $"Did you mean “{page.Correction}”?", Margin = new Thickness(0, 4, 0, 0) };
            var corrected = page.Correction!;
            fix.Click += (_, _) => _nav.Push(Search(corrected));
            root.Children.Add(fix);
        }
        if (page.Top is { } top && !string.IsNullOrEmpty(top.Id))
        {
            root.Children.Add(Ui.SectionHeader("Top result"));
            var topRow = new Grid { ColumnSpacing = 24 };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var card = new Grid { ColumnSpacing = 16, Padding = new Thickness(8), MinWidth = 360 };
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            card.Children.Add(Ui.ArtTile(top.Thumb, 96, top.Type == "artist"));
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
            info.Children.Add(Ui.Text(top.Title ?? "", "TitleLarge"));
            info.Children.Add(Ui.Text(Ui.Subtitle(top), "BodyMedium", muted: true));
            if (Kind(top) == "song" && SongItem.From(top) is { } ts)
                info.Children.Add(Ui.Filled("Play", "", (_, _) => Run(_host.PlayWithRadioAsync(ts), "play")));
            Grid.SetColumn(info, 1);
            card.Children.Add(info);
            topRow.Children.Add(Ui.Pressable(card, () => Open(top), CardMenu(top)));
            if (top.Related is { Count: > 0 } rel)
            {
                var side = SongList(rel.Take(4).ToList());
                Grid.SetColumn(side, 1);
                topRow.Children.Add(side);
            }
            root.Children.Add(topRow);
        }
        if (page.Sections.Count == 0 && page.Top is null)
        {
            root.Children.Add(Ui.Message("No results", $"Nothing matched “{query}”. Check the spelling or try fewer words."));
            return root;
        }
        root.Children.Add(Shelves(page.Sections));
        return root;
    }

    private async Task<UIElement> BuildAlbumAsync(string id, CancellationToken ct)
    {
        var catalog = await _host.CatalogAsync(ct);
        var a = await catalog.AlbumAsync(id, ct);
        var songs = a.Tracks.Select(SongItem.From).OfType<SongItem>().ToList();
        var actions = new List<UIElement>
        {
            Ui.Filled("Play", "", (_, _) => Run(_host.PlaySongsAsync(songs, 0), "play")),
            Ui.Filled("Shuffle", "", (_, _) => Run(_host.PlaySongsAsync(Shuffled(songs), 0), "shuffle"), tonal: true),
        };
        var albumPlaylist = a.PlaylistId;
        if (!string.IsNullOrEmpty(albumPlaylist))
            actions.Add(Ui.Filled("Radio", "", (_, _) => Run(_host.PlayRadioAsync("RDAMPL" + albumPlaylist), "radio"), tonal: true));
        actions.Add(Ui.IconButton("", "Add to queue", (_, _) => _host.AddToQueue(songs)));
        var artists = a.Info.Artists is { Count: > 0 } ar ? string.Join(", ", ar.Select(x => x.Name)) : "";
        var total = songs.Sum(s => s.DurationSeconds);
        var count = $"{songs.Count} song{(songs.Count == 1 ? "" : "s")}" + (total > 0 ? $" · {Math.Round(total / 60)} min" : "");
        var root = new StackPanel();
        root.Children.Add(CollectionHeader(a.Info.Thumb, a.Info.Title, new[] { a.Info.Subtitle, artists, a.Info.Second.Length > 0 ? a.Info.Second : count }, actions));
        if (a.Info.Artists?.FirstOrDefault(x => !string.IsNullOrEmpty(x.Id)) is { } artist)
        {
            var go = new HyperlinkButton { Content = artist.Name, Margin = new Thickness(0, -8, 0, 8) };
            go.Click += (_, _) => _nav.Push(Artist(artist.Id!));
            root.Children.Add(go);
        }
        root.Children.Add(SongList(a.Tracks, numbered: true, showAlbum: false));
        if (!string.IsNullOrWhiteSpace(a.Info.Description))
        {
            root.Children.Add(Ui.SectionHeader("About"));
            var d = Ui.Text(a.Info.Description, "BodyMedium", muted: true, wrap: true);
            d.MaxWidth = 900;
            d.HorizontalAlignment = HorizontalAlignment.Left;
            d.Margin = new Thickness(8, 0, 8, 0);
            root.Children.Add(d);
        }
        root.Children.Add(Shelves(a.Sections));
        return root;
    }

    private async Task<UIElement> BuildPlaylistAsync(string id, CancellationToken ct)
    {
        var catalog = await _host.CatalogAsync(ct);
        var p = await catalog.PlaylistAsync(id, ct);
        var songs = p.Tracks.Select(SongItem.From).OfType<SongItem>().ToList();
        var bare = id.StartsWith("VL", StringComparison.Ordinal) ? id[2..] : id;
        var actions = new List<UIElement>
        {
            Ui.Filled("Play", "", (_, _) => Run(_host.PlaySongsAsync(songs, 0), "play")),
            Ui.Filled("Shuffle", "", (_, _) => Run(_host.PlaySongsAsync(Shuffled(songs), 0), "shuffle"), tonal: true),
            Ui.Filled("Radio", "", (_, _) => Run(_host.PlayRadioAsync("RDAMPL" + bare), "radio"), tonal: true),
            Ui.IconButton("", "Add to queue", (_, _) => _host.AddToQueue(songs)),
        };
        var root = new StackPanel();
        var count = $"{songs.Count}{(p.HasMore ? "+" : "")} songs";
        root.Children.Add(CollectionHeader(p.Info.Thumb, p.Info.Title, new[] { p.Info.Subtitle, p.Info.Second.Length > 0 ? p.Info.Second : count, p.Info.Description }, actions));
        if (songs.Count == 0)
        {
            root.Children.Add(Ui.Message("This playlist is empty", "Or YouTube didn't send its songs. Try again later."));
            return root;
        }
        root.Children.Add(SongList(p.Tracks));
        return root;
    }

    private async Task<UIElement> BuildArtistAsync(string id, CancellationToken ct)
    {
        var catalog = await _host.CatalogAsync(ct);
        var a = await catalog.ArtistAsync(id, ct);
        var actions = new List<UIElement>();
        var shuffleId = a.ShuffleId;
        var radioId = a.RadioId;
        if (!string.IsNullOrEmpty(shuffleId)) actions.Add(Ui.Filled("Shuffle", "", (_, _) => Run(_host.PlayRadioAsync(shuffleId!), "shuffle")));
        if (!string.IsNullOrEmpty(radioId)) actions.Add(Ui.Filled("Radio", "", (_, _) => Run(_host.PlayRadioAsync(radioId!), "radio"), tonal: !string.IsNullOrEmpty(shuffleId)));
        var stats = string.Join(" · ", new[] { a.Listeners, string.IsNullOrEmpty(a.Subscribers) ? "" : a.Subscribers + " subscribers" }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var root = new StackPanel();
        root.Children.Add(CollectionHeader(a.Thumb, a.Title, new[] { stats }, actions, round: true));
        root.Children.Add(Shelves(a.Sections));
        if (!string.IsNullOrWhiteSpace(a.Description))
        {
            root.Children.Add(Ui.SectionHeader("About"));
            var d = Ui.Text(a.Description, "BodyMedium", muted: true, wrap: true);
            d.MaxWidth = 900;
            d.HorizontalAlignment = HorizontalAlignment.Left;
            d.Margin = new Thickness(8, 0, 8, 0);
            root.Children.Add(d);
        }
        return root;
    }

    private async Task<UIElement> BuildBrowseAsync(string id, string? parameters, string title, CancellationToken ct)
    {
        var catalog = await _host.CatalogAsync(ct);
        var b = await catalog.BrowseAsync(id, parameters, ct);
        var root = new StackPanel();
        var t = Ui.Text(string.IsNullOrEmpty(b.Title) ? title : b.Title, "HeadlineMedium");
        t.Margin = new Thickness(8, 16, 8, 0);
        root.Children.Add(t);
        if (b.Sections.Count == 0)
        {
            root.Children.Add(Ui.Message("Nothing to show", "This page came back empty."));
            return root;
        }
        root.Children.Add(Shelves(b.Sections));
        return root;
    }

    private static List<SongItem> Shuffled(List<SongItem> songs)
    {
        var list = songs.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
