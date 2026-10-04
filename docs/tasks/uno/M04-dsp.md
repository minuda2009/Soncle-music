# M04 — DSP: true-peak limiter, loudness meter, filters

_Branch: `openhands/uno-m04-dsp`. Needs: M00, M01. Read `README.md` in this folder first._

## Goal
The sound-critical maths, ported to C# sample for sample:
- the master limiter;
- the BS.1770 meters;
- the filters the engine builds (biquads, shelves, crossfeed, crossfade curves).

## Inputs
- `renderer/audio/worklets.js`, all of it:
  - `kWeighting(sr)` and `KFilter`;
  - `Integrator` (momentary 400 ms / short-term 3 s / gated integrated);
  - the polyphase true-peak taps (`TAPS = 12`, `PHASES = 4`, `POLY`);
  - `MasterProcessor`: 2 ms look-ahead, sliding-minimum deque plus box filter, 80 ms release,
    −1 dBTP ceiling, meter messages;
  - `MeterProcessor`.
- `renderer/engine.js`:
  - every `createBiquadFilter` and the gains/frequencies/Q it sets (EQ bands and presets,
    `EQ_BANDS`, `PRESET_PREAMP`, bass shelf 180 Hz, headphone-correction filters from AutoEq
    profiles, loudness compensation);
  - the crossfeed/width matrix;
  - `xfGains(curve, t)`.
- `test/worklets.test.mjs`, `test/crossfade.test.mjs` and `fixtures/audio/*` (M01).

## Build (in `Soncle.Audio/Dsp/`)
- **`Biquad`:** Direct Form I or TDF-II in `float`, with coefficients from RBJ formulas matching
  what Web Audio computes for `lowshelf`, `highshelf`, `peaking`, `highpass` and `lowpass`. Also
  `BiquadCascade`.
- **`TruePeakLimiter`:** a port of `MasterProcessor`.
  - Process interleaved stereo blocks in place: `Process(Span<float> interleaved, int frames)`.
  - Expose the same meter values (momentary, short-term, true peak, gain reduction) through a
    lock-free snapshot the UI can read.
- **`LoudnessMeter`:** a port of `KFilter` + `Integrator` + `MeterProcessor`.
- **`Crossfeed`, `StereoWidth`:** the matrix from engine.js.
- **`CrossfadeCurves.Gains(curve, t)`:** a port of `xfGains`.
- **No allocations in any `Process` call** (a test asserts 0 bytes allocated over 1,000 blocks).

## Acceptance (golden tests against `fixtures/audio`)
- `limiter-hot`:
  - output true peak ≤ −1.0 dBTP;
  - the output matches the JS output within 0.1 dB RMS difference;
  - the gain-reduction trace matches within 0.1 dB.
- `limiter-clean`: output equals the input delayed by the look-ahead, sample-exact within 1e-6.
- `lufs-ref`: −23.0 LUFS ±0.1. Momentary and short-term readings match the JS within 0.1 dB.
- `eq-response`: every preset's magnitude response within 0.1 dB of the fixture across
  20 Hz–20 kHz.
- `crossfade-curves`:
  - all 1,001 points within 1e-6;
  - the crossfade test's rules (never louder than either song, coinciding peaks ≤ 0 dBFS) hold
    in C#.
- **Speed:** process 60 s of 48 kHz stereo through limiter + meter + 10-band EQ and report the
  time in the PR (target: well under 1 % of real time on CI).
- **Ledger:** `worklets.js` ported; the `engine.js` filter parts noted.

## Out of scope
Decks, scheduling, transitions (M10), audio output devices, decoding.
