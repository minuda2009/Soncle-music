// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Droid;

/// <summary>
/// The docs/DESIGN.md §7.2 start-up gate: process start to the first Uno frame, the same clock the
/// probe used (uno/probes/AndroidProbe). <c>adb logcat -s Soncle</c> shows <c>first-frame-ms=…</c>.
/// </summary>
internal static class StartupClock
{
    public const string LogTag = "Soncle";

    /// <summary>The value logged on the first frame, or null before it.</summary>
    public static long? FirstFrameMs { get; private set; }

    public static long MillisecondsSinceProcessStart() =>
        global::Android.OS.SystemClock.ElapsedRealtime() - global::Android.OS.Process.StartElapsedRealtime;

    /// <summary>Records and logs the first-frame time; later calls are ignored.</summary>
    public static long ReportFirstFrame()
    {
        if (FirstFrameMs is { } done) return done;
        var ms = MillisecondsSinceProcessStart();
        FirstFrameMs = ms;
        global::Android.Util.Log.Info(LogTag, $"first-frame-ms={ms}");
        return ms;
    }
}
