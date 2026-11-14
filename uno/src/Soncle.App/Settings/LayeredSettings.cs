// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json.Nodes;

namespace Soncle.App.Settings;

/// <summary>Which layer supplied a setting's effective value (so the UI can say so).</summary>
public enum SettingSource { Base, Device, Focus, Transient }

/// <summary>An effective value and the layer it came from.</summary>
public sealed record SettingValue(JsonNode? Value, SettingSource Source);

/// <summary>
/// Effective settings = base (the person) ⊕ device profile ⊕ focus overlay ⊕ transient, resolved
/// per key, top wins (<c>docs/DESIGN.md</c> §5.1). Replaces the JS app's snapshot/restore of
/// <c>focusBackup</c>: ending focus removes the overlay, so there is nothing to restore and nothing
/// to recover after a crash.
/// </summary>
public sealed class LayeredSettings
{
    // The keys focus mode changes (renderer/app.js: FOCUS_KEYS). Any other key is untouched while
    // focus is on.
    public static readonly IReadOnlyList<string> FocusKeys = new[]
    {
        "eq", "crossfade", "smartCrossfade", "autoplay", "autoSkipOnError", "skipSilence",
        "normalize", "crossfadeOnSkip", "smartDuck", "discord", "noDuplicates",
    };

    // The values focus mode sets them to (renderer/app.js: FOCUS_PROFILE).
    public static JsonObject DefaultFocusProfile() => new()
    {
        ["crossfade"] = 8, ["smartCrossfade"] = true, ["autoplay"] = true, ["autoSkipOnError"] = true,
        ["skipSilence"] = true, ["normalize"] = true, ["crossfadeOnSkip"] = true, ["smartDuck"] = "duck",
        ["discord"] = false, ["noDuplicates"] = true,
    };

    private readonly JsonObject _base;
    private readonly Dictionary<string, JsonObject> _profiles = new();
    private readonly Dictionary<string, JsonNode?> _transient = new();
    private JsonObject? _focusProfile;

    public LayeredSettings(JsonObject baseSettings) => _base = baseSettings;

    /// <summary>The device whose profile is active, or null. Its overrides sit under focus.</summary>
    public string? DeviceKey { get; private set; }

    public bool FocusActive => _focusProfile is not null;

    public void SetBase(string key, JsonNode? value) => _base[key] = value;

    public JsonNode? BaseValue(string key) => _base[key];

    /// <summary>Stores a device's override object (the same shape as <c>DB.deviceProfiles[key]</c>).</summary>
    public void SetDeviceProfile(string deviceKey, JsonObject? profile)
    {
        if (profile is null) _profiles.Remove(deviceKey);
        else _profiles[deviceKey] = profile;
    }

    public void SelectDevice(string? deviceKey) => DeviceKey = deviceKey;

    public void SetTransient(string key, JsonNode? value) => _transient[key] = value;

    public void ClearTransient(string key) => _transient.Remove(key);

    /// <summary>Starts (or replaces) the focus overlay. Only <see cref="FocusKeys"/> are overridden.</summary>
    public void BeginFocus(JsonObject? profile = null) => _focusProfile = profile ?? DefaultFocusProfile();

    /// <summary>Ends focus. Nothing to restore: the overlay is simply dropped.</summary>
    public void EndFocus() => _focusProfile = null;

    /// <summary>The effective value of a key and the layer it came from (top wins).</summary>
    public SettingValue Resolve(string key)
    {
        if (_transient.TryGetValue(key, out var t)) return new SettingValue(t, SettingSource.Transient);
        if (_focusProfile is not null && FocusKeys.Contains(key) && _focusProfile[key] is { } fv)
            return new SettingValue(fv, SettingSource.Focus);
        if (DeviceKey is not null && _profiles.TryGetValue(DeviceKey, out var p) && p[key] is { } dv)
            return new SettingValue(dv, SettingSource.Device);
        return new SettingValue(_base[key], SettingSource.Base);
    }

    public JsonNode? Get(string key) => Resolve(key).Value;

    /// <summary>Every key any layer knows about.</summary>
    public IEnumerable<string> Keys
    {
        get
        {
            var keys = new HashSet<string>(_base.Select(kv => kv.Key));
            if (DeviceKey is not null && _profiles.TryGetValue(DeviceKey, out var p))
                foreach (var kv in p) keys.Add(kv.Key);
            if (_focusProfile is not null)
                foreach (var k in FocusKeys) keys.Add(k);
            foreach (var k in _transient.Keys) keys.Add(k);
            return keys;
        }
    }

    /// <summary>The whole settings object with every layer applied (what gets persisted and handed to the audio engine).</summary>
    public JsonObject Effective()
    {
        var outv = (JsonObject)_base.DeepClone();
        foreach (var k in Keys) outv[k] = Get(k)?.DeepClone();
        return outv;
    }
}
