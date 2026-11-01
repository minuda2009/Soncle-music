// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using Soncle.YouTube.Parsing;

namespace Soncle.App.ViewModels;

/// <summary>One playable search result row.</summary>
public sealed record SongItem(string VideoId, string Title, string Artists, string Album, double DurationSeconds, string ThumbnailUrl)
{
    /// <summary>m:ss (h:mm:ss past an hour); empty when unknown.</summary>
    public string DurationText => DurationSeconds > 0 ? TimeText.Format(DurationSeconds) : "";

    /// <summary>A song or video row with an id; anything else (albums, artists, playlists) is null.</summary>
    public static SongItem? From(YtItem item)
    {
        if (item is null || item.Type is not ("song" or "video") || string.IsNullOrEmpty(item.Id)) return null;
        var artists = item.Artists is { Count: > 0 } a
            ? string.Join(", ", a.Select(r => r.Name).Where(n => !string.IsNullOrWhiteSpace(n)))
            : item.Subtitle ?? "";
        return new SongItem(item.Id, item.Title ?? "", artists, item.Album?.Name ?? "",
            double.IsFinite(item.Duration) ? Math.Max(0, item.Duration) : 0, item.Thumb ?? "");
    }

    /// <summary>The playable rows of a search page in display order (top result first), each id once.</summary>
    public static List<SongItem> FromPage(YtSearchPage page)
    {
        var list = new List<SongItem>();
        if (page is null) return list;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = (page.Top is { } top ? new[] { top } : Array.Empty<YtItem>()).Concat(page.Sections.SelectMany(s => s.Items));
        foreach (var row in rows)
            if (From(row) is { } song && seen.Add(song.VideoId)) list.Add(song);
        return list;
    }
}

/// <summary>Time formatting for the player bar and result rows.</summary>
public static class TimeText
{
    /// <summary>m:ss, or h:mm:ss from an hour; negative or non-finite is 0:00.</summary>
    public static string Format(double seconds)
    {
        var total = double.IsFinite(seconds) && seconds > 0 ? (long)Math.Floor(seconds) : 0;
        var h = total / 3600;
        var m = total / 60 % 60;
        var s = total % 60;
        return h > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, s)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, s);
    }
}
