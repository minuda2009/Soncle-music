# Soncle.Android: the Uno Android head (Soncle Uno)

The real Uno Platform Android head (`docs/WINUI_PLAN.md` §1, §11), the step after the viability
probe in `uno/probes/AndroidProbe`. It opens Soncle's adaptive shell and plays sound through the
shared C# audio engine. Like the Windows head it is **not in `uno/Soncle.sln`**: the ubuntu library
job has no Android workload, so `.github/workflows/uno-android-preview.yml` builds this csproj on
its own.

## What this head has

- the **adaptive shell** (`docs/DESIGN.md` §3.1): the four destinations **Home · Library · Sound ·
  Settings** in a Material bottom bar (`utu:TabBar`, `BottomTabBarStyle`) on a phone, and in a rail
  (`NavigationView`, `Left`) when the window is at least 600 effective px wide (tablet, landscape
  foldable, desktop mode). Both drive the same selection;
- the **player bar** with the `docs/DESIGN.md` §4.1 empty state: "Nothing playing" and a short
  explanation, never a blank square with `0:00 / 0:00`; once a song is picked it shows the title,
  the artists and the time, with play/pause, previous/next and a seek bar;
- a **search box** and a **results list**: type a song, press Enter, tap a result to play it with
  the rest of the results as the queue;
- **songs**: playback goes through the shared `Soncle.App.PlaybackSession` — search -> resolve
  (`Soncle.YouTube`) -> download + streaming decode (`Soncle.Streams` + `StreamingOpusSource`) ->
  `Soncle.Audio.Engine` (master chain, limiter last) -> `Soncle.Audio.Output.OutputPump` ->
  `AudioTrackOutput` (`Platforms/Android/AudioTrackOutput.cs`, the
  `Soncle.Core.Platform.IAudioOutput` seam on `AudioTrack`: float PCM, 48 kHz stereo, one dedicated
  thread, preallocated buffer, blocking writes). It asks for audio focus first and stops when
  another app takes it. Start ramps the first block up from silence and a stop ramps one last block
  down and lets the track play out what is queued (`Soncle.Audio.Output.GainRamp`), so pause and
  resume neither click nor skip; the render thread does that and releases the track and focus
  itself, so the UI thread never waits for the device. A device failure shows as an error in the
  player bar, not as a pause. Decoding runs off the UI thread. Only the output is Android code:
  `Playback/AndroidPlaybackHost.cs` supplies just the output and the cache folder, and the same
  wiring runs on Windows with a WASAPI output;
- the **start-up measurement** for the `docs/DESIGN.md` §7.2 gate: process start to first Uno frame,
  logged once and shown quietly on the Settings placeholder;
- a **demo launch** for CI: `--es soncle.demo play` searches for a fixed song and plays the first
  result without a tap, so the smoke test can prove real playback.

### Log lines (`adb logcat -s Soncle`)

| Line | When |
| --- | --- |
| `first-frame-ms=<n>` | first `CompositionTarget.Rendering` after process start |
| `session-ready from-server=<bool> clients=<list>` | the anonymous InnerTube session was created (first search) |
| `search-failed <error>` | a search threw |
| `play-failed <error>` | a song could not be resolved or played |
| `audio-started sr=<sr> buffer-frames=<n>` | the AudioTrack started (a song began to play) |
| `audio-stopped frames=<total> underruns=<n>` | pause, focus loss or shutdown (`AudioTrack.UnderrunCount`) |
| `audio-focus-denied`, `audio-focus-lost <kind>` | audio focus refused or taken by another app |
| `audio-write-failed code=<n>`, `audio-thread-failed <error>` | the device failed; the track and focus are released |

## What it doesn't have yet (known gaps, by scope)

- **media session, foreground service, lock screen and notification controls**: the
  `SoncleMediaService` Java binding is a later increment, so the sound may stop when Android
  reclaims a backgrounded app;
- the **BotGuard WebView** binding (PO tokens), and the `MainActivity.java` timers/autoplay port;
- **AAC** decode via `MediaCodec` (only Opus/WebM through Concentus today, so a song YouTube
  offers only as AAC says it can't be played);
- **PO tokens and deciphering**: only the two clients that need neither are used, so some songs
  may not resolve;
- **phone playback measurements** (CPU, memory, battery) and background playback that survives
  Android reclaiming the app;
- the **`Soncle.App` pages**: every destination shows one placeholder;
- **icons in the bottom bar and the rail**: items are text-only because the repo has no
  Material Symbols font yet (`Soncle.Design`, plan stage 3, brings it, outlined at weight 600);
- the palette is the Windows head's `Styles/ColorPaletteOverride.xaml` until `Soncle.Design`.

## App id and name

`com.minuda2009.soncle.uno`, shown as **Soncle Uno**. It installs side by side with the shipping
app (`com.minuda2009.soncle`) and the Capacitor preview (`com.minuda2009.soncle.preview`) and shares
no data with them. The minimum Android version is 7.0 (API 24), the same as the Capacitor app.

## Build (Linux)

```bash
dotnet workload install android
dotnet build uno/heads/Soncle.Android/Soncle.Android.csproj -t:InstallAndroidDependencies \
  -f net10.0-android -p:AndroidSdkDirectory=$HOME/android-sdk -p:AcceptAndroidSDKLicenses=True
SONCLE_TEST_KEY_PASS=soncle-test \
dotnet publish uno/heads/Soncle.Android/Soncle.Android.csproj -f net10.0-android -c Release \
  -r android-arm64 -p:AndroidPackageFormat=apk -p:AndroidLinkTool=r8 \
  -p:AndroidSdkDirectory=$HOME/android-sdk \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=$PWD/mobile/android/app/soncle-test.keystore \
  -p:AndroidSigningStorePass=env:SONCLE_TEST_KEY_PASS \
  -p:AndroidSigningKeyAlias=soncle \
  -p:AndroidSigningKeyPass=env:SONCLE_TEST_KEY_PASS \
  -o out
```

The test keystore is the public one the Capacitor preview already uses, so every preview APK
installs over the previous one. The output is `out/com.minuda2009.soncle.uno-Signed.apk`.

## Measure on a phone

```bash
adb install -r out/com.minuda2009.soncle.uno-Signed.apk
adb shell am force-stop com.minuda2009.soncle.uno
adb shell monkey -p com.minuda2009.soncle.uno -c android.intent.category.LAUNCHER 1   # or tap the icon
adb logcat -d -s Soncle                                           # first-frame-ms=...
adb shell dumpsys meminfo com.minuda2009.soncle.uno | grep TOTAL  # idle
# tap Test sound (or start with the demo extra, below), wait ~10 s, then:
adb shell dumpsys meminfo com.minuda2009.soncle.uno | grep TOTAL  # while the test sound plays
adb logcat -d -s Soncle                                           # audio-started / sample-decoded
```

Start with the test sound already playing (what CI does):

```bash
ACT=$(adb shell cmd package resolve-activity --brief com.minuda2009.soncle.uno | tail -n 1)
adb shell am start -n "$ACT" --es soncle.demo play
```

Do 5 cold starts (`force-stop` first) and take the median first-frame time; record idle and
playing memory and compare with the Capacitor app on the same phone (`docs/WINUI_PLAN.md` §9).
Pause the test sound to get the `audio-stopped ... underruns=<n>` line; it should be 0.

## Not measured yet

Nothing in this head has been built or run outside CI: the first build is the
`uno-android-preview` workflow's. Start-up, memory and underruns on a real phone come from the
preview APK.

## CI (`.github/workflows/uno-android-preview.yml`)

Every push that touches `uno/**` runs three jobs:

- **apk**: publishes the arm64 Release APK with R8, signed with the test key above, and fails if the
  package is not `com.minuda2009.soncle.uno`, the label is not `Soncle Uno`, the APK carries any ABI
  other than `arm64-v8a`, or `apksigner verify` fails. The job summary shows the size against the
  30 MB budget (a warning when over), the versionCode and the ten largest entries;
- **smoke**: publishes an x86_64 build and runs `ci/smoke.sh` in an Android emulator (API 34,
  `google_apis`, KVM, audio on): cold start, `first-frame-ms=` within 120 s, process alive,
  screenshot and idle memory; then a launch with `--es soncle.demo play`, `audio-started` within
  60 s, ~10 s of the test sound, memory and a second screenshot. It fails on a crash, a missing
  first frame or a missing `audio-started`. The numbers in its summary are **emulator numbers**, not
  a phone. The `soncle-uno-smoke` artifact holds the screenshots, the `Soncle` log lines and the
  crash buffer for this package only;
- **release**: after both pass, replaces the `uno-android-preview` prerelease with
  `Soncle-uno-preview-arm64.apk`.

The workflow runs every `dotnet` command from `uno/` so `uno/global.json` selects the SDK the
android workload is installed for; do the same locally if you have several SDKs.
