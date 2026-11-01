// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Soncle.Core.Models;
using TagLib;
using TagLib.Id3v2;

namespace Soncle.Services;

/// <summary>
/// Reads a local file's tags with TagLibSharp (LGPL-2.1). This is the app's
/// <see cref="IAudioTagReader"/>: it builds the same <see cref="LocalTrack"/> the JS builds from
/// <c>music-metadata</c> — title, artists, album, track/disc, year, genre, duration, a codec line,
/// ReplayGain (as <c>loudnessDb</c>), embedded artwork and embedded lyrics.
/// </summary>
public sealed partial class TagLibTagReader : IAudioTagReader, IArtworkTagReader, ILyricsTagReader
{
    private static readonly string[] ReplayGainKeys =
    {
        "REPLAYGAIN_TRACK_GAIN", "replaygain_track_gain", "R128_TRACK_GAIN",
    };

    public LocalTrack Read(string path, DateTime birthTime, DateTime modifiedTime)
    {
        var id = LocalLibrary.IdFor(path);
        var mtimeMs = modifiedTime.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
        var file = TagLib.File.Create(path);
        var common = file.Tag;
        var props = file.Properties;

        var title = common.Title;
        var artists = new List<string>();
        if (common.Performers.Length > 0)
            artists = common.Performers.SelectMany(LocalLibrary.SplitArtists).ToList();
        else
            artists = LocalLibrary.SplitArtists(common.FirstPerformer);

        if (string.IsNullOrWhiteSpace(title))
        {
            var baseName = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Trim();
            baseName = NumPrefixRx().Replace(baseName, "");
            var m = TitleArtistRx().Match(baseName);
            if (m.Success && artists.Count == 0)
            {
                artists = LocalLibrary.SplitArtists(m.Groups[1].Value);
                title = m.Groups[2].Value;
            }
            else title = baseName;
        }

        var rg = ReadReplayGain(file);
        var codec = props.Codecs.FirstOrDefault();
        return new LocalTrack
        {
            Id = id,
            Title = title ?? "",
            Artists = artists.Distinct().Select(n => new ArtistRef { Name = n }).ToList(),
            Album = string.IsNullOrWhiteSpace(common.Album) ? null : new AlbumRef { Name = common.Album },
            AlbumArtist = common.FirstAlbumArtist ?? "",
            TrackNo = (int)common.Track,
            DiscNo = (int)common.Disc,
            Year = (int)common.Year,
            Genre = common.FirstGenre ?? "",
            Duration = (int)Math.Round(props.Duration.TotalSeconds),
            Thumb = $"mstream://{id}/art?v={Math.Round(mtimeMs)}",
            LoudnessDb = rg is null ? null : -rg.Value,
            Codec = LocalLibrary.CodecLine(codec?.Description, props.AudioBitrate, codec is ILosslessAudioCodec),
            Added = (long)birthTime.Subtract(DateTime.UnixEpoch).TotalMilliseconds,
            File = path,
        };
    }

    /// <summary>ReplayGain in dB from ID3v2, Vorbis comments or APE items; null when absent.</summary>
    private static double? ReadReplayGain(TagLib.File file)
    {
        // ID3v2: a TXXX frame or an RVA2 frame
        if (file.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
        {
            foreach (var key in ReplayGainKeys)
            {
                var txxx = UserTextInformationFrame.Get(id3, key, false);
                if (txxx is not null && ParseGain(txxx.Text.FirstOrDefault()) is { } g1) return g1;
            }
            var rva2 = RelativeVolumeFrame.Get(id3, "RVA2", false);
            if (rva2 is not null)
            {
                var v = rva2.GetVolumeAdjustment(ChannelType.MasterVolume);
                if (Math.Abs(v) > 0.0001) return v;
            }
        }
        // Vorbis comments (FLAC / Ogg / Opus)
        if (file.GetTag(TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
            foreach (var key in ReplayGainKeys)
            {
                var f = xiph.GetField(key);
                if (f.Length > 0 && ParseGain(f[0]) is { } g2) return g2;
            }
        // APE
        if (file.GetTag(TagTypes.Ape) is TagLib.Ape.Tag ape)
            foreach (var key in ReplayGainKeys)
            {
                var item = ape.GetItem(key);
                if (item is not null && ParseGain(item.ToStringArray().FirstOrDefault()) is { } g3) return g3;
            }
        return null;
    }

    /// <summary>Parses "−6.50 dB" / "−6.50" / R128-style integer gains into dB.</summary>
    public static double? ParseGain(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = GainRx().Match(s);
        if (!m.Success) return null;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    [GeneratedRegex(@"^\s*([-+]?\d+(?:\.\d+)?)")]
    private static partial Regex GainRx();

    public LocalArt? ReadArt(string path)
    {
        using var file = TagLib.File.Create(path);
        var pic = file.Tag.Pictures.FirstOrDefault();
        if (pic?.Data?.Data is not { Length: > 0 } data) return null;
        var mime = pic.MimeType;
        if (string.IsNullOrWhiteSpace(mime)) mime = "image/jpeg";
        else if (!mime.Contains('/')) mime = "image/" + mime;
        return new LocalArt(data, mime);
    }

    public LocalLyrics? ReadLyrics(string path)
    {
        using var file = TagLib.File.Create(path);

        // A synchronised (timed) lyrics frame wins: rebuild "[mm:ss.xx]text" lines like the JS does
        if (file.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
        {
            var sync = SynchronisedLyricsFrame.GetPreferred(id3, "", "", SynchedTextType.Lyrics);
            if (sync is { Text.Length: > 0 } && sync.Text.Any(t => !string.IsNullOrEmpty(t.Text)))
            {
                var sb = new StringBuilder();
                foreach (var line in sync.Text)
                {
                    var t = line.Time / 1000.0;
                    sb.Append('[').Append(TimeSpan.FromSeconds(t).ToString(@"mm\:ss\.ff", CultureInfo.InvariantCulture)).Append(']').Append(line.Text).Append('\n');
                }
                return new LocalLyrics(sb.ToString().TrimEnd('\n'), null, "Embedded lyrics");
            }
        }

        var text = file.Tag.Lyrics;
        if (!string.IsNullOrWhiteSpace(text))
            return LrcRx().IsMatch(text)
                ? new LocalLyrics(text, null, "Embedded lyrics")
                : new LocalLyrics(null, text, "Embedded lyrics");
        return null;
    }

    [GeneratedRegex(@"\[\d{1,2}:\d{2}")]
    private static partial Regex LrcRx();

    [GeneratedRegex(@"^\d{1,3}[\s.\-_]+")]
    private static partial Regex NumPrefixRx();

    [GeneratedRegex(@"^(.+?)\s+-\s+(.+)$")]
    private static partial Regex TitleArtistRx();
}
