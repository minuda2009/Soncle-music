# M05 — Flow: WebM cues, tempo/key/energy, planner

_Branch: `openhands/uno-m05-flow`. Needs: M00, M01. Read `README.md` in this folder first._

## Goal
Flow radio's analysis and ordering in C#, giving the same answers as the JS.

## Inputs
- `renderer/flow/webm.js`: EBML reading (`readId`, `readSize`, `children`), `parseHead`,
  `pickRange`, `wholeClusters`, `buildFile`.
- `renderer/flow/analyze.js`: `fft`, the onset envelope and tempo autocorrelation, `keyChroma`,
  the Krumhansl–Kessler `PROFILES` and `opts`, `camelotOf`, `keyName`, `analyze(pcm, sr)`.
- `renderer/flow/flow.js`: `parseCamelot`, `keyCompat`, `tempoCompat`, `HINTS`/`hintEnergy`,
  `energyOf`, `transition`, `contextFor`, `planFlow`, `featLabel`.
- `renderer/flow/sampler.js`: how ~30 s is chosen and decoded. Port only the range logic here;
  fetching and decoding come later with Streams/Audio.
- `test/flow.test.mjs` and `fixtures/flow/*` (M01).

## Build (in `Soncle.Flow/`)
- **`WebmCues`:**
  - parse the head and Cues;
  - pick a byte range for N seconds at a fraction of the song;
  - cut whole clusters;
  - rebuild a minimal valid WebM.

  Test with a real WebM from fixtures (add a short one to `fixtures/flow/` if M01 didn't).
- **`Analyzer.Analyze(ReadOnlySpan<float> pcm, int sampleRate)`:** returns tempo, key, mode,
  Camelot and energy. Use a port of the same radix-2 FFT, for identical results; don't swap in a
  library FFT.
- **`Planner`:** a port of `planFlow` and `transition`. `ContextFor(DateTime)` takes the time as
  input (no `DateTime.Now` inside), so tests are deterministic.

## Acceptance
- **Synthetic groove:** 128 BPM ±2, Camelot 8A, energy within 0.01 of the JS fixture.
- **`keyCompat`/`tempoCompat` tables:** identical, including half/double-time folding.
- **`planFlow` cases from fixtures:** identical output order. If floating-point ties make the order
  differ, report it and match the JS tie-break.
- **WebM:** the rebuilt file from a fixture is byte-identical to the JS `buildFile` output.
- **Speed:** analysing 15 s at 22.05 kHz takes well under 100 ms on CI (report the number).
- **Ledger:** `webm.js`, `analyze.js` and `flow.js` ported; `sampler.js` partially ported.

## Out of scope
The roadmap's Flow full-pool fix and beat tracking (they ship in JS first, then get ported).
