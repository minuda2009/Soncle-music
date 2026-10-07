# Uno / C# migration: tasks for coding agents

This folder turns `docs/WINUI_PLAN.md` into work an agent can pick up and finish in one pull
request. **Read `AGENTS.md` first, then `docs/WINUI_PLAN.md`** (the "why" and the target design),
then the one task file you were given. If a task file and the plan disagree, the plan wins; say so
in your PR.

## What this migration is
- **Today:** two shipping apps that share JavaScript:
  - the Electron desktop app (repo root: `src/`, `renderer/`);
  - the Capacitor Android app (`mobile/`).
- **Target:** one C# / .NET codebase on Uno Platform. It has a WinUI 3 head on Windows and a
  .NET for Android head, keeps the Material 3 look, and keeps every 1.0 feature.
- **The JS apps keep shipping until the C# app passes the parity checklist** (plan §4).
  Nothing you do here may change how the current apps behave.

## Ground rules for C# work
1. **Everything C# lives in `uno/`.** Don't touch `src/`, `renderer/` or `mobile/`, except where a
   task explicitly says to (fixtures, exports, a JS test that pins behaviour).
2. **A port is done when its output equals the JS output on the same fixtures**, not when it
   compiles. Every ported unit gets a golden-diff test that reads `fixtures/…` and compares with
   the JS-produced expected file. Number tolerances are stated in the task (audio: ±0.1 dB unless
   stated).
3. **Port, don't reinvent.**
   - Keep the JS function and variable names (in C# casing).
   - Keep the order of operations and the constants. Comments that explain *why* come along too.
   - If you find a bug in the JS, don't fix it silently in C#. Write it in the PR, and add a
     failing JS test if you can.
4. **The libraries are plain `net10.0` class libraries** (no Uno, no Windows APIs): `Soncle.Core`,
   `Soncle.YouTube`, `Soncle.Streams`, `Soncle.Flow`, `Soncle.Audio` (DSP and engine; no output
   device), `Soncle.Services`. They must build and test on Linux. Platform code goes in the
   heads later.
   - The SDK is pinned in `uno/global.json` (task M00).
   - If Uno's current templates need a different .NET version when the heads are added, the
     libraries follow; change `global.json` in that PR only.
5. **Audio code on the render path doesn't allocate.**
   - Use `Span<float>`, preallocated buffers and `System.Numerics.Vector` where it helps.
   - A test asserts zero allocations per block (`GC.GetAllocatedBytesForCurrentThread`).
6. **No real accounts, no cookies, no credentials** in code, tests, fixtures or logs. Network tests
   use local fakes and recorded responses. Real YouTube is never called from CI.
7. **File header** on every new file:
   `// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later`.
   Every new third-party package goes into `THIRD_PARTY_NOTICES.md` with its licence, in the same
   PR. Allowed licences are GPL-3.0-compatible (MIT, BSD, Apache-2.0, LGPL). Anything else: ask
   in the PR before adding it.
8. **Keep the ledger.** When your task finishes a unit, update the ledger table in
   `docs/WINUI_PLAN.md` §12.5: unit, status `ported` (+ PR number), and the JS commit you ported
   from.

## Workflow
- **Branch:** `openhands/uno-<task id>-<topic>`, for example `openhands/uno-m04-dsp`. One task per
  branch, one PR.
- **Before pushing,** all of these must pass:
  - `cd uno && dotnet build -warnaserror && dotnet test` (once M00 exists);
  - `npm test` and `npm run lint` at the repo root, if you touched anything outside `uno/`.
- **The PR description** lists:
  - what was ported (JS file → C# file);
  - the golden tests and their tolerances;
  - anything that differs from the JS, and why;
  - numbers where the task asks for them (speed, allocations).
- **Branch builds:** pushes to `openhands/**` also build a preview APK. That only matters if you
  touched `mobile/`, `renderer/` or `src/`; C#-only work doesn't trigger it.

## Order and dependencies

| Task | What | Needs |
| --- | --- | --- |
| [M00](M00-scaffold.md) | `uno/` solution, projects, test projects, CI (`uno.yml`), header check | — |
| [M01](M01-fixtures.md) | `fixtures/`: recorded YouTube responses + JS-normalised outputs, audio and Flow reference data, a sample library; committed offline mock | — |
| [M02](M02-contracts.md) | `Soncle.Core` models + the `window.api` contract as C# interfaces | M00, M01 |
| [M03](M03-store.md) | Settings defaults, library store, round-trip, legacy upgrade | M02 |
| [M04](M04-dsp.md) | True-peak limiter, LUFS meter, filters (from `worklets.js` / `engine.js`) | M00, M01 |
| [M05](M05-flow.md) | WebM cues, tempo/key/energy analysis, Flow planner | M00, M01 |
| [M06](M06-youtube-parse.md) | YouTube Music parsing and normalisation (`yt.mjs`, read side) | M02 |
| [M07](M07-youtube-streams.md) | InnerTube client, stream clients, BotGuard helpers, PO-token and JS-runtime interfaces | M06 |
| [M08](M08-streams.md) | Whole-song downloader + cache (`SoncleStreams.java` design) | M00 |
| [M09](M09-services.md) | AutoEq, import, device classifier, lyrics, carry-over (several small PRs) | M02, M03 |
| [M10](M10-audio-engine.md) | Two decks, master chain, transitions, crossfade rule; offline renderer | M04 |
| [M11](M11-playback-controller.md) | `PlaybackController` state machine with the playback rules as tests | M02, M10 |
| [M12](M12-console.md) | Console app: search, resolve, download, decode, play | M06–M08, M10 |

- **Parallel work:**
  - M00 and M01 can run in parallel.
  - After them, M02, M04, M05 and M08 are independent of each other.
- **Not tasked yet:**
  - The design system (plan stage 3), the heads and the pages (stages 4–5). They depend on the
    **head-order decision** at the top of `docs/WINUI_PLAN.md` ("Order: _undecided_").
  - Their task files are written once minuda2009 records that decision. Don't start shell or page
    work before then.

## Definition of done (every task)
- The task's acceptance list is met. CI is green on the PR.
- Golden tests exist and pass. No test reaches the real network.
- `THIRD_PARTY_NOTICES.md` is updated if a package was added, and the plan's ledger is updated.
- No change in behaviour to the Electron or Capacitor apps (their tests still pass).
