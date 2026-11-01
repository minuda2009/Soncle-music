// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Services;
using TagLib;

namespace Soncle.Services.Tests;

/// <summary>
/// The TagLibSharp reader against real files written here: a WAV for tags and artwork, a FLAC for
/// ReplayGain (a Vorbis comment) and the "Artist - Title.mp3" fallback when a file has no tags.
/// These are the same fields the JS reads with music-metadata, so the shapes must match
/// <c>src/local.mjs</c> (title, artists, album, track/disc, year, genre, duration, codec,
/// loudnessDb, thumb).
/// </summary>
public sealed class TagLibTagReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("soncle-tags-").FullName;
    private readonly TagLibTagReader _reader = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A tiny but valid 8-bit mono WAV (TagLib can write ID3v2 into it).</summary>
    private string WriteWav(string name, int seconds = 1)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = System.IO.File.Create(path);
        using var w = new BinaryWriter(fs);
        int sr = 8000, n = sr * seconds;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + n); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr);
        w.Write((short)1); w.Write((short)8);
        w.Write("data"u8.ToArray()); w.Write(n);
        for (var i = 0; i < n; i++) w.Write((byte)(128 + 20 * Math.Sin(2 * Math.PI * 440 * i / sr)));
        return path;
    }

    [Fact]
    public void ReadsTagsDurationAndCodecFromARealFile()
    {
        var path = WriteWav("Album One/01 First Song.wav", 2);
        var f = TagLib.File.Create(path);
        f.Tag.Title = "First Song";
        f.Tag.Performers = new[] { "Test Artist", "Guest" };
        f.Tag.Album = "Album One";
        f.Tag.AlbumArtists = new[] { "Test Artist" };
        f.Tag.Track = 1;
        f.Tag.Disc = 1;
        f.Tag.Year = 2020;
        f.Tag.Genres = new[] { "Rock" };
        f.Save();

        var st = new FileInfo(path);
        var t = _reader.Read(path, st.CreationTimeUtc, st.LastWriteTimeUtc);

        Assert.Equal("First Song", t.Title);
        Assert.Equal(new[] { "Test Artist", "Guest" }, t.Artists.Select(a => a.Name).ToArray());
        Assert.Equal("Album One", t.Album!.Name);
        Assert.Equal("Test Artist", t.AlbumArtist);
        Assert.Equal(1, t.TrackNo);
        Assert.Equal(1, t.DiscNo);
        Assert.Equal(2020, t.Year);
        Assert.Equal("Rock", t.Genre);
        Assert.Equal(2, t.Duration);                       // rounded seconds, like the JS
        Assert.True(t.Local);
        Assert.Matches("^lc_[0-9a-f]{16}$", t.Id);
        Assert.StartsWith($"mstream://{t.Id}/art?v=", t.Thumb);
        Assert.Contains("PCM", t.Codec);                   // the codec line carries the codec name
        Assert.Equal(path, t.File);
        Assert.Null(t.LoudnessDb);                         // no ReplayGain written
    }

    [Fact]
    public void FallsBackToTheFileNameWhenTagsAreMissing()
    {
        // "Artist - Title.mp3" file names: the JS splits them when there are no tags
        var path = WriteWav("Some Artist - A Nice Tune.wav");
        var st = new FileInfo(path);
        var t = _reader.Read(path, st.CreationTimeUtc, st.LastWriteTimeUtc);
        Assert.Equal("A Nice Tune", t.Title);
        Assert.Equal(new[] { "Some Artist" }, t.Artists.Select(a => a.Name).ToArray());
    }

    [Fact]
    public void StripsATrackNumberPrefixFromTheFileName()
    {
        var path = WriteWav("07 Second Song.wav");
        var st = new FileInfo(path);
        var t = _reader.Read(path, st.CreationTimeUtc, st.LastWriteTimeUtc);
        Assert.Equal("Second Song", t.Title);
    }

    [Fact]
    public void ReadsEmbeddedArtwork()
    {
        var path = WriteWav("art.wav");
        var f = TagLib.File.Create(path);
        f.Tag.Pictures = new IPicture[]
        {
            new Picture(new ByteVector(new byte[] { 1, 2, 3, 4 })) { MimeType = "image/png", Type = PictureType.FrontCover },
        };
        f.Save();

        var art = _reader.ReadArt(path);
        Assert.NotNull(art);
        Assert.Equal("image/png", art!.Mime);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, art.Data);
    }

    [Fact]
    public void ReadsPlainEmbeddedLyrics()
    {
        var path = WriteWav("lyr.wav");
        var f = TagLib.File.Create(path);
        f.Tag.Lyrics = "just some words";
        f.Save();

        var ly = _reader.ReadLyrics(path);
        Assert.NotNull(ly);
        Assert.Equal("Embedded lyrics", ly!.Source);
        Assert.Null(ly.Synced);
        Assert.Equal("just some words", ly.Plain);
    }

    [Fact]
    public void ReadsTimedEmbeddedLyricsAsLrcLines()
    {
        var path = WriteWav("lyr2.wav");
        var f = TagLib.File.Create(path);
        f.Tag.Lyrics = "[00:01.00]Hello\n[00:03.50]World";
        f.Save();

        var ly = _reader.ReadLyrics(path);
        Assert.NotNull(ly);
        Assert.Equal("Embedded lyrics", ly!.Source);
        Assert.Null(ly.Plain);
        Assert.Contains("[00:01.00]Hello", ly.Synced);
        Assert.Contains("[00:03.50]World", ly.Synced);
    }

    [Fact]
    public void MissingFileThrowsSoTheLibraryCanFallBack()
    {
        Assert.ThrowsAny<Exception>(() => _reader.Read(Path.Combine(_dir, "nope.wav"), DateTime.UtcNow, DateTime.UtcNow));
    }
}
