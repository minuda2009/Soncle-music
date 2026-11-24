// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json.Nodes;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Soncle.Windows.Web;

/// <summary>
/// The window opens where and as big as it was left (maximised too), like the desktop app
/// (<c>store.windowBounds</c>); a first start opens at the desktop app's 1320 × 840. Kept in
/// %LOCALAPPDATA%\Soncle Preview\window.json; a size that no longer fits a screen is ignored.
/// </summary>
internal static class WindowPlacement
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soncle Preview", "window.json");

    public static void Restore(Window window)
    {
        try
        {
            var aw = window.AppWindow;
            RectInt32? rect = null;
            var maximized = false;
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                int I(string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
                var r = new RectInt32(I("x"), I("y"), I("w"), I("h"));
                maximized = o["maximized"] is JsonValue m && m.TryGetValue<bool>(out var b) && b;
                if (r.Width >= 500 && r.Height >= 400 && Fits(r)) rect = r;
            }
            if (rect is { } fit) aw.MoveAndResize(fit);
            else
            {
                var area = DisplayArea.GetFromWindowId(aw.Id, DisplayAreaFallback.Primary).WorkArea;
                var w = Math.Min(1320, area.Width);
                var h = Math.Min(840, area.Height);
                aw.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
            }
            if (maximized && aw.Presenter is OverlappedPresenter p) p.Maximize();
        }
        catch (Exception e)
        {
            WebShell.Log($"window-restore-failed {e.GetType().Name}");
        }
    }

    public static void Save(Window window)
    {
        try
        {
            var aw = window.AppWindow;
            if (aw.Presenter is not OverlappedPresenter p || p.State == OverlappedPresenterState.Minimized) return;
            var o = File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject old ? old : new JsonObject();
            o["maximized"] = p.State == OverlappedPresenterState.Maximized;
            if (p.State == OverlappedPresenterState.Restored)
            {
                o["x"] = aw.Position.X;
                o["y"] = aw.Position.Y;
                o["w"] = aw.Size.Width;
                o["h"] = aw.Size.Height;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, o.ToJsonString());
        }
        catch (Exception e)
        {
            WebShell.Log($"window-save-failed {e.GetType().Name}");
        }
    }

    /// <summary>At least the top-left 100 px of the window is on some screen.</summary>
    private static bool Fits(RectInt32 r)
    {
        var probe = new RectInt32(r.X, r.Y, 100, 100);
        var area = DisplayArea.GetFromRect(probe, DisplayAreaFallback.None);
        return area is not null;
    }
}
