// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soncle.Audio;

namespace Soncle.App;

/// <summary>
/// The user's sound settings, read from <c>library.json</c> (the same shape the JS apps write) and
/// applied to the shared <see cref="Engine"/>. A port of the <c>engine.setSound</c> /
/// <c>engine.setEq</c> wiring in <c>renderer/app.js</c>, so a song sounds the same in the C# app as
/// in the JS apps: normalisation target, preamp, the 10-band EQ, auto headroom, loudness
/// compensation, stereo width, crossfeed and mono/balance.
/// </summary>
public sealed record SoundSettings
{
    /// <summary>The loudness target in LUFS (JS <c>normTarget</c>; default -14).</summary>
    public double NormTarget { get; init; } = -14;
    public bool Normalize { get; init; } = true;

    // EQ
    public bool EqEnabled { get; init; }
    public double PreampDb { get; init; }
    public double[] EqGains { get; init; } = new double[10];

    // sound
    public bool AutoHeadroom { get; init; } = true;
    public bool LoudnessComp { get; init; }
    public double StereoWidth { get; init; } = 1;
    public string Crossfeed { get; init; } = "off";
    public bool Mono { get; init; }
    public double Balance { get; init; }

    /// <summary>Reads the settings out of a <c>library.json</c> "settings" object, using the JS defaults.</summary>
    public static SoundSettings FromJson(JsonNode? settings)
    {
        if (settings is not JsonObject s) return new SoundSettings();

        var eq = s["eq"] as JsonObject;
        var gains = new double[10];
        if (eq?["gains"] is JsonArray arr)
            for (var i = 0; i < gains.Length && i < arr.Count; i++)
                gains[i] = Num(arr[i]) ?? 0;

        return new SoundSettings
        {
            NormTarget = Num(s["normTarget"]) ?? -14,
            Normalize = Bool(s["normalize"]) ?? true,
            EqEnabled = Bool(eq?["enabled"]) ?? false,
            PreampDb = Num(eq?["preamp"]) ?? 0,
            EqGains = gains,
            AutoHeadroom = Bool(s["autoHeadroom"]) ?? true,
            LoudnessComp = Bool(s["loudnessComp"]) ?? false,
            StereoWidth = Num(s["stereoWidth"]) ?? 1,
            Crossfeed = Str(s["crossfeed"]) ?? "off",
            Mono = Bool(s["mono"]) ?? false,
            Balance = Num(s["balance"]) ?? 0,
        };
    }

    /// <summary>Reads the settings from a library file's raw text; anything unreadable gives the defaults.</summary>
    public static SoundSettings FromLibraryJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SoundSettings();
        try
        {
            return FromJson(JsonNode.Parse(json)?["settings"]);
        }
        catch
        {
            return new SoundSettings();
        }
    }

    private static double? Num(JsonNode? n)
    {
        if (n is null) return null;
        try
        {
            return n.GetValueKind() switch
            {
                JsonValueKind.Number => n.GetValue<double>(),
                JsonValueKind.String when double.TryParse(n.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
                _ => null,
            };
        }
        catch { return null; }
    }

    private static bool? Bool(JsonNode? n)
    {
        if (n is null) return null;
        try
        {
            return n.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
        catch { return null; }
    }

    private static string? Str(JsonNode? n)
    {
        if (n is null) return null;
        try { return n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null; }
        catch { return null; }
    }

    /// <summary>Applies these settings to an engine (the shared master chain).</summary>
    public void ApplyTo(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        engine.SetNormalize(Normalize, NormTarget);
        var master = engine.Master;
        master.SetEq(EqEnabled, EqGains);
        master.Options.PreampDb = PreampDb;
        master.ConfigureEq();
        master.SetSound(AutoHeadroom, LoudnessComp, StereoWidth, Crossfeed);
        master.SetChannel(Mono, Balance);
    }
}
