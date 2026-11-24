// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Soncle.App.ViewModels;
using Soncle.YouTube.Parsing;

namespace Soncle.Windows.Shell;

/// <summary>
/// Small builders for the pages: text in the Material type scale, artwork, song rows, cards and
/// shelves. The pages are built in code so one component looks the same everywhere.
/// </summary>
internal static class Ui
{
    public static Brush? Brush(string key)
    {
        try
        {
            return Application.Current.Resources.TryGetValue(key, out var v) ? v as Brush : null;
        }
        catch
        {
            return null;
        }
    }

    public static Style? StyleOf(string key)
    {
        try
        {
            return Application.Current.Resources.TryGetValue(key, out var v) ? v as Style : null;
        }
        catch
        {
            return null;
        }
    }

    public static TextBlock Text(string text, string style = "BodyMedium", bool muted = false, int maxLines = 1, bool wrap = false)
    {
        var t = new TextBlock
        {
            Text = text ?? "",
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = wrap || maxLines > 1 ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
        };
        if (maxLines > 1) t.MaxLines = maxLines;
        if (StyleOf(style) is { } s) t.Style = s;
        if (muted && Brush("OnSurfaceVariantBrush") is { } b) t.Foreground = b;
        return t;
    }

    public static ImageSource? Art(string? url, int decodeWidth)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return new BitmapImage { UriSource = uri, DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
    }

    /// <summary>A rounded artwork tile (circle for artists).</summary>
    public static Border ArtTile(string? url, double size, bool round = false)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(round ? size / 2 : Math.Min(12, size / 8)),
            Background = Brush("SurfaceVariantBrush"),
            Child = new Image { Source = Art(url, (int)(size * 2)), Stretch = Stretch.UniformToFill },
        };
    }

    public static FontIcon Icon(string glyph, double size = 16) => new() { Glyph = glyph, FontSize = size };

    public static Button IconButton(string glyph, string label, RoutedEventHandler click, double size = 40)
    {
        var b = new Button
        {
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(size / 2),
            Content = Icon(glyph, 16),
        };
        ToolTipService.SetToolTip(b, label);
        AutomationProperties.SetName(b, label);
        b.Click += click;
        return b;
    }

    public static Button Filled(string label, string? glyph, RoutedEventHandler click, bool tonal = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (glyph is not null) content.Children.Add(Icon(glyph, 16));
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, Padding = new Thickness(20, 10, 24, 10), CornerRadius = new CornerRadius(20) };
        if (StyleOf(tonal ? "FilledTonalButtonStyle" : "FilledButtonStyle") is { } s) b.Style = s;
        b.Click += click;
        return b;
    }

    /// <summary>The second line of a song row: artists · album · duration.</summary>
    public static string Details(SongItem s, bool album = true) =>
        string.Join(" · ", new[] { s.Artists, album ? s.Album : "", s.DurationText }.Where(x => !string.IsNullOrWhiteSpace(x)));

    /// <summary>Who or what a card is, under its title.</summary>
    public static string Subtitle(YtItem it)
    {
        if (!string.IsNullOrWhiteSpace(it.Subtitle)) return it.Subtitle!;
        if (it.Artists is { Count: > 0 } a) return string.Join(", ", a.Select(r => r.Name));
        return it.Type switch { "album" => "Album", "artist" => "Artist", "playlist" => "Playlist", _ => "" };
    }

    /// <summary>A clickable surface with a hover tint; <paramref name="click"/> runs on press-release.</summary>
    public static Grid Pressable(UIElement content, Action click, Action<FrameworkElement>? context = null)
    {
        var hover = Brush("SurfaceVariantBrush");
        var g = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), CornerRadius = new CornerRadius(12), Padding = new Thickness(8) };
        g.Children.Add(content);
        g.PointerEntered += (_, _) => { if (hover is not null) g.Background = hover; };
        g.PointerExited += (_, _) => g.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        g.Tapped += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        if (context is not null)
        {
            g.RightTapped += (_, e) =>
            {
                e.Handled = true;
                context(g);
            };
        }
        g.IsTabStop = true;
        g.KeyDown += (_, e) =>
        {
            if (e.Key is global::Windows.System.VirtualKey.Enter or global::Windows.System.VirtualKey.Space)
            {
                e.Handled = true;
                click();
            }
        };
        return g;
    }

    /// <summary>One song row: artwork (or the track number), title, details, duration.</summary>
    public static Grid SongRow(SongItem s, Action play, Action<FrameworkElement>? context = null, int? number = null, bool showAlbum = true)
    {
        var row = new Grid { ColumnSpacing = 16, MinHeight = 56 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (number is { } n)
        {
            var num = Text(n.ToString(System.Globalization.CultureInfo.InvariantCulture), "TitleSmall", muted: true);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(num);
        }
        else
        {
            var art = ArtTile(s.ThumbnailUrl, 48);
            art.CornerRadius = new CornerRadius(8);
            row.Children.Add(art);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(Text(s.Title, "TitleSmall"));
        text.Children.Add(Text(string.Join(" · ", new[] { s.Artists, showAlbum ? s.Album : "" }.Where(x => !string.IsNullOrWhiteSpace(x))), "BodySmall", muted: true));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var dur = Text(s.DurationText, "BodySmall", muted: true);
        dur.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(dur, 2);
        row.Children.Add(dur);
        var p = Pressable(row, play, context);
        AutomationProperties.SetName(p, s.Title);
        return p;
    }

    /// <summary>A two-row card: artwork, title, subtitle.</summary>
    public static Grid Card(YtItem it, Action open, Action<FrameworkElement>? context = null, double size = 168)
    {
        var round = it.Type == "artist";
        var stack = new StackPanel { Width = size, Spacing = 4 };
        stack.Children.Add(ArtTile(it.Thumb, size, round));
        var title = Text(it.Title ?? "", "TitleSmall", maxLines: 2);
        if (round) title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);
        var sub = Text(Subtitle(it), "BodySmall", muted: true);
        if (round) sub.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(sub);
        var p = Pressable(stack, open, context);
        AutomationProperties.SetName(p, it.Title ?? "");
        return p;
    }

    /// <summary>A section title with an optional strapline.</summary>
    public static StackPanel SectionHeader(string? title, string? strap = null)
    {
        var s = new StackPanel { Spacing = 0, Margin = new Thickness(8, 24, 8, 8) };
        if (!string.IsNullOrWhiteSpace(strap)) s.Children.Add(Text(strap!, "LabelLarge", muted: true));
        if (!string.IsNullOrWhiteSpace(title)) s.Children.Add(Text(title!, "TitleLarge"));
        return s;
    }

    /// <summary>A horizontal carousel that scrolls with the wheel (Shift+wheel) and trackpads.</summary>
    public static ScrollViewer Carousel(IEnumerable<UIElement> items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var i in items) row.Children.Add(i);
        return new ScrollViewer
        {
            Content = row,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 0, 12),
        };
    }

    /// <summary>A centred spinner for a page that is loading.</summary>
    public static UIElement Loading() => new ProgressRing
    {
        IsActive = true,
        Width = 40,
        Height = 40,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 120, 0, 0),
    };

    /// <summary>A calm message with one action (docs/DESIGN.md §4.6).</summary>
    public static UIElement Message(string title, string body, string? action = null, Action? onAction = null)
    {
        var s = new StackPanel { Spacing = 8, Margin = new Thickness(32, 96, 32, 32), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Text(title, "HeadlineSmall"));
        s.Children.Add(Text(body, "BodyMedium", muted: true, wrap: true));
        if (action is not null && onAction is not null)
        {
            var b = Filled(action, null, (_, _) => onAction());
            b.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(b);
        }
        return s;
    }

    /// <summary>Opens a flyout with <paramref name="items"/> at <paramref name="target"/>.</summary>
    public static void Menu(FrameworkElement target, params (string Glyph, string Label, Action Run)[] items)
    {
        var f = new MenuFlyout();
        foreach (var (glyph, label, run) in items)
        {
            var mi = new MenuFlyoutItem { Text = label, Icon = Icon(glyph) };
            mi.Click += (_, _) => run();
            f.Items.Add(mi);
        }
        f.ShowAt(target);
    }

    public static bool IsTyping(object? source) =>
        source is TextBox or AutoSuggestBox or PasswordBox;
}
