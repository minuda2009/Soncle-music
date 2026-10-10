// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Soncle.Core.Store;
using Soncle.TestKit;

namespace Soncle.Core.Tests;

public class StoreTests
{
    private static JsonElement Fixture(string name) =>
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("store/" + name))).RootElement;

    private static JsonObject Obj(JsonElement e) => (JsonObject)JsonNode.Parse(e.GetRawText())!;

    [Fact]
    public void DefaultsMatchTheJs()
    {
        var expected = Fixture("defaults.json");
        // the generated defaults must equal src/defaults.mjs (structural compare, ignores whitespace)
        JsonAssert.Equal(expected.GetProperty("defaultSettings").GetRawText(), Defaults.SettingsDeepClone().ToJsonString());
        JsonAssert.Equal(expected.GetProperty("defaults").GetRawText(), Defaults.LibraryDeepClone().ToJsonString());
    }

    [Fact]
    public void MergeCasesMatchTheJs()
    {
        foreach (var c in Fixture("merge-cases.json").EnumerateArray())
        {
            var store = new LibraryStore("/tmp/does-not-matter.json");
            store.LoadFrom(Obj(c.GetProperty("raw")));
            JsonAssert.Equal(
                c.GetProperty("expected").GetProperty("eq").GetRawText(),
                ((JsonObject)store.Settings["eq"]!).ToJsonString());
            // and every default key is present
            foreach (var p in c.GetProperty("expected").EnumerateObject())
                Assert.True(store.Settings.ContainsKey(p.Name) || store.Data.ContainsKey(p.Name), $"missing {p.Name}");
        }
    }

    [Fact]
    public void LegacyCrossfadeCurveIsUpgraded()
    {
        var store = new LibraryStore("/tmp/x.json");
        store.LoadFrom(new JsonObject { ["settings"] = new JsonObject { ["xfCurve"] = "metrolist" } });
        Assert.Equal("smooth", store.Settings["xfCurve"]!.GetValue<string>());
    }

    [Fact]
    public void ExistingInstallsSkipTheWelcome()
    {
        var store = new LibraryStore("/tmp/x.json");
        store.LoadFrom(new JsonObject { ["settings"] = new JsonObject { ["volume"] = 0.5 } });
        Assert.True(store.Settings["welcomed"]!.GetValue<bool>());

        // a file that already set welcomed keeps its value
        var store2 = new LibraryStore("/tmp/x.json");
        store2.LoadFrom(new JsonObject { ["settings"] = new JsonObject { ["welcomed"] = false } });
        Assert.False(store2.Settings["welcomed"]!.GetValue<bool>());
    }

    [Fact]
    public void UnknownKeysSurviveARoundTrip()
    {
        var store = new LibraryStore("/tmp/x.json");
        store.LoadFrom(new JsonObject
        {
            ["settings"] = new JsonObject { ["futureKey"] = 42 },
            ["extraTop"] = new JsonObject { ["x"] = 1 },
        });
        Assert.Equal(42, store.Settings["futureKey"]!.GetValue<int>());
        Assert.Equal(1, ((JsonObject)store.Data["extraTop"]!)["x"]!.GetValue<int>());
    }

    [Fact]
    public void CorruptOrPartialFileFallsBackToDefaults()
    {
        var store = new LibraryStore("/tmp/x.json");
        store.LoadFrom(new JsonObject());
        Assert.Equal(0.8, store.Settings["volume"]!.GetValue<double>());
        Assert.True(store.Settings.ContainsKey("eq"));
    }

    [Fact]
    public void GetForUiNeverContainsTheCookie()
    {
        var store = new LibraryStore("/tmp/x.json");
        store.LoadFrom(new JsonObject());
        store.Cookie = "SAPISID=secret";
        Assert.DoesNotContain("cookie", store.GetForUi().Select(p => p.Key));
        Assert.DoesNotContain("SAPISID", store.GetForUi().ToJsonString());
    }

    [Fact]
    public void SaveRefusesCookieAndDownloads()
    {
        var store = new LibraryStore("/tmp/x.json");
        store.LoadFrom(new JsonObject());
        Assert.False(store.Set("cookie", "x"));
        Assert.False(store.Set("downloads", new JsonObject()));
        Assert.True(store.Set("liked", new JsonArray()));
    }

    [Fact]
    public void LoadSaveRoundTripsTheSampleLibrary()
    {
        var dir = Path.Combine(Path.GetTempPath(), "soncle-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "library.json");
            File.Copy(Fixtures.Path("store/library.sample.json"), path);
            var store = new LibraryStore(path);
            store.Load();
            Assert.Equal("WH-1000XM4", ((JsonObject)store.Data["deviceProfiles"]!).First().Key);
            store.SaveNow();
            // reload and compare structurally
            var again = new LibraryStore(path);
            again.Load();
            JsonAssert.Equal(store.Data.ToJsonString(), again.Data.ToJsonString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
