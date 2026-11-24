// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Media;
using WinRT.Interop;

namespace Soncle.Windows;

/// <summary>
/// The little bit of Windows the shell needs: Mica, the 48 px custom title bar and an honest
/// media session. The rest of the Windows services arrive with their own work
/// (docs/WINUI_PLAN.md §7), so this stays small on purpose.
/// </summary>
internal static class WindowChrome
{
    /// <summary>Mica under the Material surfaces (docs/WINUI_PLAN.md §2.5); solid when unsupported.</summary>
    public static void UseMica(Window window)
    {
        if (MicaController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop();
        }
    }

    /// <summary>
    /// The custom title bar: extend the content into the caption row and use the 48 px "Tall"
    /// height (docs/WINUI_PLAN.md §2.5). <paramref name="titleBar"/> is the 48 px grid in the shell
    /// and drags the window; each of <paramref name="interactive"/> (the back button, the search
    /// box) is cut out of the drag region as a pass-through rectangle, recomputed whenever it moves
    /// or the scale changes.
    /// </summary>
    public static void UseTitleBar(Window window, FrameworkElement titleBar, params FrameworkElement[] interactive)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        void Update() => SetPassthrough(window, interactive);
        titleBar.SizeChanged += (_, _) => Update();
        foreach (var e in interactive)
        {
            e.SizeChanged += (_, _) => Update();
            if (e.XamlRoot is { } root) root.Changed += (_, _) => Update();
        }
        Update();
    }

    /// <summary>The elements' bounds in window pixels, handed to the non-client input as pass-through.</summary>
    private static void SetPassthrough(Window window, FrameworkElement[] elements)
    {
        try
        {
            var rects = new List<RectInt32>();
            foreach (var element in elements)
            {
                if (element.XamlRoot is not { } root || element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
                var scale = root.RasterizationScale;
                var b = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                rects.Add(new RectInt32(
                    (int)Math.Round(b.X * scale), (int)Math.Round(b.Y * scale),
                    (int)Math.Round(b.Width * scale), (int)Math.Round(b.Height * scale)));
            }
            if (rects.Count == 0) return;
            InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id)
                .SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
        }
        catch (Exception e)
        {
            // never take the window down over the title bar: the box just stays a drag area
            Trace.WriteLine($"Soncle titlebar-passthrough-failed {e.GetType().Name}");
        }
    }

    /// <summary>Full screen on / off (F11); returns whether the window is now full screen.</summary>
    public static bool ToggleFullScreen(Window window)
    {
        try
        {
            var full = window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            window.AppWindow.SetPresenter(full ? AppWindowPresenterKind.Default : AppWindowPresenterKind.FullScreen);
            return !full;
        }
        catch (Exception e)
        {
            Trace.WriteLine($"Soncle fullscreen-failed {e.GetType().Name}");
            return false;
        }
    }

    /// <summary>Leaves full screen if the window is in it.</summary>
    public static void ExitFullScreen(Window window)
    {
        try
        {
            if (window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                window.AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        }
        catch (Exception e)
        {
            Trace.WriteLine($"Soncle fullscreen-failed {e.GetType().Name}");
        }
    }

    /// <summary>
    /// The media session exists and is honest: it reports Soncle with nothing playing until a song
    /// is chosen; <c>Playback/MediaSession.cs</c> then mirrors the player bar on the returned controls.
    /// </summary>
    public static SystemMediaTransportControls UseMediaSession(Window window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        var controls = SystemMediaTransportControlsInterop.GetForWindow(handle);
        controls.IsEnabled = true;
        controls.PlaybackStatus = MediaPlaybackStatus.Closed;
        return controls;
    }
}
