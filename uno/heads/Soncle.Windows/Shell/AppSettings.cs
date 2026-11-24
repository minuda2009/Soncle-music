// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text.Json.Nodes;

namespace Soncle.Windows.Shell;

/// <summary>
/// The preview's few remembered choices (the first-run notice, the volume), kept in its own folder
/// (%LOCALAPPDATA%\Soncle Preview\settings.json), never in the shipping app's data. A missing or
/// damaged file means the defaults.
/// </summary>
internal sealed class AppSettings
{
    public bool NoticeSeen { get; set; }

    public double Volume { get; set; } = 1;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soncle Preview", "settings.json");

    public static AppSettings Load()
    {
        var s = new AppSettings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject o) return s;
            if (o["noticeSeen"] is JsonValue n && n.TryGetValue<bool>(out var seen)) s.NoticeSeen = seen;
            if (o["volume"] is JsonValue v && v.TryGetValue<double>(out var vol) && double.IsFinite(vol)) s.Volume = Math.Clamp(vol, 0, 1);
        }
        catch (Exception e)
        {
            Playback.PlaybackHost.Log($"settings-load-failed {e.GetType().Name}");
        }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var o = new JsonObject
            {
                ["noticeSeen"] = NoticeSeen,
                ["volume"] = Math.Round(Volume, 3),
            };
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, o.ToJsonString());
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e)
        {
            Playback.PlaybackHost.Log($"settings-save-failed {e.GetType().Name}");
        }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"notice={NoticeSeen} volume={Volume}");
}
