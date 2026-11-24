// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Uno.Resizetizer;
using Windows.Media;

namespace Soncle.Windows;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>The name the preview runs under (docs/WINUI_PLAN.md §10), shown by Windows.</summary>
    private const string WindowTitle = "Soncle Preview";

    /// <summary>The shell window. Exposed so the shell page can set its custom title bar.</summary>
    public Window? MainWindow { get; private set; }

    /// <summary>The window's media session (SMTC); null if Windows refused it.</summary>
    public SystemMediaTransportControls? MediaControls { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The window title is what the taskbar, Alt+Tab and Task Manager show; the custom title bar
        // in MainPage draws the same string. Unpackaged, it otherwise comes from the exe, so set it
        // to the name the preview installs/runs under (docs/WINUI_PLAN.md §10).
        MainWindow = new Window { Title = WindowTitle };
        // The web UI drives Windows' media controls through navigator.mediaSession in WebView2;
        // the native media session (WindowChrome.UseMediaSession) returns with native playback.

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        MainWindow.SetWindowIcon();
        Soncle.Windows.Web.WindowPlacement.Restore(MainWindow);
        MainWindow.Activate();
    }

    private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }
}
