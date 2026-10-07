# M01 — Shared fixtures: what the JS does, written down

_Branch: `openhands/uno-m01-fixtures`. Needs: nothing (can run alongside M00). Read `README.md`
in this folder first._

## Goal
A top-level `fixtures/` folder that both `npm test` and `dotnet test` read. It holds:
- inputs (recorded YouTube responses, audio test signals, a sample library);
- the expected outputs **produced by today's JS code**.

The C# ports are then proven by comparing against these files. This task also commits an offline
YouTube mock, so screenshots and scenario runs work without a network.

## Build
1. **Layout:**
   ```
   fixtures/
     README.md                   what each folder holds, where it came from, how to regenerate
     yt/raw/<name>.json          raw InnerTube responses (browse, search, next, player…)
     yt/expected/<name>.json     what src/yt.mjs returns for each (normalised plain JSON)
     audio/<name>.f32 + .json    test signals (48 kHz float32 LE) + expected numbers
     flow/<name>.json            analysis inputs/outputs and planFlow cases
     store/library.sample.json   a realistic library.json (no cookie, fake ids are fine)
     contract/api.json           filled by M02
   ```
2. **YouTube responses (`yt/raw`).**
   - **Sources:** we need home, explore, a moods/genre page, search (all, songs, albums), album,
     artist, playlist, up next / radio (`/next`), related, lyrics, and a `/player` response for
     IOS and ANDROID_VR.
   - **Recorded responses:** the Rust project `youtui` (`ytmapi-rs/test_json`,
     https://github.com/nick42d/youtui) has recorded YouTube Music responses. Check its licence
     first. If it is MIT/Apache/GPL-compatible, copy only the files you use, and credit it in
     `THIRD_PARTY_NOTICES.md` and `fixtures/README.md`. If it isn't, stop and say so in the PR;
     minuda2009 will record fresh responses with the desktop app instead.
   - **Clean them:** strip cookies, `visitorData`, tracking params and anything account-specific.
     There must be no `SAPISID`, `__Secure-` or `Cookie` anywhere (add a test that greps for
     them).
3. **Expected outputs (`yt/expected`).**
   - Add `tools/fixtures/export-yt.mjs`. It feeds each raw response through youtubei.js's
     `Parser` and the same normalisers `src/yt.mjs` uses, and writes the result.
   - The exported test hook is `__parse` (`normItem`, `normShelf`, `norms`). Add only the minimal
     extra exports you need to `src/yt.mjs`, with no behaviour change.
   - Add `test/fixtures.yt.test.mjs` that re-runs the export in memory and asserts it still
     equals `yt/expected`. This way any future JS change that alters the output is caught, and
     the C# port gets updated with it.
4. **Audio reference data (`audio/`).** Add `tools/fixtures/export-audio.mjs`. It uses
   `test/worklets.test.mjs`'s approach (run `renderer/audio/worklets.js` in a VM) to write:
   - **`limiter-hot`:** input + output signal (≈3 s); expected true-peak ≤ −1 dBTP and the
     per-block gain-reduction trace;
   - **`limiter-clean`:** quiet input; expected output = input delayed by the look-ahead;
   - **`lufs-ref`:** the −23 LUFS reference tone; expected momentary, short-term and integrated
     readings;
   - **`crossfade-curves`:** `xfGains(curve, t)` from `renderer/engine.js` for `smooth`,
     `equalpower` and `linear` at 1001 points (matches `test/crossfade.test.mjs`);
   - **`eq-response`:** the magnitude response (dB at 1/24-octave points, 20 Hz–20 kHz) of each EQ
     preset and of the bass shelf. Compute it from the same biquad coefficients the engine
     configures, using the RBJ cookbook formulas, so it doesn't depend on Web Audio.

   Keep the total under ~10 MB. Prefer short signals plus generator parameters over long files.
5. **Flow reference data (`flow/`)**, from `test/flow.test.mjs` cases:
   - the synthetic groove's PCM (22.05 kHz) + expected `analyze()` result (tempo, key, Camelot,
     energy);
   - `keyCompat`/`tempoCompat` tables;
   - several `planFlow(seed, pool, feat, opts)` inputs with their exact output order. Fix the
     `contextFor` date so it is deterministic.
6. **Sample library (`store/`).** Write a realistic `library.json`: settings with non-default
   values, liked songs, playlists, history, device profiles, `lyricsOffsets`, `eqPresets`. Leave
   out `cookie` and `session`. Use `src/defaults.mjs` for the shape.
7. **Offline mock.** Commit `dev/mock.mjs`, a youtubei.js client double for `SONCLE_MOCK` (the
   README mentions it but it isn't in the repo). It serves the `fixtures/yt/raw` files instead of
   a local path. Check that `SONCLE_MOCK=dev/mock.mjs` lets the desktop app show Home, Search, an
   album and a playlist with no network (a headless screenshot in the PR is enough).

## Acceptance
- `npm test` and `npm run lint` pass, including the new fixture tests and the "no cookies in
  fixtures" test.
- Re-running the export scripts produces byte-identical files (they're deterministic).
- `fixtures/README.md` says where every file came from and how to regenerate it.
- No behaviour change in the apps (only export hooks were added).

## Out of scope
Any C# code. Screenshots of every page (a later stage-0 task once the mock exists).
