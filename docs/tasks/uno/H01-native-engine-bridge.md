# H01 — Native audio engine behind the web UI (hybrid Windows app)

Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later

**Goal.** In the hybrid Windows app (ROADMAP.md, "Plan: the hybrid Uno app"), songs can play
through the C# engine (`Soncle.Audio` on WASAPI) instead of Web Audio in WebView2, behind a
Settings switch **"Native audio engine (beta)"** (Settings → Sound, level 2). Off by default;
the JS engine stays the default until this passes the checks below.

## What exists

- C#: `Soncle.Audio` (decks, crossfade styles and curves, gapless, 10-band EQ + preamp, width,
  crossfeed, mono/balance, bass shelf, normalization to a LUFS target, true-peak limiter last),
  `Soncle.Audio.Opus` (WebM/Opus decode), `Soncle.Streams.SongDownloader` (range downloads,
  start while downloading), `Soncle.App.EnginePlayer`, `WasapiAudioOutput`, `VolumeOutput`.
- Web UI: `renderer/engine.js` (`Engine`), used by `renderer/app.js` in ~40 ways, including
  `engine.el` (the `<audio>` element) and `engine.ctx` (meters, visualizer).
- Host bridge: `mobile/src/desktop/core.js` (plugin calls + events), `Web/WebShell.cs`.

## Design

1. **`renderer/engine-native.js`**: a class with the same surface as `Engine` that forwards to the
   host (`Soncle.engine*` calls) and keeps a local mirror of state from host events
   (`engineState` at 10 Hz while playing: position, duration, paused, xfading, levels).
   - `load/play/pause/seek/crossfadeTo/gaplessTo/cancelCrossfade/fadeOutAll` → host.
   - `setOptions/setSound/setEq/setChannel/setBypass/setDuck/setVolumeLevel` → host (`MasterChain`).
   - `el`: a stub object with `currentTime`, `duration`, `paused`, `playbackRate`, `buffered`,
     `addEventListener` for the few direct uses; events re-dispatched from host state
     (`play`, `pause`, `timeupdate`, `ended`, `error`, `waiting`, `playing`).
   - `ctx`/meters/visualizer: `setMeters(cb)` fed from host level events (peak, RMS, LUFS-S per
     deck); the visualizer falls back to its idle state.
   - `setCorrection` (AutoEq): host gets the filter list; `MasterChain` gains a correction
     biquad stage before the EQ (port of `engine.js` `setCorrection`, same coefficients).
2. **Streams**: the page resolves the stream as today (`yt.resolveStream` with PO tokens) and
   hands `{ id, url, headers, length, mime, lufs }` to the host; the host's `SongDownloader`
   fetches it (connect like the relay: direct, IPv4 first) and `StreamingOpusSource` plays it.
   Expired URLs: host asks the page to re-resolve (event `streamExpired`, as on Android).
3. **Local files**: Opus/WebM local files go native; other codecs (MP3, FLAC, AAC) stay on the
   JS engine for that song until a native decoder lands (Media Foundation is the candidate);
   the switch between engines happens only at song boundaries, never mid-crossfade.
4. **Not native yet → JS engine**: varispeed/pitch, skip silence, A/B bypass compare. When the
   user turns one of these on, the app says so and uses the JS engine for that song.
5. **Device**: WASAPI default device (shared mode) to start; `setSink` maps Chromium device
   labels to WASAPI endpoints by name. Exclusive mode later.

## Checks (all before the switch can default to on)

- Same reference numbers as the JS engine: no true-peak overs (≤ −1 dBTP), −14 LUFS target on
  the fixtures, EQ/AutoEq responses match `engine.js` within 0.1 dB at the 10 band centres.
- Crossfade, gapless, beat-timed transitions and Flow radio work across 20 songs with no clicks.
- Measured CPU and memory while playing and idle, JS engine vs native, written in the PR.
- Media keys, seeking, pause on device removal, resume after sleep.

## Effort

About 1–2 focused sessions, with minuda2009 listening between them.
