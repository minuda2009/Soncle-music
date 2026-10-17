<p align="center"><img src="assets/soncle-icon.svg" width="112" alt="Soncle"></p>

<h1 align="center">Soncle</h1>

<p align="center"><b>A YouTube Music player built for people who care how it sounds.</b></p>

<p align="center">
  <a href="https://github.com/minuda2009/Soncle-music/releases/latest"><b>⬇ Download</b></a> ·
  <a href="#install">Install</a> ·
  <a href="#what-makes-it-different">Features</a> ·
  <a href="#questions">Questions</a> ·
  <a href="./docs/DEVELOPING.md">Develop</a>
</p>

<p align="center">
  <a href="https://github.com/minuda2009/Soncle-music/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/minuda2009/Soncle-music?label=release"></a>
  <img alt="Platforms" src="https://img.shields.io/badge/Windows%20%C2%B7%20Android-3b82f6">
  <a href="./LICENSE"><img alt="Licence" src="https://img.shields.io/badge/licence-GPL--3.0--or--later-blue"></a>
</p>

> Soncle is an independent, unofficial project. It is not affiliated with or endorsed by Google,
> YouTube or Metrolist Group. Artists are not paid through the app.

## Install

| Your device | Get | Notes |
| --- | --- | --- |
| **Windows** | [`Soncle-Setup-<version>.exe`](https://github.com/minuda2009/Soncle-music/releases/latest) | Pick the folder, no admin rights needed. Start menu entry and uninstaller. |
| **Windows, no install** | [`Soncle-Portable-<version>.exe`](https://github.com/minuda2009/Soncle-music/releases/latest) | Runs from anywhere, e.g. a USB stick. |
| **Android** | [`Soncle-Android-<version>.apk`](https://github.com/minuda2009/Soncle-music/releases/latest) | Open it on the phone and allow installing from your browser if Android asks. |

Everything is on the **[latest release](https://github.com/minuda2009/Soncle-music/releases/latest)** page, with checksums.

The builds are not code-signed yet, so Windows SmartScreen may say "Windows protected your PC".
Choose **More info → Run anyway**.

## What makes it different

| | |
| --- | --- |
| 🎚 **Sound first** | A true-peak limiter (−1 dBTP) that is always last in the chain, loudness normalisation (Quiet, Normal, Loud), gapless albums, a 10-band EQ and live meters with an A/B "hold for original" button. |
| 🎧 **Headphone correction** | Measured AutoEq profiles for about 9,000 headphone models. |
| 🔀 **Transitions that fit** | Beat-timed crossfades with a bass swap. **Flow radio** orders a queue so tempo, key and energy move smoothly, and learns from what you play, like and skip. |
| 🪶 **Light** | About 0.1 % CPU when idle, about 8 % while playing, and nothing 12 s after you pause (measured with `SONCLE_METRICS=1`). |
| 🔒 **Private** | Signing in is optional. Cookies stay in the app's main process, are encrypted at rest and never reach the UI. Your listening history stays on your device. |
| 🧰 **Everything else** | Ctrl+K command bar, local files, Spotify playlist import, Focus mode, smart ducking on Windows, and mixes made for you on your own device. |

Flow's tempo and key analysis runs on your device and downloads only a few hundred KB from the
middle of a song, with no outside service or API key. [How it works →](./docs/DEVELOPING.md#how-flow-analysis-works)

## Signing in

Optional. Click *Sign in* and a separate Chrome, Edge or Brave window opens on Google's sign-in
page, because Google does not allow sign-in inside apps. Soncle picks up the session and closes
that window. Your normal browser profile is not touched.

## Questions

<details><summary><b>Is this official? Does it remove ads?</b></summary>

No. It is an independent, unofficial player that plays what YouTube Music serves. It makes no
promise about ads, and it does not pay artists; please support the artists you love directly.
</details>

<details><summary><b>Where is my data?</b></summary>

On your device. On Windows that is `%APPDATA%\Soncle`. Uninstalling keeps your library and
settings; delete that folder to remove them too.
</details>

<details><summary><b>What are the "preview" releases?</b></summary>

Automatic test builds made while the app is being worked on. They can be broken and each one
replaces the last, so use the latest release unless you are helping test.

- [android-latest](https://github.com/minuda2009/Soncle-music/releases/tag/android-latest): newest Android build from `main`
- [android-preview](https://github.com/minuda2009/Soncle-music/releases/tag/android-preview): the branch being tested, installs next to the normal app
- [windows-preview](https://github.com/minuda2009/Soncle-music/releases/tag/windows-preview): early build of the new C# Windows app (shell only so far)
</details>

<details><summary><b>What is happening with the C# rewrite?</b></summary>

Today's apps are Electron (Windows) and Capacitor (Android), and both are maintained. A rewrite in
C# on Uno Platform, one app for Windows and Android that uses much less memory, is under way in
[`uno/`](./uno). It is not usable yet; the plan and progress are in
[docs/WINUI_PLAN.md](./docs/WINUI_PLAN.md) and the roadmap in [ROADMAP.md](./ROADMAP.md).
</details>

<details><summary><b>Something is broken or missing</b></summary>

[Open an issue](https://github.com/minuda2009/Soncle-music/issues/new/choose). In the app,
*Settings → Copy diagnostic log* gives host names only, never cookies.
</details>

## About

Soncle is by **[minuda2009](https://github.com/minuda2009)**. The ideas, feature design, direction
and real-world testing are his. The code was written with AI coding agents (Anthropic's Claude)
working to his specifications and feedback; every change runs against the test suite, lint and
scripted headless runs before a build goes out.

Soncle grew out of [Metrolist](https://github.com/MetrolistGroup/Metrolist) for Android and uses
[youtubei.js](https://github.com/LuanRT/YouTube.js). Third-party code, data and credits are in
[THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md).

**Docs:** [Developing](./docs/DEVELOPING.md) · [Releasing](./docs/RELEASING.md) · [Changelog](./CHANGELOG.md) · [Roadmap](./ROADMAP.md)

Licence: GPL-3.0-or-later, see [LICENSE](./LICENSE).
