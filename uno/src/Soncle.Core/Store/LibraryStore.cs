// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json.Nodes;

namespace Soncle.Core.Store;

/// <summary>
/// Reads and writes <c>library.json</c> exactly as the desktop app does: same defaults, same merge
/// rules, same upgrades, and unknown keys survive a round trip. A port of the store section of
/// <c>src/main.mjs</c> with <c>src/defaults.mjs</c> and <c>src/legacy.mjs</c>.
/// </summary>
public sealed class LibraryStore
{
    private readonly string _path;
    private readonly ISecretProtector? _protector;

    public LibraryStore(string path, ISecretProtector? protector = null)
    {
        _path = path;
        _protector = protector;
    }

    /// <summary>The whole library, including keys the C# code doesn't know (extension data).</summary>
    public JsonObject Data { get; private set; } = new();

    /// <summary>The settings object (a child of <see cref="Data"/>).</summary>
    public JsonObject Settings => (JsonObject)Data["settings"]!;

    /// <summary>Loads (or creates) the library. A corrupted or partial file falls back to defaults.</summary>
    public void Load()
    {
        JsonObject raw;
        try
        {
            raw = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            raw = new JsonObject();
        }
        LoadFrom(raw);
    }

    /// <summary>Applies the load/merge/upgrade rules to an already-parsed object (used by tests).</summary>
    public void LoadFrom(JsonObject raw)
    {
        var rawSettings = raw["settings"] as JsonObject;
        LegacyUpgrade.Apply(rawSettings);

        var merged = Defaults.LibraryDeepClone();
        foreach (var kv in raw) merged[kv.Key] = kv.Value?.DeepClone();
        var settings = Defaults.SettingsDeepClone();
        if (rawSettings is not null)
            foreach (var kv in rawSettings) settings[kv.Key] = kv.Value?.DeepClone();
        // eq is merged one level deep
        var eq = Defaults.EqDeepClone();
        if (rawSettings?["eq"] is JsonObject rawEq)
            foreach (var kv in rawEq) eq[kv.Key] = kv.Value?.DeepClone();
        settings["eq"] = eq;
        merged["settings"] = settings;

        // Existing installs (an earlier version) skip the full-screen welcome.
        if (rawSettings is not null && !rawSettings.ContainsKey("welcomed")) settings["welcomed"] = true;

        Data = merged;
    }

    /// <summary>Everything except the account secret, for the UI.</summary>
    public JsonObject GetForUi()
    {
        var copy = (JsonObject)Data.DeepClone();
        copy.Remove("cookie");
        copy.Remove("downloads");
        return copy;
    }

    /// <summary>Sets a top-level key, refusing <c>cookie</c> and <c>downloads</c> like <c>store:set</c>.</summary>
    public bool Set(string key, JsonNode? value)
    {
        if (key is "cookie" or "downloads") return false;
        Data[key] = value?.DeepClone();
        return true;
    }

    /// <summary>The in-memory cookie, decrypted. Never written in plain text and never returned by <see cref="GetForUi"/>.</summary>
    public string Cookie
    {
        get
        {
            var stored = Data["cookie"]?.GetValue<string>();
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            return _protector is null ? stored : _protector.Unprotect(stored);
        }
        set
        {
            if (string.IsNullOrEmpty(value)) Data["cookie"] = null;
            else Data["cookie"] = _protector is null ? value : _protector.Protect(value);
        }
    }

    /// <summary>Serialises exactly what goes on disk (atomic <c>.tmp</c> + rename, cookie encrypted).</summary>
    public void SaveNow()
    {
        var onDisk = (JsonObject)Data.DeepClone();
        if (!string.IsNullOrEmpty(Cookie))
            onDisk["cookie"] = _protector is null ? Cookie : _protector.Protect(Cookie);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, onDisk.ToJsonString());
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>Protects a secret at rest; the Windows head uses DPAPI, Android the Keystore.</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string stored);
}
