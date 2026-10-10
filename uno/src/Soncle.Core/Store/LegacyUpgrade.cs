// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json.Nodes;

namespace Soncle.Core.Store;

/// <summary>
/// Carry-over from the app's previous name, a port of the identifiers in <c>src/legacy.mjs</c>.
/// The one setting that changed id is the old crossfade curve.
/// </summary>
public static class LegacyUpgrade
{
    public const string BackupMarker = "soncle";
    private static readonly string[] LegacyBackupMarkers = { "metrolist-desktop" };

    /// <summary>The old id of the "smooth" crossfade curve.</summary>
    public const string LegacyXfCurve = "metrolist";

    /// <summary>Updates settings saved by earlier builds to current ids, in place.</summary>
    public static void Apply(JsonObject? settings)
    {
        if (settings is null) return;
        if (settings["xfCurve"] is JsonValue v && v.TryGetValue<string>(out var s) && s == LegacyXfCurve)
            settings["xfCurve"] = "smooth";
    }

    /// <summary>Whether <paramref name="marker"/> is a Soncle backup (including the old name).</summary>
    public static bool IsBackupMarker(string? marker) =>
        marker == BackupMarker || (marker is not null && Array.IndexOf(LegacyBackupMarkers, marker) >= 0);
}
