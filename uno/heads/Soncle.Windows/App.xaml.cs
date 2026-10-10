// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Uno.Resizetizer;

namespace Soncle.Windows;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>The shell window. Exposed so the shell page can set its custom title bar.</summary>
    public Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();
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
