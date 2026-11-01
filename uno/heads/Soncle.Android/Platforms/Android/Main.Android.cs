// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Android.Runtime;

namespace Soncle.Droid;

/// <summary>
/// The Android process entry point: hands control to Uno with <see cref="App"/>. No LargeHeap: the
/// shell plus an 8 s decoded clip fits the normal heap, and memory is what this head measures.
/// </summary>
[global::Android.App.ApplicationAttribute(
    Label = "@string/ApplicationName",
    Icon = "@mipmap/icon",
    HardwareAccelerated = true,
    Theme = "@style/Theme.App.Starting"
)]
public sealed class SoncleApplication : Microsoft.UI.Xaml.NativeApplication
{
    public SoncleApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(() => new App(), javaReference, transfer)
    {
    }
}
