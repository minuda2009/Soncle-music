// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
    /// height (docs/WINUI_PLAN.md §2.5). <paramref name="titleBar"/> is the 48 px grid in the shell.
    /// </summary>
    public static void UseTitleBar(Window window, UIElement titleBar)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
    }

    /// <summary>
    /// The media session exists and is honest: it reports Soncle with nothing playing. Transport
    /// wiring arrives with Soncle.App and the shared PlaybackController, so nothing is faked here.
    /// </summary>
    public static void UseMediaSession(Window window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        var controls = SystemMediaTransportControlsInterop.GetForWindow(handle);
        controls.IsEnabled = true;
        controls.PlaybackStatus = MediaPlaybackStatus.Closed;
    }
}
