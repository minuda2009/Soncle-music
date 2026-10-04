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

### Costs to keep in check
- Extra analysis only for songs about to be mixed (the next 1–2 in the queue), paused on battery,
  like Flow today.
- No extra CPU while simply listening: alignment work happens once per transition.
