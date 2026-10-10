// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Windows;

/// <summary>
/// The shell: 48 px title bar, the four destinations in a rail, the player bar
/// (docs/DESIGN.md §3.1, §4.1). Pages arrive with Soncle.App.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App { MainWindow: { } window })
        {
            WindowChrome.UseTitleBar(window, TitleBar);
        }
    }

    private void OnDestinationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (DestinationTitle is not null && args.SelectedItem is NavigationViewItem { Content: string name })
        {
            DestinationTitle.Text = name;
        }
    }
}
