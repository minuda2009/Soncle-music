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
  `docs/tasks/` (one file per task handed to an agent). `CHANGELOG.md` gets an entry for every change
  that ships.

## Setup and checks (all must pass before you push)
```bash
ELECTRON_SKIP_BINARY_DOWNLOAD=1 npm ci --ignore-scripts   # root
npm test        # node:test; the sign-in test is skipped unless SONCLE_SIGNIN_BROWSER is set
npm run lint    # must be clean
```
For Android changes also: `cd mobile && npm ci && npm run sync` (the APK itself is built by GitHub
Actions; you don't need the Android SDK). Real YouTube may not be reachable from your sandbox:
test with the offline mock and fixtures, never against a real account.

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
  weren't part of the task; minuda2009 reviews UI changes from screenshots first.
- **When unsure, fall back** to today's behaviour instead of guessing (this applies to audio
  analysis too).
