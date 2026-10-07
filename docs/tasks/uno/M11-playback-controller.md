# M11 — `PlaybackController`: the player's rules as a tested state machine

_Branch: `openhands/uno-m11-playback`. Needs: M02, M10. Read `README.md` in this folder first._

## Goal
Port the player section of `renderer/app.js` (≈ lines 2025–2840) into a UI-free
`PlaybackController` in `Soncle.Core` or `Soncle.App.Logic` (a plain library, not Uno). Every
rule learned the hard way in Android 0.1.2–0.1.6 becomes a unit test, so it can't regress.

## Inputs
- `renderer/app.js`, the player section:
  - queue and `P` state;
  - `playAt`, `loadTrack` (`looking`/`holdStart`), `togglePlay`, `next`/`prev`,
    `prefetchNext`;
  - `transitionPlan` usage, `gaplessInto` (`giveUp`, `_noGapless`);
  - `handlePlayError` and auto-skip;
  - `armStallWatch` (`bufferedEnd`, `waited`, phone 8 vs desktop 2 stall checks), `reloadAt`,
    `waitForNetwork`;
  - the media-session handlers (play only plays, pause only pauses), sleep timer, repeat and
    shuffle, no duplicates, persistent queue.
- `renderer/engine.js`: the `play()` AbortError retry and `wantPause`.
- `CHANGELOG.md`, the Android 0.1.2–0.1.6 entries: the "why" behind each rule.
- `docs/WINUI_PLAN.md` §4, the "Playback rules" block, and §9.

## Build
- **`PlaybackController`.** Its dependencies are interfaces only:
  - `IStreams` (prefetch/resolve);
  - an `IPlayer` abstraction of the engine (load, play, pause, seek, events: playing, paused,
    waiting, ended, error, buffered);
  - `IClock` and `INetworkStatus` (online/offline, metered);
  - `IStore` (session, settings).
- **Commands:** `PlayList`, `PlayAt`, `TogglePlay`, `Play`, `Pause`, `Next`, `Prev`, `Seek`,
  `SetShuffle`, `SetRepeat`, and the system media actions.
- **Observable state:** current track, playing, loading, `holdStart`, queue, errors.
- **Platform differences are settings,** not `if (android)` code. The phone/desktop stall
  allowance (8 vs 2) and the reload policy (a phone reload keeps the download) are injected
  through an options object.

## Acceptance (unit tests with fakes and a fake clock; each named after the rule)
- Pause pressed while a song is being looked up holds the start. Play during the hold resumes it.
  After the load the hold is cleared.
- A system "play" while already playing does nothing; a system "pause" while not playing does
  nothing. Neither ever toggles.
- An unrequested pause while starting (AbortError) is retried once. A requested pause is never
  retried.
- The stall watch:
  - waits while buffered data grows, up to 60 s;
  - reloads after 12 s with no growth;
  - gives up after 2 checks (desktop) or 8 (phone) with "The stream stopped responding";
  - waits for the network instead of erroring when offline.
- Auto-skip on error: at most 3 in a row, then it stops.
- Gapless/crossfade decisions follow `TransitionPlan` (M10). If gapless can't be set up in time,
  playback falls back to a normal load without skipping a song (the earlier double-skip bug).
- No-duplicates, repeat one/all, shuffle and restore all match the JS behaviour on a scripted
  scenario. Export the scenario's expected event log from a JS run into
  `fixtures/playback/*.json` (add a small JS harness in `tools/fixtures/`).
- **Ledger:** the app.js player section ported.

## Out of scope
UI, real audio output, the Flow radio service (later).
