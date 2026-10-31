// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Soncle.App.ViewModels;

/// <summary>
/// A minimal <see cref="INotifyPropertyChanged"/> base (no MVVM package). PropertyChanged is raised
/// on the thread that changed the property; the view models only change state on the thread they
/// were created on (see each class), so a head creates them on its UI thread.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
