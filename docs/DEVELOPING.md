# Developing Soncle

How the code is laid out, how to run it, and how some of the cleverer parts work. The agent rules
(branches, tests, UI) are in [AGENTS.md](../AGENTS.md); the C# rewrite is in
[WINUI_PLAN.md](./WINUI_PLAN.md).

## Run and test

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

## How Flow analysis works

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

## Contributing

Issues and pull requests are welcome. Please run `npm test` and `npm run lint` before opening one.
For anything that changes how the app looks, include a screenshot.
