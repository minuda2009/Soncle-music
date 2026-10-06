// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Soncle.Core.Store;
using Soncle.Services;

namespace Soncle.Services.Tests;

/// <summary>The JS <c>test/legacy.test.mjs</c> cases, ported, plus backup/restore.</summary>
public class CarryOverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "carry-" + Guid.NewGuid().ToString("N"));

    public CarryOverTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void UserDataIsCopiedNotMovedExactlyOnce()
    {
        var oldDir = Path.Combine(_root, CarryOver.LegacyDirName);
        var newDir = Path.Combine(_root, "Soncle");
        Directory.CreateDirectory(Path.Combine(oldDir, "downloads"));
        File.WriteAllText(Path.Combine(oldDir, "downloads", "abc.webm"), "audio");
        File.WriteAllText(Path.Combine(oldDir, "flow-features.json"), "{\"x\":1}");
        File.WriteAllText(Path.Combine(oldDir, "Local State"), "{}");
        var lib = new JsonObject
        {
            ["liked"] = new JsonArray(new JsonObject { ["id"] = "a" }),
            ["settings"] = new JsonObject { ["xfCurve"] = "metrolist", ["volume"] = 0.5 },
            ["downloads"] = new JsonObject { ["abc"] = new JsonObject { ["file"] = Path.Combine(oldDir, "downloads", "abc.webm") } },
        };
        File.WriteAllText(Path.Combine(oldDir, "library.json"), lib.ToJsonString());

        var r = CarryOver.CarryOverData(_root, newDir)!;
        Assert.Equal(new[] { "library.json", "downloads", "flow-features.json", "Local State" }, r.Copied);
        var got = JsonNode.Parse(File.ReadAllText(Path.Combine(newDir, "library.json")))!;
        Assert.Equal("smooth", got["settings"]!["xfCurve"]!.GetValue<string>());
        Assert.Equal(Path.Combine(newDir, "downloads", "abc.webm"), got["downloads"]!["abc"]!["file"]!.GetValue<string>());
        Assert.Equal("audio", File.ReadAllText(Path.Combine(newDir, "downloads", "abc.webm")));
        // the old data is untouched
        Assert.Equal("metrolist", JsonNode.Parse(File.ReadAllText(Path.Combine(oldDir, "library.json")))!["settings"]!["xfCurve"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(oldDir, "downloads", "abc.webm")));

        // never twice
        File.WriteAllText(Path.Combine(newDir, "library.json"), "{\"liked\":[]}");
        Assert.Null(CarryOver.CarryOverData(_root, newDir));
        Assert.Equal("{\"liked\":[]}", File.ReadAllText(Path.Combine(newDir, "library.json")));
    }

    [Fact]
    public void FreshInstallsJustMarkItDone()
    {
        Assert.Empty(CarryOver.CarryOverData(_root, Path.Combine(_root, "Soncle"))!.Copied);
        Assert.Null(CarryOver.CarryOverData(_root, Path.Combine(_root, "Soncle")));
    }

    [Fact]
    public void ElectronSoncleFolderIsAlsoAcceptedAsASource()
    {
        var oldDir = Path.Combine(_root, "Soncle");   // the Electron app's folder
        var newDir = Path.Combine(_root, "SoncleWinUI");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "library.json"), "{\"liked\":[{\"id\":\"b\"}]}");
        var r = CarryOver.CarryOverData(_root, newDir)!;
        Assert.Contains("library.json", r.Copied);
        Assert.Equal("b", JsonNode.Parse(File.ReadAllText(Path.Combine(newDir, "library.json")))!["liked"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void OldAndNewBackupsAreBothAccepted()
    {
        Assert.True(LegacyUpgrade.IsBackupMarker(LegacyUpgrade.BackupMarker));
        Assert.True(LegacyUpgrade.IsBackupMarker("metrolist-desktop"));
        Assert.False(LegacyUpgrade.IsBackupMarker("something-else"));
        var settings = new JsonObject { ["xfCurve"] = "metrolist" };
        LegacyUpgrade.Apply(settings);
        Assert.Equal("smooth", settings["xfCurve"]!.GetValue<string>());
    }

    [Fact]
    public void BackupOmitsSecretsAndRestoreRoundTrips()
    {
        var store = new LibraryStore(Path.Combine(_root, "lib.json"));
        store.LoadFrom(new JsonObject
        {
            ["liked"] = new JsonArray(new JsonObject { ["id"] = "x" }),
            ["settings"] = new JsonObject { ["volume"] = 0.6 },
            ["cookie"] = "SAPISID=secret",
            ["downloads"] = new JsonObject(),
        });
        var json = Backup.BuildBackup(store);
        Assert.DoesNotContain("SAPISID", json);
        Assert.DoesNotContain("\"downloads\"", json);
        Assert.Contains("\"app\":\"soncle\"", json);

        var restored = new LibraryStore(Path.Combine(_root, "lib2.json"));
        restored.LoadFrom(new JsonObject());
        Backup.Restore(restored, json);
        Assert.Equal("x", ((JsonArray)restored.Data["liked"]!)[0]!["id"]!.GetValue<string>());
        Assert.Equal(0.6, restored.Settings["volume"]!.GetValue<double>());
    }

    [Fact]
    public void RestoreRejectsNonBackupFiles()
    {
        var store = new LibraryStore(Path.Combine(_root, "lib.json"));
        store.LoadFrom(new JsonObject());
        Assert.Throws<InvalidOperationException>(() => Backup.Restore(store, "{\"app\":\"other\"}"));
    }
}
