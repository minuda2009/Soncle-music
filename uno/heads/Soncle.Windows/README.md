# Soncle.Windows — the WinUI 3 head (Soncle Preview)

Stage 4 of the migration (`docs/WINUI_PLAN.md` §1, §11): the real WinUI 3 / Windows App SDK
head that opens Soncle's shell on Windows. It is **Windows only** and is deliberately not in
`uno/Soncle.sln`, so the Linux job keeps building and testing the libraries unchanged.

## What it has

- a window with **Mica** (`MicaBackdrop`, solid when unsupported), the **48 px custom title bar**
  (`ExtendsContentIntoTitleBar` + the `Tall` caption row);
- the four destinations — **Home · Library · Sound · Settings** — in a rail;
- the **player bar** with the empty state from `docs/DESIGN.md` §4.1 (never a blank square with
  `0:00 / 0:00`), kept exactly until a song is chosen;
- **search and play** (the first `Soncle.App` slice, `Playback/PlaybackHost.cs` composes it):
  - a search box in the title bar (`docs/WINUI_PLAN.md` §3; Ctrl+K focuses it). The rest of the
    title bar still drags the window: `WindowChrome.UseTitleBar` keeps the grid as the title bar
    and cuts the box out as an `InputNonClientPointerSource` pass-through rectangle, recomputed
    when the bar is resized or the display scale changes;
  - Enter searches (InnerTube `search`, type `all`; songs and videos are listed, top result first);
    the results list shows artwork, title and artists · album · duration, a spinner while searching
    and a plain message when nothing was found or the search failed;
  - clicking a row (or Enter on it) plays it with all results as the queue. The player bar then
    shows artwork, title and artists, previous / play-pause / next, the seek bar with `m:ss`
    elapsed and total (it seeks when you let go), a spinner while the song is looked up,
    downloaded and decoded, and "Couldn't play this song." when it can't be played;
  - the **media session** (`SystemMediaTransportControls`, `Playback/MediaSession.cs`) reports
    nothing playing until then, and afterwards the title, artists, artwork and Playing / Paused;
    its Play / Pause / Next / Previous buttons (media keys, the Windows media flyout) drive the
    same `PlaybackController`.
- **sound over WASAPI** (`Playback/WasapiAudioOutput.cs`, NAudio.Wasapi 2.2.1): shared mode,
  event-driven, 32-bit float, 48 kHz stereo on the default output device. Devices whose mix
  format is different (44.1 kHz, 24-bit) are converted by Windows (NAudio opens shared streams with
  `AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | SRC_DEFAULT_QUALITY`). The render path does not allocate
  (NAudio's byte buffer is reinterpreted as floats in place), each start fades in and each stop
  plays one last faded block before the device is released on a pool thread, and a callback or
  device failure (unplugged, taken in exclusive mode) stops the session and shows "The sound device
  stopped. Press play to try again." in the bar. Exclusive mode and a device picker are later
  settings (`docs/WINUI_PLAN.md` §5).

How a song gets to the speakers: `StreamResolver` (clients **ANDROID_VR**, then **IOS**, the two
that return plain URLs: no PO token, no deciphering yet) with the **WebM/Opus-only** filter →
`SongDownloader` downloads the song to its cache file (a 256 KB first piece, then ≤ 1 MB
requests) → **`StreamingOpusSource`** demuxes and decodes it with Concentus *while it downloads* →
`EnginePlayer` (one engine per song, loudness levelled to −14 LUFS, limiter last) →
`WasapiAudioOutput`. **Sound starts as soon as the head and the first packets are in** (the first
piece), not after the whole song.

Streaming start, in short (`uno/src/Soncle.Audio/StreamingOpusSource.cs`):

- a decode-ahead thread per song fills a preallocated ring of about 5.5 s of PCM (2 MB), so a song
  of any length uses the same memory: **there is no length limit any more** (the old 20-minute cap
  is gone);
- the audio thread only copies out of the ring: it never waits, locks or allocates. If the download
  falls behind (network drop, slow connection) the song fades out over 10 ms, plays silence, the
  bar shows its **spinner while buffering** (the song stays "playing", like a browser's `<audio>`),
  and the sound fades back in when the bytes arrive (the downloader retries for about 10 minutes);
- a seek restarts decoding at the WebM cue before the target (80 ms pre-roll) and drops what comes
  before it; a seek **far past the download** is read straight from the network at that byte
  offset while the cache keeps filling. Files without Cues in front re-read from the first cluster;
- the length shown is the file's Duration until the real end is decoded, and only the real end
  ends a song, so an estimate can't cut one short;
- a download that fails before the first audio shows "Couldn't play this song." (it doesn't skip
  silently); one that fails mid-song ends the song there, and seeking tries again;
- the song playing is never dropped from the download cache while it streams from it (the cache
  keeps four songs, oldest-used first);
- the decode thread sleeps while the ring is full and waits for a seek at the end, instead of
  polling: 16 idle songs went from about 5.7 % to 0.4 % of one core in the Linux test harness.

Playback CPU and memory are not measured yet: pending on minuda2009's PC (step 7 below).

A quick pause → play (double click, the media key twice) starts a new WASAPI session while the old
one still owes its faded last block. `Start` waits up to one buffer (80 ms) for that block, then
closes the old session's `RenderGate`, so only one session ever calls the engine; the old one
renders silence from then on and its failures no longer reach the bar.

The transport icons (play/pause/previous/next, Segoe Fluent glyphs) and the search box's
`SymbolIcon Find` follow the rail's current placeholders; they become Material Symbols outlined at
weight 600 with the Material Symbols font task, so screenshots of this build don't show the final
look.

Signed out, always: one `HttpClient` with `UseCookies = false`, no account, no credentials. Nothing
touches the network at start-up (the InnerTube session is created on the first search, and retried
by the next one if it failed), so the app opens offline. Downloads go to the preview's own
`%LOCALAPPDATA%\Soncle Preview\streams` (emptied at start-up), never the shipping app's
`%APPDATA%\Soncle`. Diagnostics are one-line `Trace` messages (`Soncle ...`, visible in a debugger
or DebugView) with URLs cut out; no URL, header or token is logged.

**PO tokens (built, not used for playback yet).** `Playback/WebViewBotGuardPage.cs` is the hidden
BotGuard page of `docs/WINUI_PLAN.md` §6: a 1×1 invisible WebView2 in the window that answers
`https://www.youtube.com/soncle-potoken*` (`AddWebResourceRequestedFilter` +
`WebResourceRequested`) with `src/po_token.html`, embedded unchanged from the repo
(`<EmbeddedResource LogicalName="Soncle.po_token.html">`), then `Navigate`s there (not
`NavigateToString`, which gives the wrong origin). Every other request in that WebView gets a 404,
as in `potoken.mjs`. `Soncle.YouTube.PoTokenMinter` drives it (Create → `runBotGuard` →
GenerateIT → `createPoTokenMinter` → `obtainPoToken`) and closes it after 10 minutes without a
request. The resolver is **not** given the minter: the YTMUSIC and WEB clients stay off until an
`IPlayerScript` decipherer exists, because their URLs need deciphering as well as a token. The page
is created only when a token is minted, and nothing mints one unless `SONCLE_POTOKEN_SELFTEST=1` is
set (see "What to test"). The token is never logged, only its length.

Not here yet, by scope: AAC-only songs,
PO-token and deciphered clients (YTMUSIC, WEB), sign-in and the library, a queue view, lyrics, the
Sound page, tray / close to tray, rail collapse, the other `Soncle.App` pages, `Soncle.Design`
(stage 3) and the own-data-folder carry-over.

## What to test on Windows

1. Start `Soncle.Windows.exe` with the network off: the window opens, the bar says "Nothing
   playing". Turn the network on.
2. Drag the window by the title bar on both sides of the search box; click into the box and type.
3. Search (Enter) for an artist; the list fills. Pick a song: the bar shows it with a spinner, then
   plays. **Note the time from the click to the first sound** (it should be about a second on a
   normal connection: only the first piece has to arrive).
4. Pause / play, next / previous, drag the seek bar and let go, and use the keyboard media keys and
   the Windows media flyout. Listen for clicks at start, pause, skip and seek. Also double-click
   play/pause and press the play/pause media key twice quickly: it should keep playing without a
   skip or an error. Click once on the seek bar's track: one short dip, not two.
5. Switch the default device to one at 44.1 kHz, and unplug headphones while playing: the bar should
   say the sound device stopped; Play starts it again on the new default device.
6. Note any song that says "Couldn't play this song." (AAC-only songs are expected to).
7. **Long songs:** search for a one-hour mix (anything over 20 minutes): it must start as quickly
   as a short song and play; skip to near the end with the seek bar.
8. **Seek past the download:** right after a long song starts, drag the seek bar far ahead (past
   what can have downloaded): sound should come back within a second or two at the new spot, with
   no click and no error.
9. **Network pull mid-song:** while a song plays, turn Wi-Fi off (or pull the cable). Once the
   decoded seconds run out the sound fades out and the bar shows the spinner (the song stays
   "playing"); turn the network back on: the sound fades back in where it stopped. Note how long
   the gap was and whether anything clicked.
10. With a typical 3–5 minute song playing, note the working set and CPU of `Soncle.Windows.exe` in
   Task Manager (Details tab), and again after a few skips; those are the first playback numbers.
11. **Slow connection:** with a throttled connection (e.g. a phone hotspot at 1 bar) a song that
   stalls should come back for a stretch each time, not flicker between sound and spinner every
   moment; a song whose download gives up mid-way fades out (no click) and the next one starts.
12. **PO token self-test** (only this build's BotGuard page; playback doesn't use it yet): in
   PowerShell `$env:SONCLE_POTOKEN_SELFTEST = "1"; ./Soncle.Windows.exe`, then search once. In
   DebugView (capture Win32) look for `Soncle potoken-selftest ok length=…` within a few
   seconds (or `potoken-selftest failed …` with the reason) and `Soncle PO token minter ready (ttl
   …s)`. A line `helper page origin is …` means the page did not get the youtube.com origin.
   Ten minutes later, without another search, the WebView2 child processes
   (`msedgewebview2.exe` under Soncle.Windows in Task Manager) should be gone.

## Measured (shell, `windows-latest`, 10 Oct 2026)

The workflow does not just compile the head: it extracts the packed build, launches it and fails
if the process exits or shows no window within 90 s. From that run:

| | Shell, idle | Electron 1.8.0 |
| --- | --- | --- |
| Window shown after start | 0.8–3.3 s (first-frame proxy, three runs) | not recorded |
| Working set | 102–116 MB, 117–120 MB after 5 s | ~500 MB, all processes (`CHANGELOG.md`) |
| Download | 136.8 MB zip, run from a folder | installer |

The window title the shell sets is `Soncle Preview` (`Window.Title`), the same string the custom
title bar draws. The numbers come from the runner (a shared VM, no GPU, self-contained and
untrimmed publish — trimming, ReadyToRun and Native AOT are the stage 6 performance pass), so they
are the first WinUI build's, not minuda2009's PC: CPU and the on-PC run come from the preview link.

## Notes

- The rail is a WinUI `NavigationView` in `Left` mode. `docs/WINUI_PLAN.md` §3 names the Uno
  Toolkit `TabBar` for it, but `TabBar` is a single horizontal line in Uno Toolkit (no vertical
  rail), so the shell uses the platform rail until `Soncle.Design` styles the final one.
- The head is not in `uno/Soncle.sln`: adding a `net10.0-windows` project would put it on the
  ubuntu job's path. The `windows-preview` workflow builds the csproj directly.

## Build and run (Windows)

```powershell
dotnet publish uno/heads/Soncle.Windows/Soncle.Windows.csproj `
  -c Release -f net10.0-windows10.0.26100 -r win-x64 -p:Platform=x64 --self-contained true `
  -p:WindowsPackageType=None -p:PublishTrimmed=false -p:PublishReadyToRun=false -o publish
./publish/Soncle.Windows.exe
```

`.github/workflows/windows-preview.yml` does exactly this on `windows-latest` and replaces the
`windows-preview` release with the zip. That job first launches the packed build (a window must
appear) and only then touches the release, so a head that no longer starts never replaces the last
good preview. The build is unsigned, so SmartScreen warns; run from a folder, nothing is installed.
Its app id is `com.minuda2009.soncle.preview` and it never reads or writes the shipping app's
`%APPDATA%\Soncle` (only the stream cache above is written; the carry-over arrives with the
switch).

Building for Windows needs Windows: Uno's SDK stops with `UNOB0014` on other systems, and
WinAppSDK's XAML compiler is a Windows binary.
