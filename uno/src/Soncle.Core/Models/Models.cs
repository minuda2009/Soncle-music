// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soncle.Core.Models;

/// <summary>
/// The JSON shapes the UI renders (tracks, albums, pages, …). They round-trip unknown properties
/// through <see cref="ExtensionData"/>, because YouTube adds and renames things constantly and
/// <c>library.json</c> may hold keys newer than this code.
/// </summary>
public sealed class Track
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<ArtistRef> Artists { get; set; } = new();
    public string? Album { get; set; }
    public double Duration { get; set; }
    public string? Thumb { get; set; }
    public string? Type { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ArtistRef
{
    public string Name { get; set; } = "";
    public string? Id { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AlbumRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Thumb { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class Album
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Thumb { get; set; }
    public string? Year { get; set; }
    public List<ArtistRef> Artists { get; set; } = new();
    public List<Track> Tracks { get; set; } = new();
    public string? Description { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class Artist
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Thumb { get; set; }
    public List<Track> Songs { get; set; } = new();
    public List<AlbumRef> Albums { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class Playlist
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Thumb { get; set; }
    public string? Description { get; set; }
    public List<Track> Tracks { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class Shelf
{
    public string? Title { get; set; }
    public string? More { get; set; }
    public List<Track> Items { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A home/explore/search/browse page: shelves plus a continuation token.</summary>
public sealed class Page
{
    public List<Shelf> Sections { get; set; } = new();
    public bool HasMore { get; set; }
    public string? Continuation { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LyricsResult
{
    public string? Synced { get; set; }
    public string? Plain { get; set; }
    public string? Source { get; set; }
}

public sealed class StreamInfo
{
    public string? Client { get; set; }
    public string? Mime { get; set; }
    public int? Bitrate { get; set; }
    public double? Lufs { get; set; }
}

public sealed class SongInfo
{
    public string? Id { get; set; }
    public double? Lufs { get; set; }
    public bool Liked { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class HeadphoneProfile
{
    public string? Name { get; set; }
    public string? Source { get; set; }
    public double Preamp { get; set; }
    public List<EqFilter> Filters { get; set; } = new();
    public string? Rig { get; set; }
}

public sealed class EqFilter
{
    public string Type { get; set; } = "";
    public double Frequency { get; set; }
    public double Q { get; set; }
    public double Gain { get; set; }
}

public sealed class DeviceProfile
{
    public string? Type { get; set; }
    public bool Bluetooth { get; set; }
    public string? Model { get; set; }
    public double? Volume { get; set; }
    public long Updated { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AppInfo
{
    public string Version { get; set; } = "";
    public string Platform { get; set; } = "";
    public bool Mobile { get; set; }
    public bool Discord { get; set; }
    public bool Mica { get; set; }
    public bool MicaSupported { get; set; }
    public bool Dark { get; set; }
}

/// <summary>Analysed Flow features for a track (the shape stored in the Flow cache).</summary>
public sealed class FlowFeatures
{
    public double? Bpm { get; set; }
    public double BpmConf { get; set; }
    public string? Key { get; set; }
    public string? Camelot { get; set; }
    public double KeyConf { get; set; }
    public double? Energy { get; set; }
    public double? RmsDb { get; set; }
    public int V { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
