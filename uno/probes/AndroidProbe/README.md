# Android viability probe (docs/DESIGN.md §7.2)

The smallest Soncle-shaped Uno app: Material theme + Toolkit, a 500-row virtualised list,
the player bar and the four-destination bottom bar. It exists only to measure whether Uno on
Android fits the budget in `docs/WINUI_PLAN.md` §9. It is **not** part of `Soncle.sln` and
CI does not build it.

## Build (Linux)

```bash
dotnet workload install android
dotnet new install Uno.Templates   # only needed to regenerate
cd uno/probes/AndroidProbe
dotnet build Soncle.AndroidProbe/Soncle.AndroidProbe.csproj -t:InstallAndroidDependencies \
  -f net10.0-android -p:AndroidSdkDirectory=$HOME/android-sdk -p:AcceptAndroidSDKLicenses=True
dotnet publish Soncle.AndroidProbe/Soncle.AndroidProbe.csproj -f net10.0-android -c Release \
  -r android-arm64 -p:AndroidPackageFormat=apk -p:AndroidLinkTool=r8 \
  -p:AndroidSdkDirectory=$HOME/android-sdk -o out
```

## Measure on a phone

```bash
adb install -r out/com.minuda2009.soncle.unoprobe-Signed.apk
adb shell am force-stop com.minuda2009.soncle.unoprobe
adb shell monkey -p com.minuda2009.soncle.unoprobe -c android.intent.category.LAUNCHER 1   # or tap the icon
adb logcat -d -s SonclePROBE          # first-frame-ms=… (process start → first Uno frame)
adb shell dumpsys meminfo com.minuda2009.soncle.unoprobe | grep TOTAL
```

The first-frame time is also shown in the player bar. Do it 5 times after a reboot-free cold
start (`force-stop` first) and take the median; compare with the Capacitor app on the same phone.

## Results (Uno.Sdk 6.7.30, .NET 10, arm64, release)

| Variant | APK |
| --- | --- |
| default (Uno release: AOT) | 29.1 MB |
| default + R8 (`AndroidLinkTool=r8`) | 27.4 MB |
| no AOT (`RunAOTCompilation=false`) | 26.5 MB |
| Capacitor app today (plan §9) | 5.8 MB |

Biggest pieces: assembly store 11.1 MB, `classes.dex` 9.4 MB (before R8), `libSkiaSharp.so`
9.0 MB, `libmonosgen` 3.1 MB, HarfBuzz 2.8 MB, the Fluent icon font 0.8 MB (unused with Material).
Start-up and memory: **not measured yet** (needs a real 4 GB-class phone).
