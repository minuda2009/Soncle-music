# Soncle on WinUI — migration plan

_Drafted 4 Oct 2026; updated the same day against `main` @ ad98fa9 (desktop 1.0.0, Android 0.1.6). Extends the "Later:
move to Uno Platform" section of ROADMAP.md; where they differ, this file is the detailed plan._

**Goal.** Replace the Electron app with a native WinUI 3 app (Uno Platform, C#) that **looks like
Soncle does today** — Material 3, tonal surfaces, dynamic colour from the artwork, Material Symbols
at weight 600, the spring motion — keeps **every 1.0 feature**, and is noticeably more polished:
sample-accurate audio, GPU-smooth motion, real Windows integration, and a fraction of the memory.

**Ground rules**
1. **Material 3 is Soncle's look; WinUI is the engine.** We don't adopt Fluent controls visually.
   Windows-native things (Mica, title bar, media flyout, tray, taskbar) are used *under* the
   Material design, not instead of it.
2. **Parity gate.** Electron stays the shipping app until every row of the parity checklist (§4)
   is green and minuda2009 has used the WinUI build day-to-day.
3. **Measured, not guessed.** Electron baselines are measured first (§9) and every stage reports
   against them.
4. **UI changes are shown before they ship.** Side-by-side screenshots (Electron vs WinUI) for
   every page; anything that looks different from what minuda2009 liked is a bug unless agreed.
   (Lesson from 1.8: thin icons, filled controls and a two-line title made it worse.)
5. Name, attribution, licence, security and honesty rules stay exactly as they are today.

---

## 1. Target architecture

```
Soncle.sln
├─ src/Soncle.Core        models (Track, Album…), settings + library store (same JSON shape), contracts
├─ src/Soncle.YouTube     port of yt.mjs: InnerTube, normalisation, stream clients, PO tokens
├─ src/Soncle.Streams     whole-song downloader + cache for both heads (replaces mstream / SoncleStreams)
├─ src/Soncle.Audio       decode → two decks → master chain → limiter → output (WASAPI / AudioTrack)
├─ src/Soncle.Flow        port of renderer/flow: WebM cues, sampler, tempo/key/energy, planFlow
├─ src/Soncle.Services    lyrics (LRCLIB → YTM), AutoEq, Spotify/CSV import, local files, downloads,
│                         backup/restore, legacy carry-over, Discord
├─ src/Soncle.Design      Material 3 tokens, dynamic colour, styles, shared controls, motion
├─ src/Soncle.App         Uno shared UI: pages, view models (MVVM), navigation
├─ heads/Soncle.Windows   WinUI 3 / Windows App SDK head (+ Windows-only services)
├─ heads/Soncle.Android   .NET for Android head (later; ROADMAP stage 3)
└─ tests/…                xUnit per library + UI runtime tests + golden screenshots
```

- **MVVM:** CommunityToolkit.Mvvm (source generators, no reflection, trim/AOT-friendly).
- **The `window.api` contract becomes interfaces** in `Soncle.Core`, grouped as today:
  `IMusicCatalog` (home, explore, browse, search, album, artist, playlist, upNext, radio, related,
  library, songInfo, rate), `IStreams`, `IAccount` (signIn, signOut, authStatus, premiumStatus),
  `IStore`, `IDownloads`, `ILyrics`, `IHeadphones` (hpSearch/Match/Profile), `IFlowCache`,
  `ILocalLibrary`, `IImport`, `ISystem` (power, bt devices, ducking, open external, info, licences),
  `IWindowShell` (mini, fullscreen, titlebar colour, tray). Events (`onDownload`, `onDuck`,
  `onMediaAction`, …) become C# events / `IObservable`.
- **One stream downloader for both apps (`Soncle.Streams`).** `mstream://` and Android's
  `SoncleStreams.java` both exist only so a browser audio element can read googlevideo. In C# the
  decoder reads from our own downloader, which takes the **Android 0.1.4–0.1.6 design** as the
  reference, because it is the more advanced one:
  - each song downloads in full to a cache file as fast as the connection allows, and playback
    reads from that file; a seek far past what has arrived goes to the network directly;
  - bytes are passed on as they arrive (small 256 KB first piece, then ≤ 1 MB requests, kept-alive
    connections), so songs start after a few KB;
  - a lost connection is retried with back-off for about 10 minutes; what is stored keeps playing;
  - an expired URL is refreshed **for the same file** (same format and length), so a song can never
    be mixed with another stream; a 403 switches client as in `streamproxy.mjs`;
  - the next song is fetched ahead once the current one is complete, so it never competes with it.
  Desktop gets all of this for free (it doesn't have it today).
- **Android native code is kept, not rewritten.** `SoncleMediaService.java` (MediaBrowserService:
  Google Maps, Android Auto, lock screen, headsets, watches, foreground service) and the BotGuard
  WebView in `SonclePlugin.java` move into the Android head as a Java library with a .NET binding.
  `SoncleStreams.java` is replaced by the shared C# downloader above once its tests match.
- **Renderer choice:** the Windows head is real WinUI (native Mica, SMTC, composition); Android
  uses Uno's Skia renderer. One XAML tree for both.

## 2. Keeping the Material 3 look

### 2.1 Base styles
- **Uno.Themes Material (v2 = Material 3)** as the base control styles: buttons (filled, tonal,
  outlined, text), switches, sliders, chips, text fields, nav rail, cards, dialogs. Every style is
  then tuned to match `styles.css` exactly (sizes, radii, paddings, `--row-pad`, densities).
- **Uno Toolkit** for the bits WinUI lacks: `TabBar` (sidebar / bottom nav), `AutoLayout`,
  `ShadowContainer` (M3 elevation), `SafeArea` (Android).

### 2.2 Tokens: CSS → XAML resources (one-to-one)

| CSS today | XAML | Notes |
| --- | --- | --- |
| `--primary`, `--on-primary`, `--primary-container`, `--surface`…`--surface-4`, `--on-surface(-var)`, `--outline(-var)`, `--error`, `--bg` | `ColorPaletteOverride` → `PrimaryBrush`, `SurfaceContainerLowBrush`…`SurfaceContainerHighestBrush`, etc. | Same hex values for the default accent `#bfc2f0`. |
| Theme: System / Light / Dark / Black | `ThemeService` + a "Black" palette (pure #000 surfaces) | Black = same scheme, surfaces forced to black. |
| `--ease`, `--ease-emph`, `--fosi` | `KeySpline`s / `CubicBezierEasingFunction` in Composition | Same control points. |
| `--spring-m3`, `--spring-bouncy` (from Compose springs) | `SpringScalarNaturalMotionAnimation` / `SpringVector3NaturalMotionAnimation` | Composition springs take a damping ratio and period, like Compose — we reproduce the originals instead of sampled curves. |
| `--d-fast/med/slow` 220/420/600 ms | `Duration` resources | |
| radii (8/12/16/28, pills) | `CornerRadius` resources (`ShapeSmall`… `ShapeFull`) | |
| `--sidebar-w 236`, `--player-h 88`, `--titlebar-h 48` | layout resources | Same numbers. |
| fonts: Segoe UI Variable + Noto Sinhala/Tamil fallbacks | `FontFamily` with fallback list | Sinhala/Tamil must render; tested with real titles. |

### 2.3 Dynamic colour (polished)
Today `extractColor` picks one hue/saturation from a 48×48 canvas. In C#:
- Use the **Material Color Utilities** algorithm (quantize → score → HCT tonal palettes → full M3
  scheme), C# port. Result: proper tones for every role (containers, on-colours, surfaces) in light,
  dark and black, with guaranteed contrast.
- Palettes are cached per thumbnail URL (as now) and **animated** between songs (brush colour
  transitions on the composition thread, ~420 ms, `--ease`).
- Accent picker and "Dynamic colours" off behave exactly as today.

### 2.4 Icons
- **Material Symbols Outlined, weight 600** (and `-fill` variants for filled states), exactly the
  set in `renderer/icons.js`. WinUI can't set variable-font axes reliably, so we ship a **static
  instance generated at wght 600** (fonttools `instancer`, a build script in `tools/`) and use
  `FontIcon` glyphs. Pixel-checked against the current SVGs.

### 2.5 Mica under Material
- `MicaBackdrop` (or `MicaAlt`) on the window, with the M3 `surface` drawn at the same translucency
  the Electron `mica` setting uses today; off = solid surface. The custom title bar
  (`ExtendsContentIntoTitleBar`, 48 px) keeps the search field and caption buttons tinted with the
  scheme (replaces `titleBarOverlay` + `titlebarColor`).

## 3. Page and component map

Every page becomes a XAML page + view model. Lists are virtualised (`ItemsRepeater`) everywhere.

| Today (app.js) | WinUI | Polish |
| --- | --- | --- |
| Shell: title bar, sidebar (236 px), main, player bar (88 px) | `NavigationRoot` grid, Toolkit `TabBar` as rail | Rail collapses at narrow widths, same as the phone layout. |
| Page transitions (Android NavHost ±1/8 slide + 200 ms fade) | Custom `NavigationTransitionInfo` with composition springs | Back/forward gestures and mouse back button. |
| Home (chips, shelves, "Made for you", load more) | `HomePage` | Incremental loading, skeleton shimmer instead of blank. |
| Explore, Browse/moods | `ExplorePage`, `BrowsePage` | |
| Search + suggestions + correction + filters + load more | `SearchPage`, suggest flyout in title bar | Debounced, keyboard-first. |
| Album, Playlist, Artist, Liked, Local playlist | `CollectionPage` (shared header + track list) | "Now playing" bars on covers, Pause-instead-of-Play kept. |
| Library (songs, downloads, playlists, albums, artists) | `LibraryPage` | |
| Local files (folders, scan progress, album/artist filters) | `FilesPage` | Folder watching via `FileSystemWatcher`, no rescans. |
| History, Stats | `HistoryPage`, `StatsPage` | |
| Queue panel + "how the next song comes in" chip | `QueuePane` | Drag-reorder with the M3 lift; Flow plan shown in the chip (roadmap). |
| Now playing (classic artwork morph / Android bottom sheet) + lyrics / up next / related | `NowPlayingSheet` | Connected-animation artwork morph; the sheet is a real spring with velocity from the drag. |
| Squiggly seekbar | `SquigglySlider` (Composition shapes / Win2D) | Runs on the GPU; amplitude eases to flat on pause, like Android. |
| Synced lyrics (classic glow / Android motion), size, alignment, auto-scroll, BT delay, offsets | `LyricsView` | Glow via composition shadow; word-level timing when the source has it. |
| Sound page: meters, AutoEq picker, 10-band EQ, A/B hold | `SoundPage` with Win2D meters and EQ curve | Meters at display refresh, drawn from the limiter's data with no UI-thread work. |
| Equalizer presets / device profiles | `EqualizerPage` | |
| Settings (14 groups, ~50 rows) | `SettingsPage` with M3 list rows | Search box over settings. Rows hidden on a platform via the same `MOBILE_HIDDEN` idea. |
| Context menus, toast, modals | M3-styled `MenuFlyout`, snackbar, `ContentDialog` | |
| Ctrl+K command bar | `CommandPalette` | Same commands list (`paletteCommands`). |
| Focus mode (ambient field, auto-hide UI) | `FocusPage` | |
| Welcome + first-run notice (artists aren't paid through the app) | `WelcomePage`, `NoticeBar` | Notice wording unchanged; Premium check as today. |
| Mini player | `CompactOverlay` presenter (always on top) | A real picture-in-picture window. |
| Import (Spotify link, CSV, file, progress, cancel) | `ImportDialog` | |
| Keyboard shortcuts (space, arrows, `[` `]`, Ctrl+K, F, L, …) | `KeyboardAccelerator`s | Same keys; listed in Settings. |

## 4. Parity checklist (every row must pass before Electron is retired)

**Playback & sound.** Two-deck engine; crossfade (smooth / equal power / linear); smart crossfade;
mix style with bass swap; gapless albums; crossfade on skip; normalisation to −19/−14/−11 LUFS with
YouTube and ReplayGain data and measured+cached `DB.lufs`; quiet-song boost up to +6 dB behind the
limiter; true-peak limiter (2 ms look-ahead, 4× true peak, 80 ms release, −1 dBTP); 10-band EQ and
presets incl. VC (Warm vocal) and device presets; auto headroom; AutoEq headphone correction
(~8,850 profiles); loudness compensation; stereo width; crossfeed off/light/strong; mono; balance;
skip silence (speed-up and instant); tempo & pitch; varispeed; output device choice; per-device
sound profiles; auto device EQ; pause on disconnect; Bluetooth lyrics delay; smart ducking
(duck/pause, level); sleep timer with fade; focus timer; audio quality best/low; A/B bypass;
live meters; suspend audio 12 s after pause.

**Music.** Home/explore/browse/search/album/artist/playlist/radio/related/up next; sign-in through
the real browser; library sync; likes sync; Premium detection; lyrics (LRCLIB then YouTube Music)
with offsets; Flow radio (learn songs, crossfade by how songs mix); autoplay; skip on error;
no duplicates; persistent queue; remember shuffle/repeat; content language and country.

**Playback rules learned in Android 0.1.2–0.1.6 (shared code, so they apply to desktop too).**
Pause pressed while a song is still being looked up holds the start instead of being ignored;
system/media-key Play and Pause only play or only pause (never toggle); an unrequested pause while
starting is retried once; the stall watch keeps waiting (up to a minute) while data is still
arriving instead of restarting the song; phones allow up to 8 stall checks, desktop 2; a reload
keeps the song's download. Audio quality Auto / High / Low (Auto on phones: lighter stream on
2G/3G or Data Saver, checked per song). Settings → Copy diagnostic log (host names only, no
cookies).

**Library & files.** Liked, playlists, saved albums/playlists, followed artists, history, search
history, stats; downloads (add, remove, size, open folder, auto-download liked); local folders
(add, remove, scan, open, reveal, drag & drop, open-with); Spotify/CSV import; backup/restore
(including old `metrolist-desktop` backups); one-time carry-over from `%APPDATA%\Metrolist` and
now from `%APPDATA%\Soncle`.

**Windows.** Tray / close to tray; taskbar thumbnail buttons; system media controls; single
instance; Mica; custom title bar; fullscreen; mini player; Discord Rich Presence (still hidden
until Soncle has its own client id); per-user installer with folder choice; portable build.

**Look.** Every page matches the Electron screenshot at 1280×800 and at the narrow layout, in
dark, light and black, with dynamic colour on and off.

## 5. Audio engine in C# (`Soncle.Audio`)

Signal path is unchanged (deck: media → norm → bass shelf 180 Hz → fade; master: pre → EQ →
correction → loudness comp → width/crossfeed → master → duck → mono → pan → limiter), but it now
runs in **our own render loop** instead of Web Audio:

- **Decode:** WebM/Opus (our WebM parser, already written for Flow, plus Opus via Concentus or
  libopus), M4A/AAC and local files via Media Foundation on Windows (MediaCodec on Android).
  Local FLAC/MP3/etc. through the same path.
- **DSP:** biquads, shelves, crossfeed matrix, K-weighted LUFS meter and the polyphase true-peak
  limiter ported from `worklets.js`, allocation-free, `Span<float>` + `System.Numerics.Vector`.
  If the CPU budget isn't met, the hot loops move to C and are called through P/Invoke.
- **Output:** WASAPI shared mode, event-driven, float32 at the device rate; resampling only when
  the source rate differs. Optional **exclusive mode** (lower latency, no system mixer) as a
  setting.
- **Tempo/pitch:** SoundTouch (LGPL, GPL-compatible) for `preservesPitch`, varispeed is plain
  resampling.
- **What gets better:** gapless and crossfades become **sample-accurate** — no warm-up, no
  measured start delay, no 12 ms overlap trick. Beat-phase alignment and tempo nudge from the
  roadmap become straightforward. Skip-silence reads the decoded samples instead of polling an
  analyser.
- **Crossfade rule (Oct 2026 fix):** no blend style may make the overlap louder than either song
  (`test/crossfade.test.mjs`); a "mix" is the user's curve and length plus the bass swap, until
  beat-phase alignment lands. The C# port starts from this, not from the old 16-beat mix.
- **Reference tests carried over:** no true-peak overs on the test signals; −23 LUFS reference
  tone; gapless handover clean (sample-exact, checked by rendering offline); crossfade curves
  match the JS curves within 0.1 dB.

## 6. YouTube Music in C# (`Soncle.YouTube`)

- Port `yt.mjs` normalisation one function at a time; test against **recorded fixtures** exported
  from today's offline YouTube double, so both implementations parse the same responses into the
  same JSON. Metrolist's Kotlin innertube is the second reference.
- **Stream clients:** same order (YTMUSIC+PO, IOS, ANDROID_VR, WEB+PO, TV, WEB_EMBEDDED), same
  range probe, same 1 MB ANDROID_VR rule.
- **Deciphering (n/sig):** prefer clients that don't need the player script; for those that do,
  run the player's extracted functions in **Jint** (pure .NET JS engine) — check YoutubeExplode's
  current approach first.
- **PO tokens:** keep the 0.1.3 fixes — one minter shared by concurrent requests, a failed token
  doesn't break another request in flight, init gives up after 25 s, no orphaned helper left behind.
  On Windows: a hidden **WebView2** running the same `po_token.html` with a youtube.com origin
  (WebView2 is already on every Windows 11 PC). Closed after 10 min idle, as today, so it costs
  nothing when you're not playing.
- **Sign-in:** keep the real-browser flow (Chrome/Edge/Brave, temporary profile,
  `--remote-debugging-pipe`) — ported to C#. The cookie is stored with **DPAPI**
  (`ProtectedData`, current user) and never reaches view models; the account service exposes only
  status. Carry-over: the last Electron release writes the cookie into a DPAPI-protected file the
  WinUI app imports once (otherwise one fresh sign-in).

## 7. Windows integration (Windows head)

| Feature | API |
| --- | --- |
| System media controls (flyout, keyboard media keys, lock screen) | `SystemMediaTransportControls` via the desktop interop |
| Taskbar prev/play/next buttons | `ITaskbarList3` thumb buttons (CsWin32) |
| Tray + close to tray | H.NotifyIcon.WinUI |
| Single instance, open-with, jump list | `AppInstance`, `JumpList` |
| Mini player | `AppWindow` with `CompactOverlayPresenter` |
| Output devices, Bluetooth detection, pause on disconnect | `IMMDeviceEnumerator` + notifications |
| Smart ducking | WASAPI audio session notifications (`IAudioSessionManager2`) — replaces `ducking.mjs` |
| Power / battery | `PowerManager` |
| Discord | named-pipe IPC (unchanged protocol) |

## 8. Polish list (beyond parity)

- Desktop gets the phone's resilience: whole-song cache, next song fetched ahead, retry through
  network drops, and **Auto quality on metered connections** (Windows `ConnectionCost`).
- Copy diagnostic log on desktop too (same redaction rules).
- Sample-accurate gapless and crossfades; beat-matched mixes (ROADMAP items 1–6) built natively.
- Real M3 dynamic schemes with animated colour changes.
- Spring motion on the composition thread: the UI never stutters while a page loads.
- Skeleton loading on every page; optimistic likes and queue edits.
- Real always-on-top mini player; settings search; drag-reorder queue with M3 lift.
- Faster cold start (target < 1 s to first frame) and much lower memory — no Chromium processes.
- Sinhala and Tamil text rendering checked with real titles.
- Accessibility: Narrator names for every control, full keyboard navigation, high-contrast check.

## 9. Measurement and testing

**Baseline first (Electron 1.0.0, same PC, same songs):** cold start to first frame, working set
idle / playing / after an hour, CPU idle and playing, processes. Today's known numbers:
~0.1 % CPU idle, ~8 % playing.
**Targets for WinUI:** idle CPU ≤ 0.1 %, playing ≤ 3 %, memory well under Electron's (target
< 150 MB playing, measured), no audio dropouts under load.

- `dotnet test`: unit tests per library, run in CI on Linux (libraries) and Windows (head).
- Shared fixtures: the JS tests' recorded responses and audio reference numbers live in one
  `fixtures/` folder used by both `npm test` and `dotnet test`.
- **Golden screenshots:** a script captures every page in Electron (`SONCLE_SHOT`) and WinUI at the
  same size and state with the mock data; CI publishes the side-by-side sheet.
- **Playback state machine** (`PlaybackController` in `Soncle.App`, no UI): every rule in the
  "playback rules" block of §4 becomes a unit test (hold-start, play/pause from the system, stall
  watch with arriving data, AbortError retry), so the behaviour that took six Android releases to
  get right can't regress.
- **Downloader tests** against a fake googlevideo server: dead zone mid-song, URL expiry, 403 →
  client switch, far seek, prefetch ordering, cache eviction.
- `tools/measure` script for the resource numbers, run on every preview build.

## 10. Build and delivery

- GitHub Actions on `windows-latest`: `dotnet publish` self-contained (Windows App SDK
  self-contained), trimmed, ReadyToRun; try Native AOT and keep it if startup and size improve.
- **Preview channel:** every push to the migration branch replaces a `windows-preview` release
  (like `android-latest`), so it can be installed from a link. Installs side by side as
  "Soncle Preview" with its own data folder until the switch.
- Installer: per-user installer with folder choice (Inno Setup, or Velopack for built-in
  updates) plus a portable zip. Still unsigned for now (SmartScreen warns); look into free
  open-source signing (e.g. SignPath) before the switch.
- Workflows keep the `concurrency` groups added in 0.1.3, so an older build never replaces a newer
  one; the Android head keeps the fixed test signing key so updates install over each other.
- Add unoplatform/uno, Uno.Themes and YoutubeExplode to `upstream/watch.json` when work starts.

## 11. Stages and exit criteria

| Stage | Work | Done when |
| --- | --- | --- |
| **0. Prep** (in the current repo) | Export fixtures and audio reference numbers to `fixtures/`; record the playback-rule cases and `SoncleStreams` behaviour as fixtures; measure Electron baselines; screenshot every page; generate the weight-600 icon font. | Baselines and golden screenshots committed. |
| **1. Libraries** | `Soncle.Core`, `Soncle.YouTube`, `Soncle.Streams`, `Soncle.Flow` + tests; console app that searches and resolves a stream. | Fixture tests match JS output; a song resolves on a real PC. |
| **2. Audio** | `Soncle.Audio` on WASAPI: decks, master chain, limiter, LUFS, gapless, crossfade, tempo. | Reference tests pass; minuda2009 A/B-listens against Electron and hears no regression. |
| **3. Design system** | `Soncle.Design`: tokens, dynamic colour, icon font, styles, springs; a gallery page of every control. | Gallery matches the CSS components in screenshots, all themes. |
| **4. Shell + core pages** | Window, Mica, title bar, rail, player bar, Home, Search, collections, Now playing, queue, lyrics, SMTC, tray. | Daily-usable preview build on `windows-preview`. |
| **5. Everything else** | Sound/EQ/AutoEq, settings, library, local files, downloads, import, stats, focus, welcome, mini player, palette, Discord, carry-over. | Parity checklist (§4) all green. |
| **6. Polish + switch** | §8 items, performance pass, accessibility, installer. Final Electron release adds the cookie hand-off and points to the new app. | Targets in §9 met; minuda2009 signs off; Electron retired (code kept in history). |

The roadmap's next desktop version (smart crossfade, Flow full-pool analysis) still ships in
Electron first, as committed; its tests become fixtures so the C# port has to match them.

## 12. Risks

- **YouTube changes mid-port** — two implementations to keep alive for a while. Mitigation: the
  upstream watch, shared fixtures, port `yt.mjs` last-known-good and keep it in sync weekly.
- **Deciphering in .NET** — Jint speed/compat with new player scripts. Mitigation: token-free
  clients first; fall back to WebView2 (already loaded for PO tokens) to run the player JS.
- **Uno Material ≠ our CSS exactly** — expect to restyle many templates. Mitigation: gallery page
  and screenshot diffs from stage 3.
- **Audio thread GC pauses** — allocation-free rule, a dedicated high-priority thread (MMCSS
  "Pro Audio"), and a dropout counter in debug builds.
- **Order of the heads.** ROADMAP.md puts Android before Windows. Android now has substantial
  native Java (media service, streams, BotGuard), which lowers the risk of its port, while the
  shared libraries in stages 1–3 serve both heads. Either order works after stage 3; this plan
  assumes Windows first only if minuda2009 confirms.
- **Unsigned builds** — SmartScreen friction stays until signing is sorted.
