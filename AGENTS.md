# Soncle — instructions for coding agents

Soncle is an independent, unofficial YouTube Music player "built for people who care how it
sounds", by minuda2009 (https://github.com/minuda2009). The ideas, design and direction are his;
agents write, test and ship the code. Licence: GPL-3.0-or-later.

## Layout
- **Desktop app (repo root, Electron 44):** `src/` main process and shared services
  (`yt.mjs` YouTube Music, `streamproxy.mjs`, `potoken.mjs`, `autoeq.mjs`…), `renderer/` the UI
  (`app.js`), audio engine (`engine.js`, `audio/worklets.js`) and Flow radio (`flow/`).
- **Android app (`mobile/`, Capacitor 8):** reuses `renderer/` and `src/` unchanged.
  `mobile/src/backend.js` provides the same `window.api` as `src/preload.cjs`. Native code is in
  `mobile/android/app/src/main/java/com/minuda2009/soncle/` (`SonclePlugin`, `SoncleMediaService`,
  `SoncleStreams`).
- **Plans:** `ROADMAP.md` (features), `docs/WINUI_PLAN.md` (later move to Uno Platform / C#),
  `docs/DESIGN.md` (agreed UI, UX and app-structure decisions — **read it before any UI, UX or
  `uno/` app work**),
  `docs/tasks/` (one file per task handed to an agent; the C# migration tasks are in
  `docs/tasks/uno/`, start with its README). `CHANGELOG.md` gets an entry for every change
  that ships.

### Where to look (fast index)
| I need to… | Go to |
| --- | --- |
| understand the whole app | `docs/DEVELOPING.md` (architecture, env vars, test list) |
| change the desktop UI/audio | `renderer/app.js` (UI), `renderer/engine.js` + `renderer/audio/worklets.js` (audio), `src/main.mjs` (Electron wiring) |
| change YouTube access or streaming | `src/yt.mjs`, `src/streamproxy.mjs`, `src/potoken.mjs` |
| change Flow radio | `renderer/flow/` (`analyze.js`, `flow.js`) |
| change the phone app | `mobile/src/backend.js` (the `window.api`), `mobile/android/.../SonclePlugin.java` |
| work on the C# migration | `uno/src/**` + `uno/tests/**` (portable, Linux-testable); read `docs/WINUI_PLAN.md` §12 for the transfer map and §12.5 for what is already ported |
| know what is agreed for UI/UX/structure | `docs/DESIGN.md` (wins over the plan) |
| find a task to do | `docs/tasks/` (one file per handed-out task; the C# ones are in `docs/tasks/uno/`) |
| see what is planned | `ROADMAP.md` |
| regenerate test fixtures | `tools/fixtures/export-*.mjs` (each writes a JSON file under `fixtures/`) |
| understand the test layout | `test/*.test.mjs` (JS), `uno/tests/**` (C#); shared data in `fixtures/` |

Every `fixtures/**` JSON that a C# test reads is produced by a script in `tools/fixtures/`; never
hand-edit a fixture — change the exporter and regenerate, so the JS and C# stay in step.

## Setup and checks (all must pass before you push)

**One command runs everything this machine can run** (JS tests, lint, drift check, and the C#
libraries when the .NET SDK is present). Use it before every push; it prints what it skipped and why:

```bash
npm run check          # everything;  or: node tools/check.mjs --js / --csharp
```

What it runs, and the same commands by hand:

```bash
ELECTRON_SKIP_BINARY_DOWNLOAD=1 npm ci --ignore-scripts   # root, once
npm test        # node:test; the sign-in test is skipped unless SONCLE_SIGNIN_BROWSER is set
npm run lint    # must be clean (0 errors; 3 known warnings in tools/fixtures are pre-existing)
node tools/drift-check.mjs   # a ported JS file must not change without its C# side (see below)

cd uno && dotnet build Soncle.sln -warnaserror && dotnet test Soncle.sln   # the C# libraries
```

For Android changes also: `cd mobile && npm ci && npm run sync` (the APK itself is built by GitHub
Actions; you don't need the Android SDK). Real YouTube may not be reachable from your sandbox:
test with the offline mock and fixtures, never against a real account.

### Environment gotchas (they cost real turns; read once)
- **The .NET SDK is not installed by default.** Install .NET 10 (the version is pinned in
  `uno/global.json`). On Linux, `dotnet` also needs **libicu** or it aborts at startup with
  *"Couldn't find a valid ICU package"* — `apt-get install libicu76`, or unpack the `.deb` and set
  `LD_LIBRARY_PATH`. Do **not** work around it with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`:
  `String.Normalize` then throws, and the lyrics engine needs it.
- **`uno/heads/Soncle.Windows` and `uno/heads/Soncle.Android` do not build on Linux.** The Windows
  head fails with `UNOB0014` (Uno's SDK and WinAppSDK's XAML compiler are Windows-only); the Android
  head needs the Android SDK. Both are built by CI (`windows-preview.yml`,
  `uno-android-preview.yml`) — do not spend turns trying them locally. The **libraries** under
  `uno/src/**` and `uno/tests/**` are plain `net10.0` and build and test anywhere; that is where the
  agent-verifiable C# work lives.
- **CI runs three separate worlds** and a push only triggers the ones whose `paths:` match:
  `CI` (npm test + lint + drift) and `uno.yml` (build + test) run for almost everything;
  `android.yml` / `android-preview.yml` for `mobile/**`, `renderer/**`, `src/**`, `assets/**`;
  `windows-preview.yml` / `uno-android-preview.yml` for `uno/**`. Touch nothing outside `uno/` and
  the JS apps are untouched.

## C# / `uno/` work (the migration)

The C# port of the app lives in `uno/`. Read `docs/WINUI_PLAN.md` (the plan and the file-by-file
transfer map) and, **before any UI, UX or `uno/` app work**, `docs/DESIGN.md` (the agreed decisions;
where it and the plan differ, DESIGN.md wins). The task files are in `docs/tasks/uno/` — start with
its README.

The rules that matter most for an agent:
- **A port is done when its output equals the JS output on the same fixtures**, not when it
  compiles. Every ported unit gets a golden-diff test that reads `fixtures/…` and compares with the
  JS-produced expected file. Add the fixture exporter under `tools/fixtures/` and commit the JSON.
- **Register every new port in `tools/port-map.json`** (JS file → C# paths). The CI `drift` job fails
  when a ported JS file changes and its C# side and the ledger (`docs/WINUI_PLAN.md` §12.5) are both
  left alone. When you port something, also update the ledger row.
- **Keep the JS names and the order of operations.** Where the JS and C# round differently, port the
  JS behaviour exactly (e.g. `Math.round(x)` is `floor(x + 0.5)` in C#, not `Math.Round`).
- **Port, don't reinvent, and don't silently fix JS bugs** — write them in the PR and add a failing
  JS test if you can.
- **Every new C# file starts with the Soncle header** (the rule below); a test
  (`HeaderCheckTests`) enforces it.
- **No real accounts, cookies or credentials** in code, tests, fixtures or logs. Network tests use
  local fakes and recorded responses.
- New third-party packages go into `THIRD_PARTY_NOTICES.md` with their licence, in the same PR.
  Allowed licences are GPL-3.0-compatible (MIT, BSD, Apache-2.0, LGPL); ask before anything else.

## Git workflow (important)
- **Never push to `main`.** Work on a branch named `openhands/<short-topic>`, e.g.
  `openhands/phone-output`. One task per branch, small commits with clear messages.
- Every push to an `openhands/**` branch that touches the app builds a **preview APK** ("Soncle
  Preview", installs next to the normal app) on the `android-preview` release. minuda2009 tests on
  his phone from there, so say in your summary what to test.
- Open a pull request into `main` when the task's acceptance list is met. minuda2009 merges.
- Add a `CHANGELOG.md` entry (under an "Unreleased" heading) describing what changed for users.

## Rules that don't change
- **Name.** Use "Soncle" everywhere users can see. "Metrolist" may appear only in
  THIRD_PARTY_NOTICES.md, one README line, `src/legacy.mjs` and `upstream/`. Never claim to be
  affiliated with Metrolist, Google or YouTube.
- **Attribution.** New code files start with
  `// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later`.
  Credit stays out of the app's UI.
- **Security.** Never log, print, copy or commit cookie values or credentials. The sign-in cookie
  stays in the main process (desktop) or the backend (Android) and never reaches the UI.
- **Honesty.** No "ad-free" claims. Keep the first-run notice that artists aren't paid through the
  app.
- **Sound and resources.** The limiter stays last in the audio chain; nothing may clip (true peak
  ≤ −1 dBTP). Measure CPU and memory instead of guessing, and write the numbers in the PR.
- **UI.** Material 3 look, Material Symbols outlined at weight 600. Don't restyle things that
  weren't part of the task; minuda2009 reviews UI changes from screenshots first. New UI/UX follows
  `docs/DESIGN.md` (detail levels, navigation, UX rules) and is built in the Uno app; once Uno
  shell work starts, the Electron/Capacitor apps get bug fixes and roadmap items only.
- **When unsure, fall back** to today's behaviour instead of guessing (this applies to audio
  analysis too).
