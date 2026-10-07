# Roadmap

## Next version: beat-aware transitions ("Smart mix 2")

These are committed for the next release, on top of whatever else that release asks for. The goal
is transitions that sound mixed by a person: in time, on the phrase, in key. Every item keeps the
current rule: when the app isn't confident, it falls back to today's behaviour instead of guessing.

### 1. Beat and downbeat phase from the onset envelope
- **Today.** Flow analysis measures tempo from a 15 s excerpt in the middle of the song, but not
  where the beats fall.
- **Next.** Analyse two more excerpts, each fetched through the WebM Cues index like today, so it
  is still a few hundred KB per song:
  - the intro (first ~30 s);
  - the outro (last ~45 s).
- **Beat phase.** Comb-filter / dynamic-programming beat tracking on the onset-strength envelope
  gives a beat grid (times of each beat) plus a confidence.
- **Downbeat.** Choose the bar position (1 of 4) whose beats carry the most low-frequency onset
  energy and harmonic change (chroma novelty).
- **Stored per song:** `beats: { intro: [t0, period], outro: [t0, period], conf }` and
  `downbeat: { intro, outro, conf }`.

### 2. Beat-phase alignment
- Start the incoming song so its first strong beat lands exactly on a beat of the outgoing song:
  schedule `play()` using the measured start-up delay (as the gapless handover does now), then
  correct any leftover offset of a few ms by nudging the playback rate for a moment.
- **Accept when** beat onsets of both songs line up within ±10 ms (±20 ms at the edges) in a
  synthetic test with click tracks.

### 3. Tempo nudge
- When the two tempos are close (within ~6 %), bring the incoming song to the outgoing tempo during
  the blend with `playbackRate` and `preservesPitch`, then glide back to 1.0 over 2–4 bars.
- The listener shouldn't notice; the drums shouldn't flam.
- Hard limit ±6 %. Beyond that, no nudge and a shorter fade.
- Half-time and double-time pairs (e.g. 85 ↔ 170 BPM) are treated as matching.

### 4. Phrase and bar awareness
- Music moves in 4-, 8- and 16-bar phrases. Find phrase boundaries in the outro from the downbeat
  grid plus an energy/timbre novelty curve (where the section changes).
- Blends start on a phrase boundary and last a whole number of bars (8 or 16), so the swap lands on
  a downbeat.

### 5. "Best place to crossfade"
- Pick the exit point in the outgoing song and the entry point in the incoming one, rather than a
  fixed "last N seconds".
- **Exit:** the last phrase boundary before the energy drops away, the vocals end or the song
  starts fading.
  - Vocal presence is estimated from mid-band (300 Hz–3 kHz) harmonic energy and spectral flatness.
  - This replaces today's "level drops 22 dB below average" rule when confidence is high.
- **Entry:** skip a long ambient intro to the first downbeat of the first full phrase with drums,
  if the intro would otherwise sit under the outro for too long.
- Never cut into a vocal line on either side. Candidate pairs are scored and the best one wins.

### 6. Key matching
- Today Camelot compatibility already shapes Flow order and chooses mix vs short fade.
- **Next:**
  - Let the key score also pick the blend style. Compatible keys get a long melodic overlap;
    clashing keys get a percussion-only overlap (the incoming song high-passed/filtered until the
    swap) or a quick cut on the downbeat.
  - Where a tempo nudge happens anyway, prefer nudges that keep pitch (`preservesPitch`).
  - No pitch shifting of whole songs.

### 7. Also in scope
- **EQ blend styles.** Besides the bass swap: a high-pass sweep on the outgoing song, and a
  filter-in for the incoming one, chosen by energy and genre (energy + spectral profile).
- **Vocal-clash guard.** If both songs have vocals in the overlap, shorten it to the instrumental
  part.
- **UI.** Show the plan in the queue chip, e.g. "Beat-matched mix · 16 bars at 124 BPM · 8A → 9A".
  The Sound page keeps a Smart / Classic switch.
- **Tests.**
  - Click-track and synthetic-groove fixtures for beat phase and downbeat.
  - A two-track render in node that asserts beat alignment within tolerance.
  - The existing gapless and transition scenario runs in the app.

### 8. Fix: Flow radio stops analysing after ~10 songs
- **Reported.** Starting a Flow radio, analysis halts after about 10 songs.
- **Cause.** This is a limit set in 1.6.0 to save resources, not a crash (`renderer/app.js`:
  `startFlowRadio` → `ensureFeatures([seed, ...pool.slice(0, 10)])`; afterwards only the next 3
  songs are added per track, via `afterStart`). A radio pool is usually 40–60 songs, so most are
  never measured. The 10 are taken in YouTube's order rather than the planned order, so Flow orders
  the radio knowing only a small slice of it.
- **Fix.**
  - Keep analysing the whole pool in the background: one song at a time, low priority, never
    during a blend, paused on battery (as today).
  - Order the work by what the planner needs next: the candidates for the next 2–3 slots first,
    then the rest.
  - Re-plan as results arrive (already happens).
  - Songs that fail get one retry later, not a permanent skip.
  - Show honest progress in the queue's Flow note, e.g. "Learning the radio · 23 of 48 songs".
  - Stop when the pool is done. Cost is a few hundred KB of audio per song, and results are saved,
    so each song is only measured once.
- **Why all of them.** "Best next song" is only the best of what has been measured. With 10 of ~50
  measured, a better match can sit unmeasured in the pool, ranked only by YouTube's order. So:
  - **Measure the whole pool fast.** Keep one job running back to back while the radio is new, not
    idle-time only. At a few seconds per song, a 50-song pool should be done within about the
    first one or two songs. Afterwards it drops back to low priority for songs added later.
  - **Decide late.** Don't fix the whole order when the radio starts. Commit only the next song,
    about 30 s before the current one ends, choosing from every candidate measured by then. The
    queue further ahead is a preview that can still change.
  - **Be honest while data is partial.** Until the pool is done, the pick is the best of what's
    measured so far. Unmeasured songs don't win just by their YouTube position, and the queue's Flow
    note says the choice was made from partial data.
- **Accept when**
  - a 50-song radio is fully measured within the first two songs, with no stall and no gap or
    glitch in playback;
  - from then on, every next pick is made with the whole pool measured (logged, and checked in a
    scenario test).

### 9. EDM options (asked for 4 Oct 2026: "all of them")
Three separate switches, all off by default, all on the Sound page. Each follows the house rules:
nothing gets louder just by being switched on (the comparison is loudness-matched), the limiter
stays last, and a low-confidence analysis means the effect simply doesn't happen.

**9a. EDM sound style ("Club").** A sound style next to the EQ, not a replacement for it.
- Chain, placed after headphone correction and before the limiter:
  - high-pass at 25 Hz (rumble that only eats headroom);
  - sub-bass low shelf +5 dB at 50 Hz, a small dip (−1.5 dB) around 300 Hz to keep it clean,
    +1 dB presence at 3 kHz, +2 dB air at 10 kHz;
  - **bass kept mono below 120 Hz** (mid/side), stereo width 1.2× above 200 Hz only;
  - a gentle "glue" compressor (2:1, slow attack ~25 ms, release ~120 ms, about 2 dB of reduction)
    for punch.
- Make-up gain is set so the style is A/B-fair at the same loudness, and the auto-headroom rule
  covers the boost.
- **Auto** option: apply only to songs Flow has measured as high-energy (energy and tempo), plain
  for the rest.
- On a phone speaker (see "Android next") the sub-bass boost is turned down automatically; the
  speaker can't play it and it only causes distortion.
- Accept: no true-peak overs on the test signals; loudness within 0.5 LU of the style switched
  off; CPU cost measured (target: under 1 % extra while playing).

**9b. DJ-set Flow radio.** A "Set" mode for Flow radio: Chill / Steady / **Peak (EDM)**.
- Needs items 1–5 (beat phase, alignment, tempo nudge, phrases, best crossfade point).
- **Energy arc.** The planner orders songs to build up, peak and come down over about 45 minutes,
  instead of only picking the best next match. Peak mode prefers 120–130 BPM and compatible keys.
- **Blends like a set.** 16- or 32-bar beat-matched blends; a high-pass sweep on the outgoing song
  through its last phrase (the build-up feel, from item 7's EQ blend styles); the swap timed so the
  incoming song's drop or first downbeat lands on the outgoing song's phrase boundary.
- **Drop detection** as an extension of item 5: a breakdown (energy falls) followed by a jump in
  the onset envelope marks a drop; the planner can enter a song just before its drop.
- The queue chip shows the plan, e.g. "Peak set · into the drop at 1:04 · 126 BPM · 8A → 9A".
- Accept: blends start on phrase boundaries in the click-track and synthetic-groove tests; the
  set's energy follows the arc in a scenario test; anything unsure falls back to a normal blend.

**9c. Beat booster (experimental).** Effects driven by the beat grid from items 1–2. Labelled
experimental, and described honestly as an effect, not a remix.
- **Pump.** The classic EDM side-chain feel: the music dips 2–4 dB on each beat and swells back,
  timed to the song's own beat. The most useful of the three.
- **Kick.** A synthesised four-on-the-floor kick (a short sine sweep, about 120 ms) added on the
  beat, mixed well under the music, for songs that have none.
- Only when beat confidence is high; stops at once if the tempo drifts more than 2 % or the song
  changes meter; never during a blend's first or last bar.
- Accept: kicks land within ±10 ms of the click-track beats; switching it on or off mid-song never
  clicks.

### Costs to keep in check
- Extra analysis only for songs about to be mixed (the next 1–2 in the queue), paused on battery,
  like Flow today.
- No extra CPU while simply listening: alignment work happens once per transition.

## Android next: sound per output (phone-specific EQ)

Desktop already keeps a sound profile (EQ, headphone correction, loudness, Bluetooth lyrics delay)
for each output and switches it automatically. On the phone those settings are hidden, because the
app can't yet tell what the sound is playing through (`btDevices` returns an empty list).

- **Know the output.** `SonclePlugin.java` registers an `AudioDeviceCallback` and reports the
  active output to the app: phone speaker, wired, USB, Bluetooth (A2DP / LE) with its product name,
  car. The existing `devices.js` classifier then names it (earbuds, headphones, speaker, car…).
- **Profiles switch on their own,** as on desktop: earbuds get one EQ, the phone speaker another,
  the car another. Unhide "Per-device sound profiles", "Auto-tune new devices" and "Saved device
  profiles" on the phone ("Output device" stays hidden: Android chooses the output itself).
- **Headphone correction by name.** A Bluetooth headphone's name is matched against AutoEq (as
  desktop's `hpMatch` does), with a one-tap confirm.
- **Pause when headphones come out,** via Android's "audio becoming noisy" broadcast (more reliable
  than watching the device list). Bluetooth lyrics delay per device, as on desktop.
- **New "Phone speaker" preset,** used automatically for the built-in speaker:
  - a high-pass around 150 Hz (deep bass a phone speaker can't play only causes distortion);
  - **virtual bass**: soft harmonics of the cut bass, so the low end is still heard on a tiny
    speaker;
  - +2 dB presence (2–4 kHz) so vocals are clear, and the "Loud" loudness target behind the limiter.
- Accept: the profile changes within a second of plugging in or connecting Bluetooth; the phone
  speaker preset is louder and clearer with no true-peak overs; CPU cost on a phone measured and
  written down.

## Later: move to Uno Platform (C# / .NET): WinUI on Windows, native Android

_Decided 4 Oct 2026. This replaces the earlier Dart / Flutter idea._

**Target.** One C# codebase with XAML UI built on Uno Platform.
- **Windows:** the WinUI 3 / Windows App SDK head. A real native Windows app: Mica, the system
  media controls, low memory, no Chromium.
- **Android:** the .NET for Android head. Java/Kotlin only where Android needs it: the media
  service, Media3/ExoPlayer bindings, audio focus, the notification.

**What carries over, and how.**

| Today (JS) | In Uno (C#) | Notes |
| --- | --- | --- |
| `src/yt.mjs` (YouTube Music, InnerTube) | `Soncle.YouTube` library | Ported by hand. Metrolist's Kotlin `innertube` module is a second reference, and Kotlin reads almost line for line as C#. Tested against the same recorded fixtures. Stream URLs: prefer clients that don't need the player JS run; check what YoutubeExplode does for deciphering before writing our own. |
| `renderer/engine.js` + worklets (limiter, LUFS, EQ, crossfade, gapless) | `Soncle.Audio` core | One DSP core used by both apps, so it sounds identical everywhere. Either allocation-free C# on the audio thread, or C/C++ called through P/Invoke from both heads. Windows output is WASAPI. Android output is AudioTrack, or Media3 with a custom AudioProcessor written in Java. Checked against today's test reference numbers (no true-peak overs, −23 LUFS reference tone, etc.). |
| `renderer/flow/*` (tempo, key, energy, planner) | `Soncle.Flow` | Pure computation, a direct port, checked against the same synthetic-groove tests. |
| `renderer/app.js` (UI) | XAML pages + view models (MVVM) | A rewrite. The `window.api` surface becomes the services the view models call. |
| Settings and library (`library.json`) | Same JSON shape | A one-time carry-over from `%APPDATA%\Soncle` and the Android app, as was done for the rename. |
| AutoEq, lyrics (LRCLIB), Spotify import | C# services | Small ports. |

**Stages. Nothing is switched off until its replacement is at parity.**
0. **Now.** Keep the contracts portable: the `window.api` surface, recorded fixtures, and the test
   reference numbers.
1. **`Soncle.YouTube` + `Soncle.Flow` in C#**, with their tests, as a .NET library. A small
   console app proves search and stream resolution.
2. **`Soncle.Audio`.** Limiter, LUFS, EQ, crossfade, gapless, playing on Windows (WASAPI) and
   Android.
3. **Android app on Uno.** Replaces the Capacitor app in `mobile/`.
4. **Windows app on Uno (WinUI).** Replaces Electron once it matches 1.x. The installer becomes
   MSIX, or keeps a classic installer.

**Build.** GitHub Actions builds both apps: Android on Linux runners, WinUI on Windows runners. So
everything can still be built and installed from a phone.

**Upstream watch.** Add the Uno Platform repo, and YoutubeExplode if it is used, to
`upstream/watch.json` when the work starts.
