# Changelog

## Unreleased: the Windows head starts (Soncle Preview)

- **A first Windows build of the C# app** now comes from `uno/heads/Soncle.Windows` and is
  published to the `windows-preview` release on every push that touches `uno/**` (the same way the
  phone gets `android-preview`). It runs from a folder, installs nothing, is named **Soncle
  Preview**, and leaves the shipping Electron app and its library untouched. It is unsigned, so
  SmartScreen warns.
- This first slice is the **shell**: a Mica window with the 48 px custom title bar, the four
  destinations (Home · Library · Sound · Settings) in a rail, and the player bar. The pages,
  tray, and the design system come next (`docs/WINUI_PLAN.md` stage 4).
- The build is **checked, not just compiled**: the workflow launches the packed build and needs a
  window, so a preview that no longer starts never replaces the last good one. First numbers from
  those runs (shell only, `windows-latest`, self-contained and untrimmed): window after 0.8–3.3 s,
  102–116 MB working set at idle against Electron 1.8.0's ~500 MB across five processes.
- The Linux build and test of the C# libraries is unchanged; the head is Windows-only and not in
  `uno/Soncle.sln`.

## Unreleased: Android sound profiles that follow the output

- **The phone now knows what the sound is going to** — its speaker, wired headphones, USB, a
  Bluetooth device by name, HDMI or the car. Soncle registers Android's audio device callback and
  listens for the "audio becoming noisy" broadcast, so it follows the output as you plug in, connect
  or unplug. On Android 13+ it asks Android directly which device the music is routed to, so with
  both Bluetooth and wired connected it picks the right one instead of guessing.
- **Per-device sound profiles work on the phone, like on the desktop.** Each output remembers its
  own EQ, sound settings and volume, and switching outputs brings that profile back (within about a
  second). "Per-device sound profiles", "Auto-tune new devices" and "Saved device profiles" are now
  in Android's settings; "Output device" stays hidden because Android chooses the output itself.
- **LE Audio earbuds** (newer Galaxy and Pixel phones) are recognised as Bluetooth, so they get
  their own profile instead of the phone speaker's, and unplugging them pauses.
- **Cars are recognised by their Bluetooth class**, not just a brand name, so a car that reports
  itself as "Uconnect" or a model number still gets the car profile.
- **Each USB DAC keeps its own profile** (they are told apart by product name), and a car's profile
  is kept apart from a phone speaker's.
- **Pause when headphones disconnect** now works on the phone too, using Android's becoming-noisy
  broadcast; it no longer pauses twice when the broadcast and the device change both arrive.
- Bluetooth names ("Galaxy Buds2") need Android 12+'s Bluetooth permission. Soncle asks for it when
  you touch "Per-device sound profiles" (whether you switch it on or off), and works from the output
  type alone if you decline.

## Unreleased: crossfades that sound right

Measured against the curves in `engine.js`. The plain fade was already the same as Metrolist's;
the problems were in what Soncle adds on top. Desktop and Android both get this.
- **"Smart mix" no longer piles two songs up.** It held both songs near full volume for a third of
  the blend (+3 dB louder, +6 dB on coinciding kicks), with beats that weren't aligned, so kicks
  flammed and the limiter squashed it. It now uses your crossfade curve, plus the bass swap in the
  middle. Beat-aligned mixes come with the roadmap's beat-phase work.
- **A mix lasts as long as your crossfade setting.** It used to stretch to 16 beats: 9.6 s when you
  chose 5 s, up to 14 s.
- **Songs that would clash get a shorter fade, not a cut.** 60 % of your setting (at least 3 s),
  instead of 2.5 s whatever you chose.
- **Smart crossfade no longer cuts off a last chorus.** It started the blend on any quiet moment in
  the last 12 s plus the crossfade; now only a real tail counts (the last few seconds, quiet for
  over a second).
- New tests: no crossfade curve may make the overlap louder than either song.

## Android 0.1.6: built for listening on the move

- **Each song downloads in full to the phone's cache**, as fast as the connection allows. The
  player reads from that copy, so a tunnel or a weak spot only pauses the download, while what has
  already arrived keeps playing. A lost connection is retried with back-off for about 10 minutes.
  A seek far past what has arrived is fetched from the network directly.
- **The next song is fetched ahead of time**, once the current one has finished downloading, so
  it never competes with the song that's playing. The switch to it needs no network.
- **Auto quality** (the new default on phones): the lighter stream on 2G/3G or with Data Saver
  on, the best stream otherwise. It's checked for each song.
- **Riding out dead zones:** a reload keeps the song's download rather than starting over, and the
  phone waits longer (up to 8 stall checks) before giving up on a song.

## Android 0.1.5

- **Songs start much sooner.** The stream proxy now passes audio on as it downloads. Before, it
  waited for each whole 1 MB piece. The first piece is also small (256 KB), and connections are
  reused between pieces.
- **A slow start is no longer treated as a stall** (desktop too). Before, the app restarted the
  song after 12 s with nothing playable, throwing away what had already arrived. Now, while data
  is still arriving, it keeps waiting (up to a minute).

## Android 0.1.4

- **Playback rebuilt the way desktop does it.**
  - Songs now play from `https://localhost/_soncle/stream/…`. Native code answers those requests
    (`SoncleStreams.java`), the phone's version of desktop's `mstream:` proxy.
  - The audio element sees an ordinary seekable file. The bytes come from googlevideo in 1 MB
    pieces, sent with the headers each stream client needs.
  - When a URL expires mid-song, the proxy gets a fresh URL for the same file and carries on.
  - This replaces the MediaSource player, which is kept only for the browser test harness.

## Android 0.1.3

- **Google Maps support.** Soncle's own media service (`SoncleMediaService.java`, a
  MediaBrowserService) replaces the third-party media-session plugin. Google Maps can now show
  Soncle's song and its play/pause/skip controls during navigation. Lock screen, notification,
  headset and car controls go through the same service, with album art in the notification.
- **Updates install over the previous version.** Test builds are now signed with one fixed key, so
  each new APK installs over the last and keeps your library. Before this, every build had a new
  random key. This one time, uninstall 0.1.2 first.
- **Fixes from a full code review:**
  - **Pause works while a song is still loading** (lookup can take a few seconds on a phone).
    Before, the song started anyway. Desktop too.
  - **A song can't get mixed into another file.** Each song keeps downloading from the same
    file, even if the quality setting changes or the stream URL has to be refreshed mid-song.
  - **PO tokens:**
    - a failed token no longer breaks a second request running at the same time;
    - a failed start gives up after 25 s instead of 45 s;
    - desktop no longer leaves an orphaned hidden window behind.
  - **Closing the file picker** without choosing a file no longer leaves restore or import
    hanging.
  - **Carousel arrows** are hidden on the phone (swipe instead), as intended.
  - **Lint passes again, so CI is green.** The weekly upstream pull request was being skipped
    because of it.
  - **GitHub builds:**
    - only one build runs at a time, so an older build can't replace a newer APK;
    - the upstream watch copes with upstream history being rewritten.

## Android 0.1.2

- **Fixed "The play() request was interrupted by a call to pause()".** The system's media-session
  "play" signal arrives when the playback notification starts. It was being treated as a
  play/pause toggle, so it paused the song that was just starting. The notification and headset
  Play and Pause buttons now only play or only pause (desktop too).
- **A pause the app didn't ask for** while a song is starting is retried once, instead of being
  reported as an error.
- **New: Settings → Copy diagnostic log** (Android). It copies recent app messages for a bug
  report. Web addresses are cut to their host name, and cookie values are removed.

## Android 0.1.1

- **Playback fixed.** The phone now gets PO tokens the same way desktop does: BotGuard runs in a
  hidden youtube.com WebView (`SonclePlugin.java`). It uses the same stream clients as desktop,
  in the same order. Audio requests are sent natively, so googlevideo receives the headers it
  expects.
- **Sign-in.** Google's sign-in page opens full screen inside the app. Once you're signed in, the
  app keeps the YouTube session in its private storage, and it never reaches the UI's settings
  store. Library, likes sync and Premium detection work as on desktop.
- **Shared code.** BotGuard request handling is now `src/botguard.mjs`, used by both apps.

## Android 0.1.0 — first build (in `mobile/`)

- **What it is.** Soncle on Android, sharing the desktop app's UI, audio engine and YouTube layer.
  See `mobile/README.md`.
- **What works.**
  - Home, Explore, Search, Library, albums, artists and playlists.
  - Lyrics, the queue, Flow radio.
  - The Sound page: EQ, AutoEq headphone correction, limiter and loudness.
  - Crossfades and gapless playback.
  - Background playback, with notification, lock-screen and headset controls.
  - Spotify/CSV import, backup and restore.
- **Not in this build yet.** Sign-in, downloads, local files.
- **Shared-code changes (desktop behaviour unchanged).**
  - `src/yt.mjs` runs in a browser.
  - `src/autoeq.mjs` takes a storage adapter.
  - Settings defaults moved to `src/defaults.mjs`.
  - The renderer accepts an app-supplied audio source (`api.srcFor`, `api.range`).
  - The renderer hides settings rows the phone can't use.

## 1.0.0 — first public release

Soncle's first public release: a YouTube Music player for Windows, focused on sound.

- **Sound**
  - True-peak limiter (−1 dBTP).
  - LUFS loudness normalisation (Quiet, Normal or Loud).
  - Gapless albums, and Smart mix: beat-timed crossfades with a bass swap.
  - A 10-band EQ, plus headphone correction for about 9,000 measured models (AutoEq).
  - A Sound page with live meters and an A/B "hold for original" button.
- **Flow radio**: radio queues ordered by tempo, key and energy, learned on your PC. Also "Made for
  you" mixes (time of day, On repeat, Rediscover, Discover).
- **Ctrl+K command bar**, local files, Spotify playlist import, Focus mode, smart ducking and a mini
  player.
- **Sign-in** happens in your own browser, which Google accepts.
- **Light**: about 0.1 % CPU when idle, and nothing at all after 12 s paused.
- **Installer**: choose where to install, no admin rights needed, Start menu and desktop shortcuts,
  and a proper uninstaller. Your settings are kept when you uninstall. A portable exe is also
  available.

Fixed since the last dev build:
- **Gapless handover** is now reliable. The next song is warmed up silently and started as far
  ahead as this PC needs, and it takes over before the old song's end is reported. Before, it
  could miss the moment and fall back to a normal load (a short gap), and in rare cases skip a
  song.

## Development history

Private builds made before the first release, while the app was still a port under another name.
Kept for reference; their version numbers are not related to Soncle's.

### Dev build 1.8.1

#### Fixed: "Couldn't sign you in — this browser or app may not be secure"
- Google blocks sign-in inside app windows, whatever they call themselves. Sign-in now happens in
  your own Chrome, Edge or Brave:
  - Soncle opens a separate window with a temporary profile on Google's sign-in page, and you sign
    in there as on any website.
  - Soncle notices when you're signed in, keeps the YouTube session and closes that window.
  - The temporary profile is deleted afterwards. Your normal browser profile is never touched.
- The browser is controlled over a private pipe, not a network port, and nothing is injected into
  the page. The page sees an ordinary browser (no automation flag); a test checks this.
- The in-app sign-in window remains only for PCs with none of those browsers.

#### Look and feel back to 1.7
- Icons are Material Symbols at weight 600, which matches 1.7's visual weight. 1.8.0's lighter
  icons made the whole app feel thinner.
- Outlined play, pause and skip controls, as in 1.7.
- Solid folder for Local files, a trend line for Stats, and centred lines for Lyrics, as in 1.7.
- The Now Playing title is one line again. The full title shows on hover.

### Dev build 1.8.0 — Soncle: a new name, a new audio engine, a Sound page, Ctrl+K

#### New name: Soncle
- **Why.** The app is now called Soncle. The maintainers of the Android app it was first ported
  from asked community clients not to use their name, so this project now uses its own, and it is
  independent and unofficial.
- **What changed.**
  - New name everywhere: window, tray, installer, backups (`soncle-backup-….json`) and the log
    (`soncle.log`).
  - New app id `com.minuda2009.soncle`.
  - New app icon: a play mark inside a circle of sound.
- **Your data comes with you.** On first start, the library, settings, downloads, caches, learned
  tempo/key data and headphone profiles are copied from the old settings folder once. The old
  folder is left as it was. If the saved sign-in can't be carried over, sign in again.
- **Old backups still restore.**
- **Icons.** Every UI icon now comes directly from Google's Material Symbols (Apache-2.0). The few
  custom drawings from the original app were replaced.
- **Discord Rich Presence** is off until Soncle has its own Discord application. The previous
  builds used the original app's.
- **Licence** is now declared as GPL-3.0-or-later.

#### Audio engine
- **True-peak limiter.** An AudioWorklet (−1 dBTP ceiling, 2 ms look-ahead, 4× oversampled peak
  detection) replaces Chrome's compressor. It is bit-transparent below the ceiling and costs about
  0.2 % of one core. Tests check that no inter-sample peak gets through.
- **Real loudness normalisation.** Songs are levelled in LUFS (EBU R128), with targets Quiet (−19),
  Normal (−14) or Loud (−11).
  - Quiet songs are now lifted too, since the limiter makes that safe.
  - YouTube's loudness data and ReplayGain are read correctly (they use different reference levels).
  - Songs with no loudness data are measured while they play; the value is remembered and eased in.
- **Gapless.** Album tracks, and every song when crossfade is off, hand over at the exact end of the
  previous song with a 12 ms overlap: no gap, no click.
- **Smart mix.** When two songs match on tempo and key (learned by Flow), the crossfade is timed to
  16 beats and the bass lines swap halfway, like a DJ blend. Songs that would clash get a short fade.
- **Headphone correction.** Search about 9,000 headphones and earphones measured by the AutoEq
  project and apply that model's correction under your own EQ.
  - When known headphones connect, the app offers their profile, e.g. "Headphones (WH-1000XM5
    Stereo)" → Sony WH-1000XM5.
  - Profiles are remembered per output device.
- **Neutral stages are skipped.** The EQ, correction, loudness compensation, width and crossfeed
  leave the signal path entirely when neutral.
- **No CPU while paused.** The audio thread goes to sleep 12 s after you pause or the queue ends.

#### Sound page (was Equalizer)
- Live meters: loudness (LUFS), true peak and limiter activity.
- "Hold for original" A/B button: hear the music without any of your sound settings.
- Headphone correction, loudness target, transitions (crossfade length, Smart mix / Classic,
  gapless albums) and the EQ, all in one place. The EQ graph also shows the headphone correction.

#### Ctrl+K command bar
- One box to play and skip, like, start radio or Focus, jump to any page, change loudness, EQ,
  crossfade or headphone correction, and find music. Your library shows up instantly and YouTube
  Music as you type. Enter plays; Shift+Enter adds to the queue.
- There's also a "Ctrl K" button in the search box.

#### Home and queue
- **Made for you** on Home, built on this PC:
  - a mix for the time of day ("Evening mix", in flow order);
  - On repeat;
  - Rediscover (favourites you haven't heard in a while);
  - Discover (a Flow radio of new songs).
- The queue shows how the next song will come in (gapless, Smart mix at 124 BPM, crossfade 6 s,
  short fade because of a clash). Clicking it opens the Sound page.
- Queue rows glide to their new place after a reorder.
- Long titles in Now Playing wrap to two lines instead of being cut off.
- Fixed: when the same song was in the queue twice, every copy showed as playing.

#### Lighter (same scripted session, same machine)
| | 1.7.0 | 1.8.0 |
| --- | --- | --- |
| Processes | 6 | 5: audio runs inside the main process, no spare renderer |
| Memory, all processes | ~555 MB | ~500 MB |
| CPU while playing | ~12.6 % | ~8 %, with the renderer's main thread 98 % idle |
| CPU while paused | audio thread kept running | 0 after 12 s |
- While playing, the player bar ticks 4× a second on a timer instead of every display frame, so
  Chromium's compositor can idle. The "playing" bars animate on the compositor, not with layout.
- The hidden BotGuard window (a whole browser process) closes after 10 minutes without a request
  and is rebuilt on demand in about a second.

#### Staying in step with upstream (UPSTREAM.md)
- **Weekly GitHub Action** that checks the upstream Android app and opens a pull request:
  - `po_token.html` is copied automatically;
  - changes to the Kotlin files this app was ported from are listed for porting.
- **Dependabot** brings youtubei.js updates. The baseline is upstream v13.7.0.
- **Ready for Kotlin Multiplatform.** Moved files are followed (`shared/src/commonMain/…`,
  `composeResources/`) and only real changes are reported. New desktop code upstream
  (desktopMain/jvmMain) is listed. This was tested on a simulated migration of v13.7.0's sources,
  and `--repo` follows a separate KMP repository.

#### Housekeeping
- **Credits.** The author credit (minuda2009) is in the code: a header on every source file, plus
  AUTHORS, README and package.json. The README says the code was written with AI coding agents to
  the author's design.
- **Notices.** THIRD_PARTY_NOTICES.md lists what comes from the upstream Android app (GPL-3.0), Material
  Symbols/Icons (Apache-2.0), AutoEq data (MIT) and the dependencies. It ships inside the app along
  with LICENSE.
- **Logging and lint.** No stray console.log: warnings and errors go to the log file only. Lint
  is clean (0 errors, 0 warnings).
- **Key comment.** The public BotGuard keys in potoken.mjs now have a comment saying where they
  come from.

### Dev build 1.7.0 — ducking, focus mode, no more getting stuck

#### Fixed: getting stuck
- A hung connection to YouTube no longer freezes playback: each chunk has a 20 s timeout, then it
  retries / switches client and continues from the same byte (tested: a request that never answers).
- Stall watchdog: if the song is buffering for 12 s it reloads at the same position with a fresh
  stream (up to twice), instead of spinning forever.
- Internet drops: instead of failing and skipping through the queue, it waits and resumes where it
  was when you're back online.
- Search and some pages failing with "reading 'url'": youtubei.js's own parser error handler crashed
  in the packaged app (it reads a package.json field the build strips) — replaced with a silent one.
  This also revives the TV stream client as a fallback.

#### Smart ducking (Windows)
- When another app makes sound — a YouTube video in the browser, a WhatsApp voice note, a call — the
  music is lowered (to 25 % by default) or paused, and comes back smoothly when it goes quiet.
  System sounds (notification dings) are ignored. Settings → Smart ducking: Lower / Pause / Off.
- Watches the other apps' audio sessions only while music is playing (a tiny helper process).

#### Focus mode (Ctrl+Shift+F or the Focus button)
- A zen full-screen view with just the song name and artist on a dark, slowly drifting glow. No
  artwork, no countdown, no lyrics, menus, pop-ups or keyboard shortcuts (only play/pause and skip).
  Moving the mouse briefly shows the controls, "Focus · until 5:00 PM" and End focus, which is the
  only way out; they fade again after a few seconds.
- Focus EQ preset (warm, softened presence), long 8 s smart crossfades, skip silence, normalisation,
  autoplay and skip-on-error on, crossfade on skip, no Discord status, non-essential toasts muted.
- Keeps the music going: short queues grow into a calm Flow radio (steady energy, ≤130 BPM, tighter
  tempo band).
- 25 / 50 / 90 min or no timer; optional pause at the end. Everything it changed is snapshotted first
  and restored when the session ends or is turned off — even after a crash or restart.

### Dev build 1.6.0 — lighter, smarter radio

#### Lighter (measured with the new metrics probe, same scripted session)
- Idle CPU ~1.5 % → ~0.1 %: no animation frames at all when nothing moves (they only run while
  playing, seeking or with Now Playing open), the 100 ms engine loop idles at 1 tick/s, and the
  renderer is throttled in the background whenever nothing is playing.
- Background playback ~6.6 % → ~5 %: the player-bar progress repaints 4×/s instead of every frame.
- Now Playing background is pre-blurred once on a 40 px canvas and upscaled by the GPU instead of a
  live 90 px blur over the whole window (the most expensive paint in the app).
- Long lists (history, big playlists, queue) skip layout/paint for off-screen rows.
- Occlusion detection is back on (Windows stops painting a covered window).
- music-metadata loads on first use of local files; the artwork cache is smaller; only the en-US
  Chromium locale is shipped (smaller download).
- Flow radio learning is nearly free: the song you're listening to is measured from bytes the player
  already downloaded (a tiny head+middle cache), 15 s excerpts instead of 30 s, only ~10 songs ahead
  are measured, and measuring pauses during crossfades and on battery power.

#### Smarter Flow radio
- Time of day, like Spotify: late night winds down (lower energy, mellower tempos), mornings ramp up,
  afternoons carry the most energy, weekends a touch more upbeat.
- Your taste, from this computer only: artists you play and like are favoured, songs you keep skipping
  (skipped in the first 30 s) sink, songs you just heard are held back, and familiar songs are mixed
  with discoveries (never 3 familiar in a row); a few liked songs from the same artists act as anchors.
- Energy is compared by rank among songs the app knows, so "calm" vs "upbeat" is meaningful.
- Much better key detection (high-resolution chroma from spectral peaks): on a 48-song synthetic
  sweep every key is now exact or its relative major/minor (was 25 % with a strong "D minor" bias).
  Keys learned by 1.5 are re-learned automatically.

#### Fixed
- "Cannot read properties of undefined (reading 'url')" on some pages: one malformed item or shelf no
  longer breaks a whole page; errors are logged with their stack.

### Dev build 1.5.1

- Full-screen animated welcome on a brand-new installation (Welcome → A quick note → Make it yours:
  sign in / add a music folder / import). Skip or Esc at any time; the app is ready behind it.
- Existing installations instead get the small one-time notice below.
- First-run notice (shown once per installation, after the UI has painted, never blocking):
  recommends the official music.youtube.com with YouTube Premium and says this client is an
  unofficial hobby project. This client plays through a third-party audio pipeline, so YouTube's
  ads can't be served in it and creators aren't paid through it — saying so up front is more honest
  than implying otherwise. The recommendation is a good-faith signal, not a legal shield. Signed-in
  Premium users (best-effort detection) only see the hobby-project line. Dismiss with a click, any
  key, Esc or the buttons; it also fades after ~9 s unless hovered or focused.

### Dev build 1.5.0 — Flow radio

#### Fixed
- Playback broke in 1.4.0 on some PCs ("ERR_FAILED loading" the BotGuard helper page): the BotGuard
  helper page now falls back to a data: URL with a youtube.com base (as Android's WebView does).

#### Flow radio (new)
- "Start radio" on a song, album, playlist, local playlist or artist builds a queue arranged for the
  crossfade: YouTube finds similar songs, the app orders them so the tempo stays in a comfortable
  band and keys stay harmonically compatible (Camelot wheel), with energy smoothing, a one-step
  look-ahead, no same-artist runs, and YouTube's relevance as the tie-breaker.
- Tempo, key and energy are measured on this computer from ~30 s of each song (WebM excerpts cut from
  the middle of the song via its Cues index, so only ~0.5 MB is fetched; local files are decoded
  directly). No external service. Results are remembered, so radios get better over time; songs
  that aren't analysed yet still take part using YouTube's order, loudness and title hints.
- The queue re-arranges itself as songs get analysed (never the song playing, nor the next one close
  to the transition); manual queue edits are respected. Autoplay grows the pool instead of appending.
- Crossfade length follows how well two songs mix (a clashing pair gets a short blend).
- Queue rows show "124 BPM · 8A"; Now Playing shows tempo and key; Settings → Flow radio, learning,
  and "Forget learned songs".

#### Motion
- New Settings → Motion style: **Classic** (default — the 1.3 feel: fade-up pages, round play
  button, glowing lyric line, artwork morph) or **Android** (the 1.4 Android-style motion).

### Dev build 1.4.0 — smoothness & consistency

#### Icons
- Every icon is now the actual drawable from the upstream Android app (Material Symbols + its custom
  icons, rendered with their own viewBox, strokes and fill rules) — previously only "close" matched.
- One icon per action everywhere (e.g. Add to queue = queue_music, Remove download = offline,
  Go to artist = artist, Follow = subscribe, Sleep = bedtime, Lyrics offset = fast_forward,
  Clear cache/search history = clear_all, Clear listening history = delete_history…), settings rows
  use the same icons as the Android settings screens, and the selected sidebar destination switches
  to the filled icon like Android's navigation bar.

#### Lyrics (matches Android's "experimental lyrics")
- Lines glide individually to their new place (750 ms fast-out-slow-in, staggered 20 ms per line);
  the active line sits at 35% height; other lines fade by distance; no blur/scale.
- Word-by-word karaoke fill on the active line (real word timings from enhanced LRC, or Android's
  quick synthetic sweep), with the subtle word-start "wobble"; instrumental gaps show a progress ring.
- Scroll/drag the lyrics freely: auto-scroll pauses and an "Auto scroll" pill brings it back
  (tapping a line also seeks and resyncs), instead of snapping back after 3 s.

#### Motion
- Now Playing opens/closes like Android's bottom sheet (spring) with the artwork morphing from the
  player bar; the artwork slides left/right on next/previous; background crossfades over 800 ms.
- Play / previous / next are Android's three pills with the springy re-flow on press.
- Pages slide ±1/8 with a 200 ms fade (direction follows back/forward); the queue slides in and out.

#### "What's playing" everywhere
- The playlist/album/artist/liked/local page you're playing from shows Pause instead of Play, and its
  cover (cards, library, sidebar, page header) shows animated playing bars.
- Now Playing shows "Playing from …" (click to open it); the queue's "Next from" links there too.
- Album/playlist download button shows "offline" when everything is already downloaded.
- Undo after removing a song from a playlist or the queue; F5 refreshes the current page.
- "1 song" instead of "1 songs"; About text notes this is a community port.

### Dev build 1.3.0 — sound

#### Crossfade
- **Smooth curve from the upstream Android app** (now the default): the next song rises fast
  (1 − (1 − x)²) while the old one clears out early ((1 − x)²), so the overlap never gets muddy.
  Equal-power and linear are still available (Settings → Crossfade style).
- **Smart crossfade**: follows the song's loudness and starts the blend when the outro really
  fades out or goes silent (instead of blindly at "length − crossfade"), and jumps over silence at
  the start of the incoming song.
- Crossfade length follows the tempo setting (like Android); gapless-album detection works for
  local files too.

#### Sound quality
- Clip protection: the EQ preamp automatically drops by the curve's biggest boost, so boosting
  never distorts; the equalizer shows the headroom in use.
- The always-on compressor was replaced by a transparent peak limiter (hard knee at −0.5 dBFS,
  make-up gain cancelled): quieter than 0 dBFS passes bit-for-bit level-accurate, overs no longer
  clip (the old one let +3 dB inputs out at +1.3 dBFS).
- Loudness compensation (optional): adds back bass and treble as you turn the volume down.
- Stereo width (0–160%) and headphone crossfeed (off / light / strong, bs2b-style).
- Sound settings are remembered per output device along with the EQ.
- Now Playing shows what is playing: codec, bitrate, source and output sample rate.

### Dev build 1.2.0

#### New
- **Local files.** New "Local files" page: add music folders (MP3, FLAC, M4A/AAC, OGG, Opus,
  WAV, WMA, AIFF…), browse by song / album / artist, filter, play, shuffle, add to playlists and
  the queue, like. Tags, embedded cover art (or cover.jpg / folder.jpg), ReplayGain for
  normalisation, and lyrics from a matching `.lrc` file or embedded tags. Folders are rescanned
  on start (unchanged files are cached). "Start radio" on a local song continues from YouTube Music.
- **Drag & drop** audio files or folders onto the window to play them (Shift = add to queue);
  "Open files…"; files passed on the command line / "Open with" play too.
- **Import from Spotify.** Paste a public Spotify playlist, album or song link (in the search box
  or the new Import button) — no login needed, playlists over 100 songs are paged in full. Or
  import a CSV export (Exportify etc., for Liked Songs / private playlists) or a plain
  "Artist - Title" list. Every song is matched on YouTube Music (title, artist and duration
  scoring); review the matches, untick wrong ones, then create a playlist or just play.
- Search also shows matching songs "On this computer" (and still works offline for them).

#### Fixed
- Songs skipping after ~15 s / ~1 min (stream proxy now serves one continuous response). Downloads
  use the same self-healing chunked fetch.
- Tempo reset to 1.0× on every new song.
- Turning the sleep timer off during its fade-out (or touching volume/mute) left the app silent.
- Crossfade races: rapid Next presses, a click on another song being overridden, pausing while the
  next song was buffering, and the old track ending before the crossfade started (song restart).
- A failing song was reported three times and auto-skip gave up after two bad songs; retries could
  load the wrong song if you had moved on.
- A resume position could be applied to a different song.
- Autoplay no longer appends radio songs into a queue you just replaced.
- Space toggles play/pause even after clicking a button (it used to re-press that button).
- Shift+[ / Shift+] lyric offset shortcuts.
- Dragging between the queue and a playlist moved the wrong item.
- Discord Rich Presence works out of the box (used the upstream app's Discord app id; removed in 1.8.0).
- Stream cache respects the audio quality setting; search continuation memory is capped.
- Restoring a backup applies language/region and other settings immediately; last session is saved on quit.
- Lyrics load for a restored session; stale resume positions are cleared.
- Pasted `browse/VL…` playlist links open the playlist; "Press Enter to open this link" hint works.
- Song-filter search no longer shows albums/artists as playable song rows.
- Errors from menu actions and buttons are shown instead of silently doing nothing.
- Deleted download files can be downloaded again; cancelled downloads no longer reappear.

### Dev build 1.1.4

- Playback: fixed songs stopping after ~10 seconds and skipping to the next one. The stream
  size is now always known (format length → `clen` → asked from googlevideo), so the player
  no longer mistakes the first chunk for the whole song and crossfades early.
- Chunks are 1 MB (fewer requests); if the size is unknown the stream is not chunked.
- A 403 mid-song re-resolves and switches client instead of failing.
- Early song endings, upstream errors and renderer errors are written to the log file.

### Dev build 1.1.3

- Playback: fixed "Could not load a playable stream. IOS: range probe failed". Streams now
  come first from the YouTube Music web client with BotGuard PO tokens (the same approach as
  the upstream Android app), falling back to IOS, ANDROID_VR, WEB+PO token, TV and WEB_EMBEDDED.
- Uses real visitor data (not locally generated) so PO tokens bind to the session; old
  session cache is ignored.
- Error toast shows every client that was tried, not just the first.
- Playback diagnostics are written to a log file in the app's settings folder.

### Dev build 1.1.2

- Home: reaching the end of the feed no longer logs `Continuation did not have any
  content`; infinite scroll now stops quietly.
- Audio devices: Windows labels with several bracket groups are parsed correctly.
  `Headphones (LP-V53) (Bluetooth)` is now model `LP-V53` (was `LP-V53) (Bluetooth`),
  which also fixes per-device EQ/volume profiles being saved under a broken name.
  Nested brackets (`Realtek(R) Audio`) and USB ids (`(0d8c:0014)`) are handled too.
- Tests for both.

### Dev build 1.1.1

Fixes across playback, search, security and packaging. Source is now tracked
directly (previously shipped only as a committed zip).

#### Playback (songs cutting off / jumping mid-song)

The root cause: the stream client chosen first (`ANDROID_VR`) serves a
`googlevideo.com` URL that rejects any byte range whose end crosses roughly
1 MB (about 60 seconds). Playback stalled there, and the retry re-resolved the
same capped client, so tracks died part-way through or resumed at the wrong
offset.

- Prefer clients whose URLs allow arbitrary offsets (`IOS` first). Verified:
  `ANDROID_VR` 403s at a 60 s window and at every offset past ~60 s, while `IOS`
  serves the full file and mid-file ranges.
- Probe a **mid-file** range before selecting a client (was only `range=0-1`).
- On 403/410, rotate to a *different* client instead of re-resolving the same one.
- Cap proxied range width to 256 KB and return `416` for ranges past the end.
- Strip the ignored `?r=` cache-buster in the `mstream:` handler.
- De-duplicate concurrent resolves; bound the stream cache.
- Mirror audio into the analyser without routing the audible output through it.

#### Search and content parsing

- Search returned zero results for All/Songs/Albums: YouTube Music returns
  `ItemSection -> MusicResponsiveListItem`, which the parser dropped. Sections
  are now unwrapped and merged into one list. (All: 0 -> 20 items.)
- `type:'song'` and `type:'album'` are rejected by the API for some responses;
  the app now falls back to a client-side filtered `all` search.
- Read artist/album navigation from `.endpoint` or `.navigationEndpoint`.
- Parse durations on two-row song cards (were always 0).
- Artist pages: detect top-songs/albums sections.
- `album()` is resilient (raw `/browse` fallback) and throws only when genuinely
  empty, so albums open instead of showing an error box.
- `txt()` now handles `{ runs: [...] }` shapes.

#### Renderer

- Manual Next at the end of the queue no longer wraps to track 0 when repeat is off.
- Listening stats no longer credit a flat 180 s for tracks with unknown duration.
- Bounded LRU for the lyrics and colour caches; removed dead state.
- Keyboard shortcuts no longer hijack Space/letters while a button has focus and
  leave OS/browser shortcuts alone.
- Debounced library writes and the queue session are flushed on unload.
- Pasting a link no longer auto-plays; Enter opens it.
- Disabled setting rows no longer toggle; auto-download on like moved client-side.

#### Main process and security

- macOS now has an application menu, so ⌘C/⌘V/⌘A/⌘Q work again.
- Downloads honour the quality setting and handle unknown content length.
- Sign-in cookies are encrypted at rest with Electron `safeStorage`.
- DevTools is disabled in packaged builds.
- Media permission handlers deny everything except output-device selection.
- Discord Rich Presence requires an explicit client id and only forwards artwork
  from known YouTube image hosts (previously used the upstream app's id).
- CORS is relaxed only for YouTube artwork hosts.

#### Packaging

- Replaced a committed source zip with the source tree.
- Added `README.md`, `LICENSE` (GPL-3.0), `eslint.config.mjs`, unit tests
  (`node --test`), a CI workflow, and `dist:*` build scripts for win/linux/mac.

#### Testing

- 15 unit tests covering device classification and the yt.mjs parse helpers.
- Lint: 0 errors.
- Headless Electron run of every view and the audio protocol: no renderer errors.
