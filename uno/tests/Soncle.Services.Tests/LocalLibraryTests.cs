// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Core.Models;
using Soncle.Services;

namespace Soncle.Services.Tests;

/// <summary>
/// The pure parts of the local library: the id scheme (which must match <c>src/local.mjs</c> so an
/// existing <c>local-library.json</c> keeps working), the audio-extension set, artist splitting,
/// the codec line and the placeholder art.
/// </summary>
public class LocalLibraryPureTests
{
    [Fact]
    public void LocalIdMatchesTheJsScheme()
    {
        // id = "lc_" + first 16 hex of SHA-1 of the resolved lowercase path (local.mjs idFor)
        var path = Path.Combine(Path.GetTempPath(), "Some Folder", "Song.mp3");
        var expected = "lc_" + Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())))[..16].ToLowerInvariant();
        Assert.Equal(expected, LocalLibrary.IdFor(path));
        Assert.Matches("^lc_[0-9a-f]{16}$", LocalLibrary.IdFor(path));
    }

    [Fact]
    public void LocalIdsAreCaseInsensitiveOnThePath()
    {
        var a = Path.Combine(Path.GetTempPath(), "Music", "Track.flac");
        var b = Path.Combine(Path.GetTempPath(), "MUSIC", "TRACK.FLAC");
        Assert.Equal(LocalLibrary.IdFor(a), LocalLibrary.IdFor(b));
    }

    [Fact]
    public void RecognisesLocalIdsAndAudioExtensions()
    {
        Assert.True(LocalLibrary.IsLocalId("lc_0123456789abcdef"));
        Assert.False(LocalLibrary.IsLocalId("lc_0123456789ABCDEF"));   // hex is lowercase, as the JS writes it
        Assert.False(LocalLibrary.IsLocalId("dQw4w9WgXcQ"));
        Assert.True(LocalLibrary.IsAudioFile("a.MP3"));
        Assert.True(LocalLibrary.IsAudioFile("a.flac"));
        Assert.True(LocalLibrary.IsAudioFile("a.opus"));
        Assert.False(LocalLibrary.IsAudioFile("cover.jpg"));
    }

    [Fact]
    public void SplitsArtistsLikeTheJs()
    {
        Assert.Equal(new[] { "A", "B" }, LocalLibrary.SplitArtists("A; B"));
        Assert.Equal(new[] { "A", "B" }, LocalLibrary.SplitArtists("A feat. B"));
        Assert.Equal(new[] { "A", "B" }, LocalLibrary.SplitArtists("A & B"));
        Assert.Equal(new[] { "A", "B", "C" }, LocalLibrary.SplitArtists("A, B / C"));
        Assert.Empty(LocalLibrary.SplitArtists(null));
    }

    [Fact]
    public void BuildsTheCodecLineLikeTheJs()
    {
        Assert.Equal("FLAC • 900 kbps • lossless", LocalLibrary.CodecLine("FLAC", 900_000, true));
        Assert.Equal("MPEG-1 Layer 3 • 320 kbps", LocalLibrary.CodecLine("MPEG-1 Layer 3", 320_000, false));
        Assert.Equal("", LocalLibrary.CodecLine(null, null, false));
    }

    [Fact]
    public void PlaceholderArtIsDeterministic()
    {
        var a = LocalLibrary.PlaceholderArt("lc_0123456789abcdef");
        var b = LocalLibrary.PlaceholderArt("lc_0123456789abcdef");
        Assert.Equal(a, b);
        Assert.StartsWith("<svg", a);
        Assert.NotEqual(a, LocalLibrary.PlaceholderArt("lc_fedcba9876543210"));
    }

    [Fact]
    public void ParsesReplayGainStrings()
    {
        Assert.Equal(-6.5, TagLibTagReader.ParseGain("-6.50 dB")!.Value, 3);
        Assert.Equal(2.0, TagLibTagReader.ParseGain("+2.0 dB")!.Value, 3);
        Assert.Equal(-6.5, TagLibTagReader.ParseGain("-6.5")!.Value, 3);
        Assert.Null(TagLibTagReader.ParseGain(""));
        Assert.Null(TagLibTagReader.ParseGain("nonsense"));
    }
}

/// <summary>The library's folder/file bookkeeping with a fake tag reader (no real files' tags).</summary>
public sealed class LocalLibraryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("soncle-local-").FullName;
    private readonly FakeTagReader _tags = new();
    private readonly LocalLibrary _lib;

    public LocalLibraryTests()
    {
        _lib = new LocalLibrary(_tags);
        _lib.Init(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeFile(string rel, string content = "x")
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public async Task ScansAFolderAndIndexesAudioFilesOnly()
    {
        MakeFile("Album One/01 First Song.mp3");
        MakeFile("Album One/cover.jpg");           // not audio
        MakeFile("Album One/02 Second Song.flac");
        MakeFile("loose/Untagged Tune.wav");
        _lib.AddFolder(_dir);

        var tracks = await _lib.Scan();
        Assert.Equal(3, tracks.Count);
        Assert.All(tracks, t => Assert.True(t.Local));
        Assert.All(tracks, t => Assert.Matches("^lc_[0-9a-f]{16}$", t.Id));
    }

    [Fact]
    public async Task ScanIsSharedWhenCalledTwiceConcurrently()
    {
        MakeFile("a.mp3");
        _lib.AddFolder(_dir);
        var t1 = _lib.Scan();
        var t2 = _lib.Scan();
        Assert.Same(t1, t2);
        Assert.Single(await t1);
    }

    [Fact]
    public async Task ReportsProgressAndFinishes()
    {
        MakeFile("a.mp3");
        MakeFile("b.flac");
        _lib.AddFolder(_dir);
        var reports = new List<ScanProgress>();
        await _lib.Scan(p => { lock (reports) reports.Add(p); });
        lock (reports)
        {
            Assert.NotEmpty(reports);
            Assert.True(reports[^1].Finished);
            Assert.Equal(2, reports[^1].Total);
            Assert.Equal(2, reports[^1].Done);
        }
    }

    [Fact]
    public async Task RescanningKeepsTheSameIdsAndDropsVanishedFiles()
    {
        var f = MakeFile("a.mp3");
        _lib.AddFolder(_dir);
        var first = await _lib.Scan();
        Assert.Single(first);

        // unchanged file: the reader is not called again (mtime+size match)
        _tags.Calls = 0;
        await _lib.Scan();
        Assert.Equal(0, _tags.Calls);

        File.Delete(f);
        Assert.Empty(await _lib.Scan());
    }

    [Fact]
    public async Task RemoveFolderDropsItsTracks()
    {
        MakeFile("sub/a.mp3");
        _lib.AddFolder(_dir);
        Assert.Single(await _lib.Scan());
        _lib.RemoveFolder(_dir);
        Assert.Empty(_lib.Tracks());
    }

    [Fact]
    public async Task TracksSortByAlbumArtistThenAlbumThenDiscTrack()
    {
        MakeFile("a.mp3"); MakeFile("b.mp3"); MakeFile("c.mp3");
        _tags.ByFile = f => Path.GetFileName(f) switch
        {
            "a.mp3" => new LocalTrack { Id = LocalLibrary.IdFor(f), Title = "Zeta", AlbumArtist = "Band", Album = new AlbumRef { Name = "B" }, TrackNo = 2 },
            "b.mp3" => new LocalTrack { Id = LocalLibrary.IdFor(f), Title = "Alpha", AlbumArtist = "Band", Album = new AlbumRef { Name = "A" }, TrackNo = 1 },
            _ => new LocalTrack { Id = LocalLibrary.IdFor(f), Title = "Mid", AlbumArtist = "Another", Album = new AlbumRef { Name = "A" } },
        };
        _lib.AddFolder(_dir);
        var tracks = await _lib.Scan();
        Assert.Equal(new[] { "Mid", "Alpha", "Zeta" }, tracks.Select(t => t.Title).ToList());
    }

    [Fact]
    public async Task OpenFilesAddsLooseFilesThatSurviveARescan()
    {
        var f = MakeFile("loose/song.mp3");
        var opened = await _lib.OpenFilesAsync(new[] { f });
        Assert.Single(opened);
        Assert.Single(_lib.Tracks());

        // a scan with no folders keeps the loose file (the JS's `loose` rule)
        Assert.Single(await _lib.Scan());
    }

    [Fact]
    public async Task PathForAndTrackForResolveAnId()
    {
        var f = MakeFile("a.mp3");
        _lib.AddFolder(_dir);
        var t = (await _lib.Scan()).Single();
        Assert.Equal(f, _lib.PathFor(t.Id));
        Assert.Equal(t.Id, _lib.TrackFor(t.Id)!.Id);
        Assert.Null(_lib.PathFor("lc_ffffffffffffffff"));
    }

    [Fact]
    public async Task ReloadingTheLibraryFileKeepsTheTracks()
    {
        MakeFile("a.mp3");
        _lib.AddFolder(_dir);
        var n = (await _lib.Scan()).Count;
        _lib.Flush();

        var again = new LocalLibrary(_tags);
        again.Init(_dir);
        Assert.Equal(n, again.Tracks().Count);
        Assert.Single(again.Folders());
    }

    [Fact]
    public async Task ArtworkPrefersTheEmbeddedCover()
    {
        MakeFile("a.mp3");
        _tags.Art = _ => new LocalArt(new byte[] { 1, 2, 3 }, "image/png");
        _lib.AddFolder(_dir);
        var t = (await _lib.Scan()).Single();
        var art = await _lib.ArtworkAsync(t.Id);
        Assert.NotNull(art);
        Assert.Equal("image/png", art!.Mime);
    }

    [Fact]
    public async Task ArtworkFallsBackToAFolderImage()
    {
        MakeFile("a.mp3");
        File.WriteAllBytes(Path.Combine(_dir, "cover.jpg"), new byte[] { 9, 9, 9 });
        _tags.Art = _ => null;
        _lib.AddFolder(_dir);
        var t = (await _lib.Scan()).Single();
        var art = await _lib.ArtworkAsync(t.Id);
        Assert.NotNull(art);
        Assert.Equal("image/jpeg", art!.Mime);
    }

    [Fact]
    public async Task LyricsPreferASidecarLrcFile()
    {
        MakeFile("a.mp3");
        File.WriteAllText(Path.Combine(_dir, "a.lrc"), "[00:01.00]Hello local\n[00:03.00]World");
        _lib.AddFolder(_dir);
        var t = (await _lib.Scan()).Single();
        var ly = await _lib.LyricsAsync(t.Id);
        Assert.NotNull(ly);
        Assert.Equal("Local .lrc file", ly!.Source);
        Assert.Contains("Hello local", ly.Synced);
        Assert.Null(ly.Plain);
    }

    [Fact]
    public async Task LyricsSidecarWithoutTimingIsPlainText()
    {
        MakeFile("a.mp3");
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "just words");
        _lib.AddFolder(_dir);
        var t = (await _lib.Scan()).Single();
        var ly = await _lib.LyricsAsync(t.Id);
        Assert.NotNull(ly);
        Assert.Null(ly!.Synced);
        Assert.Equal("just words", ly.Plain);
    }

    private sealed class FakeTagReader : IAudioTagReader, IArtworkTagReader, ILyricsTagReader
    {
        public int Calls { get; set; }
        public Func<string, LocalTrack>? ByFile { get; set; }
        public Func<string, LocalArt?>? Art { get; set; }

        public LocalTrack Read(string path, DateTime birthTime, DateTime modifiedTime)
        {
            Calls++;
            return ByFile?.Invoke(path) ?? new LocalTrack
            {
                Id = LocalLibrary.IdFor(path),
                Title = Path.GetFileNameWithoutExtension(path),
                File = path,
                Duration = 200,
            };
        }

        public LocalArt? ReadArt(string path) => Art?.Invoke(path);

        public LocalLyrics? ReadLyrics(string path) => null;
    }
}
