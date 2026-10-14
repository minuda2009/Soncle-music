<p align="center"><img src="assets/soncle-icon.svg" width="120" alt="Soncle"></p>

# Soncle

A desktop YouTube Music player, built for people who care how it sounds.

True-peak limiting, EBU R128 / LUFS loudness, gapless albums, DJ-style transitions and measured
headphone correction (AutoEq). Built with Electron and [youtubei.js](https://github.com/LuanRT/YouTube.js).

> Soncle is an independent, unofficial project. It was inspired by and originally ported from
> [Metrolist](https://github.com/MetrolistGroup/Metrolist) for Android, and is not affiliated with
> or endorsed by Metrolist Group, Google or YouTube.

## Download

**[Latest release →](https://github.com/minuda2009/Soncle-music/releases/latest)** has everything
you need:

| You want | Get this file |
| --- | --- |
| Windows, installed (Start menu, uninstaller) | `Soncle-Setup-<version>.exe` |
| Windows, no install | `Soncle-Portable-<version>.exe` |
| Android phone | `Soncle-Android-<version>.apk` |

Windows builds are not code-signed yet, so SmartScreen may say "Windows protected your PC". Choose
**More info → Run anyway**. The installer lets you pick the folder and needs no admin rights;
uninstalling keeps your library and settings (delete `%APPDATA%\Soncle` to remove them too).

Signing in is optional. When you click *Sign in*, a separate Chrome, Edge or Brave window opens on
Google's sign-in page, because Google does not allow sign-in inside apps. Soncle picks up the
session and closes that window. Your normal browser profile is not touched.

### Test builds (not the stable release)

Built automatically while the app is being worked on. They can be broken, and each one replaces the
previous.

| Release | What it is |
| --- | --- |
| [android-latest](https://github.com/minuda2009/Soncle-music/releases/tag/android-latest) | Newest Android build from `main` |
| [android-preview](https://github.com/minuda2009/Soncle-music/releases/tag/android-preview) | Android build of the branch being tested ("Soncle Preview", installs next to the normal app) |
| [windows-preview](https://github.com/minuda2009/Soncle-music/releases/tag/windows-preview) | First build of the new C# Windows app (shell only so far) |

## Where the project is

- **Today's apps:** the Windows desktop app (Electron, this folder's root) and the Android app
  (`mobile/`, Capacitor, reusing the desktop's UI and code). Both are being maintained.
- **Next:** a rewrite in C# on the Uno Platform (`uno/`), one app for Windows and Android that
  uses less memory. It is not usable yet; the plan and progress are in
  [docs/WINUI_PLAN.md](./docs/WINUI_PLAN.md), the roadmap in [ROADMAP.md](./ROADMAP.md).
- What changed and when: [CHANGELOG.md](./CHANGELOG.md).

## Author

**[minuda2009](https://github.com/minuda2009)**. The ideas, feature design, direction and real-world
testing are mine.

**How it was built:** the code was written with AI coding agents (Anthropic's Claude), working to my
specifications and feedback over many rounds of testing on my own PC. I decided what the app should
do and how it should feel. The agents wrote, tested and refactored the code. Every change was run
against the test suite, lint and scripted headless runs before a build went out.

## What it does

- **Plays reliably.** Audio goes through a small local proxy (`mstream://`) that streams continuously,
  times out and retries hung chunks, switches stream clients on 403s, and resumes after network drops.
- **Sound.**
  - A true-peak limiter (AudioWorklet, −1 dBTP).
  - LUFS loudness normalisation with Quiet, Normal and Loud targets.
  - Gapless playback.
  - Smart mix: beat-timed crossfades with a bass swap.
  - A 10-band EQ, plus headphone correction for about 9,000 measured models (AutoEq).
  - Live meters and an A/B "hold for original" button.
- **Flow radio.** Orders a radio queue so tempo, key and energy move smoothly. It also adapts to
  time of day and to what you play, like and skip. The analysis runs on-device (see below).
- **Made for you.** Mixes for the time of day, On repeat, Rediscover and Discover, all built on
  your PC.
- **Ctrl+K command bar.** Play, jump anywhere, change sound settings, or find music.
- **Local files and Spotify playlist import.**
- **Smart ducking (Windows).** Lowers the music while another app plays sound.
- **Focus mode.** A quiet full-screen view with just the song name, a Focus EQ, and your settings
  restored afterwards.
- **Light.** About 0.1 % CPU when idle, about 8 % while playing, and nothing at all 12 s after you
  pause. Measured with `SONCLE_METRICS=1`.

### On-device tempo and key without downloading songs

YouTube serves audio as DASH WebM. `renderer/flow/webm.js` reads the file's Cues index and
range-requests the header plus a few whole Clusters from the middle of the song, a few hundred KB in
total. Those bytes are rebuilt into a small valid WebM that `decodeAudioData` accepts.
`renderer/flow/analyze.js` is pure JS with no external service or API key. It runs in a Worker and
finds:

- **tempo**, from onset-strength autocorrelation;
- **key**, from a spectral-peak chroma matched to Krumhansl–Kessler profiles and converted to Camelot
  notation;
- **energy**.

For the song you're listening to, it reuses bytes the player already downloaded.
`renderer/flow/flow.js` (`planFlow`) then scores transitions with a weighted Camelot and tempo model,
shaped by time of day and your taste. When it has no data it falls back to YouTube's own order.

## Develop

Requires Node.js 20+.

```bash
npm install
npm start               # run the app
npm test                # unit tests (node:test)
npm run lint            # eslint (0 errors, 0 warnings)
npm run dist:win        # installer + portable exe (x64)
# stable releases are built by the Release workflow, see docs/RELEASING.md
npm run upstream:check  # what changed upstream since the last sync (see UPSTREAM.md)
```

The test suite runs with `npm test`. Some tests are skipped automatically without their fixtures: the local-files
test needs a sample music folder, and the sign-in test needs `SONCLE_SIGNIN_BROWSER` (a Chromium-based
browser) and a display. The tests cover:
- the limiter: transparent below the ceiling, and no true-peak overs on hot material;
- the loudness meter: reads a −23 LUFS reference tone;
- tempo and key detection: 128 BPM ± 2 and Camelot 8A on a synthetic groove;
- the one-time data carry-over from the old name.

Windows builds are unsigned, so expect a SmartScreen warning.

### Environment variables

| Variable | Purpose |
| --- | --- |
| `SONCLE_DISCORD_CLIENT_ID` | Discord application id for Rich Presence. Rich Presence is off when unset. |
| `SONCLE_DEVTOOLS` | Enable F12 devtools in a packaged build. |
| `SONCLE_METRICS=1` | Log CPU/memory every `SONCLE_METRICS_MS` ms (default 10000). |
| `SONCLE_MOCK` | Path to a module exporting a youtubei.js client double, for offline UI work (not included). |
| `SONCLE_FAKE_AUDIO` | Play a local file for every track (development). |
| `SONCLE_SHOT`, `SONCLE_SCRIPT`, `SONCLE_WAIT`, `SONCLE_PROFILE` | Headless screenshot/script/profiling harness used for testing. |
| `SONCLE_SIGNIN_BROWSER` | Browser to use for sign-in (and the sign-in test) instead of the one found automatically. |
| `SONCLE_EMBEDDED_SIGNIN` | Use the in-app sign-in window instead of a browser. |

## Architecture

- `src/main.mjs`: Electron main process (window, tray, IPC, downloads, sign-in, lyrics, the
  `mstream:` protocol).
- `src/legacy.mjs`: the one-time carry-over of settings and data from the app's previous name.
- `src/streamproxy.mjs`: the continuous, retrying range proxy and its small chunk cache.
- `src/yt.mjs`: all YouTube Music access, normalised to plain JSON.
- `src/potoken.mjs` + `src/po_token.html`: PO tokens from BotGuard in a hidden window.
- `src/autoeq.mjs`: headphone correction profiles (search, match to your device, cache).
- `src/browser-signin.mjs`: Google sign-in in your own browser, with a temporary profile.
- `src/local.mjs`, `src/spotify.mjs`, `src/ducking.mjs`, `src/discord.mjs`: local library, playlist
  import, Windows audio-session ducking, and Rich Presence.
- `renderer/app.js`: the UI. `renderer/engine.js`: the Web Audio engine.
  `renderer/audio/worklets.js`: the limiter and loudness meters. `renderer/flow/`: Flow radio.
- `mobile/`: the Android app. `mobile/src/backend.js` provides the same `window.api` on the phone.
- `tools/`: the upstream sync, the icon generator (`make-icon.mjs`) and icon sources.
  `build/`: installer artwork, and the step that stamps the icon and version onto `Soncle.exe`.

## Privacy and security

- Sign-in is optional. Cookies are encrypted at rest with Electron `safeStorage`, never sent to the
  renderer, and never written to the log.
- The renderer runs with `contextIsolation`, `sandbox`, no Node integration, and a strict CSP.
- Listening history and taste data stay on this computer.

## Contributing

Issues and pull requests are welcome. Please run `npm test` and `npm run lint` before opening one.

## License

GPL-3.0-or-later, see [LICENSE](./LICENSE). Third-party code, icons and data, and the credit to the
project Soncle grew out of, are in [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md).
