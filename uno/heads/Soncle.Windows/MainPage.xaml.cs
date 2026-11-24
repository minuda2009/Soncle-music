// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Windows.Web;

namespace Soncle.Windows;

/// <summary>
/// The shell: one WebView2 filling the window, showing Soncle's web UI (built by
/// <c>mobile/build.mjs --desktop</c> into <c>web/</c> next to the exe). <see cref="WebShell"/> serves it,
/// relays its requests and does the window work. Native parts (audio, devices, notifications)
/// move in from here one at a time.
/// </summary>
public sealed partial class MainPage : Page
{
    private WebShell? _shell;
    private bool _started;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;
        if ((Application.Current as App)?.MainWindow is not { } window) return;
        WebShell.UseCaption(window, DragSpot);
        try
        {
            _shell = new WebShell(Web, window, BotGuardHost);
            window.Closed += (_, _) => _shell?.Dispose();
            await _shell.StartAsync();
        }
        catch (Exception ex)
        {
            WebShell.Log($"start-failed {ex}");
            StartError.Text = "Soncle couldn't start its window. Make sure the Microsoft Edge WebView2 Runtime is installed (it comes with Windows 11), then open Soncle again.\n\nDetails: " + ex.Message;
            StartError.Visibility = Visibility.Visible;
        }
    }
}
