// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soncle.Core.Models;

/// <summary>
/// The JSON options every model uses: camelCase names matching the JS JSON exactly, case-insensitive
/// reads (so <c>library.json</c> from any version loads), and nulls written out (the JS does).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(Track))]
[JsonSerializable(typeof(Page))]
[JsonSerializable(typeof(Album))]
[JsonSerializable(typeof(Artist))]
[JsonSerializable(typeof(Playlist))]
[JsonSerializable(typeof(FlowFeatures))]
public partial class SoncleJsonContext : JsonSerializerContext
{
}

public static class SoncleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
