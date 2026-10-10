// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Soncle.Core.Store;

/// <summary>
/// The settings and library defaults, taken from <c>src/defaults.mjs</c> (generated into
/// <c>Defaults.g.cs</c> from <c>fixtures/store/defaults.json</c>). Deep clones are returned so a
/// caller can never mutate the defaults.
/// </summary>
public static class Defaults
{
    private static readonly JsonElement Root = JsonDocument.Parse(DefaultsData.Json).RootElement;

    public static JsonObject SettingsDeepClone() => (JsonObject)JsonNode.Parse(Root.GetProperty("defaultSettings").GetRawText())!;

    public static JsonObject LibraryDeepClone() => (JsonObject)JsonNode.Parse(Root.GetProperty("defaults").GetRawText())!;

    public static JsonObject EqDeepClone() => (JsonObject)SettingsDeepClone()["eq"]!.DeepClone();
}
