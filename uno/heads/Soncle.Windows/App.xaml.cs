// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Uno.Resizetizer;

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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The window title is what the taskbar, Alt+Tab and Task Manager show; the custom title bar
        // in MainPage draws the same string. Unpackaged, it otherwise comes from the exe, so set it
        // to the name the preview installs/runs under (docs/WINUI_PLAN.md §10).
        MainWindow = new Window { Title = WindowTitle };
        WindowChrome.UseMica(MainWindow);
        WindowChrome.UseMediaSession(MainWindow);

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
        MainWindow.Activate();
    }

    private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }
}
