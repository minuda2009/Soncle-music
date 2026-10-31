// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Android.Views;

namespace Soncle.Droid;

[global::Android.App.Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden,
    Exported = true
)]
public sealed class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        // `adb shell am start -n <activity> --es soncle.demo play` starts the test sound without a
        // tap, so CI's emulator can exercise the audio path (Engine -> OutputPump -> AudioTrack).
        if (Intent?.GetStringExtra(LaunchOptions.DemoExtra) == "play")
        {
            LaunchOptions.AutoPlay = true;
        }

        base.OnCreate(savedInstanceState);
    }
}

/// <summary>What the launching intent asked for, read once by <see cref="MainPage"/>.</summary>
internal static class LaunchOptions
{
    public const string DemoExtra = "soncle.demo";

    /// <summary>Set by <c>--es soncle.demo play</c>; cleared when the page consumes it.</summary>
    public static bool AutoPlay { get; set; }
}
