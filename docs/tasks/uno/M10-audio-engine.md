# M10 — Audio engine: decks, master chain, transitions (offline)

_Branch: `openhands/uno-m10-engine`. Needs: M04. Read `README.md` in this folder first._

## Goal
`renderer/engine.js` rebuilt as our own render loop in `Soncle.Audio`: two decks, the master
chain, normalisation, gapless and crossfades. It is proven by rendering **offline** (no sound
device) and measuring the output. Output devices (WASAPI / AudioTrack) come with the heads.

## Inputs
- `renderer/engine.js`, all of it, especially:
  - deck graph: media → norm → bass (low shelf 180 Hz) → fade;
  - master: pre → EQ → headphone correction → loudness comp → width/crossfeed → master → duck →
    mono → panner → limiter;
  - `#rewire` (neutral stages are bypassed);
  - `load`/`play`/`pause` with fades and `wantPause`;
  - `gaplessTo` (warm-up, start-delay measurement, 12 ms overlap: this becomes sample-exact
    here);
  - `crossfadeTo(style 'fade' | 'mix')` with the bass swap;
  - `xfGains` and the crossfade rule from 2f60b95;
  - skip silence (speed-up and instant);
  - tempo/varispeed;
  - suspend 12 s after pause.
- `renderer/app.js`: `transitionPlan` and `lufsFor` (normalisation targets −19/−14/−11 LUFS,
  YouTube LUFS = −14 + loudnessDb, ReplayGain = −18 − gain, quiet-song boost up to +6 dB only
  when the limiter is on). Port these two as pure functions in `Soncle.Audio/Policy/`.
- `test/crossfade.test.mjs`, `fixtures/audio/*`, and plan §5.

## Build (in `Soncle.Audio/`)
- **`IAudioSource`:** pull-based, giving float PCM at a fixed rate. For tests, a
  `GeneratedSource` (tones, clicks, noise) and a `PcmFileSource` (fixtures). Decoding WebM/Opus
  and M4A comes in M12/heads; keep the interface ready for it.
- **`Deck`:** source + norm gain + bass shelf + fade, with sample-accurate scheduling
  (`StartAt(frame)`, `FadeTo(gain, frames, curve)`).
- **`MasterChain`:** the stages above, using M04's DSP, with neutral-stage bypass.
- **`Engine`:**
  - `Load`, `Play`, `Pause` (with the same fade times);
  - `GaplessTo` (the next deck starts on the exact frame the current one ends);
  - `CrossfadeTo(style, seconds)` using `CrossfadeCurves` and the mix rules;
  - `Seek`;
  - meters out through a lock-free snapshot;
  - `Render(Span<float> output, int frames)`, the only method an output device calls.
- **`Policy.TransitionPlan(...)` and `Policy.LufsFor(...)`:** pure ports with the same inputs as
  the JS.
- **Tempo:** SoundTouch.Net for `preservesPitch`, plain resampling for varispeed. Add it to
  THIRD_PARTY_NOTICES.

## Acceptance (offline renders)
- **Gapless:** two click-track sources join with no gap or overlap. The click spacing across the
  join is exact to the sample.
- **Crossfade:**
  - each curve's measured overlap is within 0.1 dB of `xfGains` on two equal noise signals;
  - no style is louder than either song;
  - "mix" applies the bass swap at the planned point.
- **Normalisation:** a −8 LUFS and a −20 LUFS track both measure at the target (−14) ±0.5 LU
  after the chain; the quiet-song boost is capped at +6 dB.
- **Limiter last:** a hot mix never exceeds −1 dBTP at the output.
- **Policy:** `TransitionPlan` and `LufsFor` equal the JS on a table of cases. Export the table
  from JS into `fixtures/audio/policy.json`, adding to M01's export script.
- **No allocations** in `Render` over 10 minutes of rendering (test).
- **CPU:** report the time to render 10 minutes with every stage on (target well under 3 % of
  real time).
- **Ledger:** `engine.js` ported (offline), and app.js `transitionPlan`/`lufsFor` noted.

## Out of scope
Output devices, decoding, ducking (heads), the roadmap's beat-aware mixing (JS first, then port).
