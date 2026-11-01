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
    /// and drags the window; <paramref name="interactive"/> (the search box) is cut out of the drag
    /// region as a pass-through rectangle, recomputed whenever it moves or the scale changes.
    /// </summary>
    public static void UseTitleBar(Window window, FrameworkElement titleBar, FrameworkElement interactive)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        void Update() => SetPassthrough(window, interactive);
        titleBar.SizeChanged += (_, _) => Update();
        interactive.SizeChanged += (_, _) => Update();
        if (interactive.XamlRoot is { } root) root.Changed += (_, _) => Update();
        Update();
    }

    /// <summary>The element's bounds in window pixels, handed to the non-client input as pass-through.</summary>
    private static void SetPassthrough(Window window, FrameworkElement element)
    {
        try
        {
            if (element.XamlRoot is not { } root || element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
            var scale = root.RasterizationScale;
            var b = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            var rect = new RectInt32(
                (int)Math.Round(b.X * scale), (int)Math.Round(b.Y * scale),
                (int)Math.Round(b.Width * scale), (int)Math.Round(b.Height * scale));
            if (rect.Equals(_passthrough)) return;
            _passthrough = rect;
            InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id)
                .SetRegionRects(NonClientRegionKind.Passthrough, new[] { rect });
        }
        catch (Exception e)
        {
            // never take the window down over the title bar: the box just stays a drag area
            Trace.WriteLine($"Soncle titlebar-passthrough-failed {e.GetType().Name}");
        }
    }

    private static RectInt32 _passthrough;

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
