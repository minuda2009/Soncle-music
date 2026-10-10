// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Soncle.Core.Store;

namespace Soncle.Services;

public sealed record CarryOverReport(IReadOnlyList<string> Copied, string? From = null, string? Skipped = null, string? Error = null);

/// <summary>
/// One-time copy of the previous build's user data into this app's folder, a port of
/// <c>carryOver</c> in <c>src/legacy.mjs</c>. Copy, never move: the old data stays where it was.
/// The new app also accepts the Electron app's folder as a source.
/// </summary>
public static class CarryOver
{
    public const string MarkerFile = ".carried-over.json";
    public const string LegacyDirName = "Metrolist";
    public const string SoncleDirName = "Soncle";

    /// <summary>What gets copied. Copy, never move.</summary>
    private static readonly string[] Items = { "downloads", "yt-cache-v2", "flow-features.json", "local-library.json", "autoeq" };

    /// <summary>
    /// Looks for the previous build's data in <paramref name="appDataDir"/> (the old "Metrolist"
    /// folder, then the Electron "Soncle" folder) and copies it into <paramref name="newDir"/> once.
    /// Returns the report, or null when it was already done.
    /// </summary>
    public static CarryOverReport? CarryOverData(string appDataDir, string newDir, Action<string>? log = null)
    {
        var markerPath = Path.Combine(newDir, MarkerFile);
        if (File.Exists(markerPath)) return null;

        CarryOverReport Done(CarryOverReport report)
        {
            try
            {
                Directory.CreateDirectory(newDir);
                File.WriteAllText(markerPath, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow.ToString("o"), copied = report.Copied, from = report.From, skipped = report.Skipped }));
            }
            catch (Exception e) { log?.Invoke("carry-over marker: " + e.Message); }
            return report;
        }

        var oldDir = FindSource(appDataDir);
        if (oldDir is null) return Done(new CarryOverReport(Array.Empty<string>()));
        if (Path.GetFullPath(oldDir).Equals(Path.GetFullPath(newDir), StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(oldDir, "library.json")))
            return Done(new CarryOverReport(Array.Empty<string>()));
        // never overwrite a library that already exists here
        if (File.Exists(Path.Combine(newDir, "library.json")))
            return Done(new CarryOverReport(Array.Empty<string>(), Skipped: "library already present"));

        var copied = new List<string>();
        try
        {
            Directory.CreateDirectory(newDir);
            var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(oldDir, "library.json"))) as JsonObject ?? new JsonObject();
            var oldPrefix = Path.GetFullPath(oldDir);
            if (raw["downloads"] is JsonObject downloads)
                foreach (var kv in downloads)
                    if (kv.Value is JsonObject d && d["file"] is JsonValue fv && fv.TryGetValue<string>(out var file))
                    {
                        var full = Path.GetFullPath(file);
                        if (full.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                            d["file"] = Path.Combine(newDir, Path.GetRelativePath(oldPrefix, full));
                    }
            LegacyUpgrade.Apply(raw["settings"] as JsonObject);
            File.WriteAllText(Path.Combine(newDir, "library.json"), raw.ToJsonString());
            copied.Add("library.json");
            foreach (var item in Items)
            {
                var src = Path.Combine(oldDir, item);
                if (!File.Exists(src) && !Directory.Exists(src)) continue;
                try { CopyRecursive(src, Path.Combine(newDir, item)); copied.Add(item); }
                catch (Exception e) { log?.Invoke($"carry-over: {item}: {e.Message}"); }
            }
            // Chromium keeps the key that encrypts the saved sign-in in "Local State".
            var ls = Path.Combine(oldDir, "Local State");
            var lsNew = Path.Combine(newDir, "Local State");
            if (File.Exists(ls) && !File.Exists(lsNew))
            {
                try { File.Copy(ls, lsNew); copied.Add("Local State"); } catch { /* optional */ }
            }
        }
        catch (Exception e)
        {
            log?.Invoke("carry-over failed: " + e.Message);
            return new CarryOverReport(copied, Error: e.Message);   // no marker: try again next start
        }
        log?.Invoke($"carried over {string.Join(", ", copied)} from the previous version");
        return Done(new CarryOverReport(copied, From: oldDir));
    }

    private static string? FindSource(string appDataDir)
    {
        foreach (var name in new[] { LegacyDirName, SoncleDirName })
        {
            var dir = Path.Combine(appDataDir, name);
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    private static void CopyRecursive(string src, string dst)
    {
        if (File.Exists(src)) { Directory.CreateDirectory(Path.GetDirectoryName(dst)!); File.Copy(src, dst, overwrite: false); return; }
        Directory.CreateDirectory(dst);
        foreach (var entry in Directory.EnumerateFileSystemEntries(src))
            CopyRecursive(entry, Path.Combine(dst, Path.GetFileName(entry)));
    }
}

/// <summary>Backup and restore, a port of the backup/restore code in <c>src/main.mjs</c>.</summary>
public static class Backup
{
    public static string BuildBackup(LibraryStore store)
    {
        var data = (JsonObject)store.Data.DeepClone();
        data.Remove("cookie");
        data.Remove("session");
        data.Remove("windowBounds");
        data.Remove("maximized");
        data.Remove("downloads");
        var root = new JsonObject
        {
            ["app"] = LegacyUpgrade.BackupMarker,
            ["version"] = 1,
            ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        foreach (var kv in data) root[kv.Key] = kv.Value?.DeepClone();
        return root.ToJsonString();
    }

    /// <summary>Restores a backup into the store, accepting the old marker too.</summary>
    public static void Restore(LibraryStore store, string backupJson)
    {
        var data = JsonNode.Parse(backupJson) as JsonObject ?? throw new InvalidOperationException("Not a Soncle backup file");
        var marker = data["app"]?.GetValue<string>();
        if (!LegacyUpgrade.IsBackupMarker(marker)) throw new InvalidOperationException("Not a Soncle backup file");
        LegacyUpgrade.Apply(data["settings"] as JsonObject);
        foreach (var k in new[] { "liked", "playlists", "savedAlbums", "savedPlaylists", "followedArtists", "history", "searchHistory", "eqPresets" })
            if (data[k] is JsonArray) store.Set(k, data[k]);
        foreach (var k in new[] { "lyricsOffsets", "deviceProfiles" })
            if (data[k] is JsonObject) store.Set(k, data[k]);
        if (data["settings"] is JsonObject rawSettings)
        {
            var merged = Defaults.SettingsDeepClone();
            foreach (var kv in rawSettings) merged[kv.Key] = kv.Value?.DeepClone();
            var eq = Defaults.EqDeepClone();
            if (rawSettings["eq"] is JsonObject rawEq)
                foreach (var kv in rawEq) eq[kv.Key] = kv.Value?.DeepClone();
            merged["eq"] = eq;
            store.Set("settings", merged);
        }
    }
}
