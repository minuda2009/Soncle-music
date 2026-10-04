# Staying in step with upstream

Soncle was originally ported from an Android app (credited in
[THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md)). The two share no code that could simply be
merged: upstream is Kotlin for Android, and Soncle is JavaScript on Electron. Fixes upstream still
matter for the few parts Soncle mirrors, such as PO tokens and stream clients. They arrive here in
three ways. The upstream repository and paths are configured in `upstream/upstream.json`.

## 1. Applied automatically

`tools/upstream-sync.mjs` downloads upstream's latest `main` and copies the files Soncle uses
unchanged. Today that is only `po_token.html`, the BotGuard page that PO tokens depend on.

UI icons are not synced: they come from Google's Material Symbols (see `tools/icon-sources.json`).

## 2. Reported for porting

Everything else upstream is Kotlin, so it has to be ported. `upstream/upstream.json → watch` lists
each upstream file Soncle was ported from and the file here that mirrors it. Examples: PO tokens
→ `src/potoken.mjs`, stream clients → `src/yt.mjs`, crossfade and normalisation →
`renderer/engine.js`, lyrics providers → `src/main.mjs`.

The script fingerprints those files. When any of them change, it writes `upstream/REPORT.md` with
links to each changed file and the full upstream diff, grouped by the file to update here.

## 3. Through dependencies

Most "YouTube changed something and playback broke" fixes land in
[youtubei.js](https://github.com/LuanRT/YouTube.js). Dependabot (`.github/dependabot.yml`) opens a
PR whenever youtubei.js, Electron or music-metadata release an update.

## How it runs

- **On GitHub.** `.github/workflows/upstream-sync.yml` runs every Monday, and on demand from the
  Actions tab.
  - If anything changed, it runs the tests and opens a single **"Sync with upstream …"** pull
    request whose description is the report. Nothing reaches the app until that PR is merged.
  - One-time setup: *Settings → Actions → General → Workflow permissions →* "Read and write" and
    "Allow GitHub Actions to create and approve pull requests".
- **Locally.** `npm run upstream:check` shows what's new without changing anything.
  `npm run upstream` applies the automatic parts and writes the report.
- **After porting.** Merge the PR. The fingerprints in `upstream.json` move forward, so the same
  change isn't reported again.

The baseline is upstream **v13.7.0**, the version Soncle was ported from.

## Adding to it

When you port something new, add its upstream path and your file to `watch` in
`upstream/upstream.json`. Then run `node tools/upstream-sync.mjs --init` to take a fresh baseline.

## When upstream reorganises (e.g. Kotlin Multiplatform)

A move to Kotlin Multiplatform (KMP) relocates most files, for example to
`shared/src/commonMain/kotlin/…`. The sync is built for that:

- **Moved files are followed automatically.** When a watched file or folder disappears, the tool
  finds its new home, by file name for files and by how many of their files match for folders. It
  re-points `upstream.json` and carries the fingerprints over, so a move alone isn't reported as a
  change. Anything it can't find is flagged for a human.
- **New KMP and desktop code is reported.** The report lists `commonMain` totals and every new
  `desktopMain` / `jvmMain` file.
- **A separate repository is supported.** Run `node tools/upstream-sync.mjs --repo <owner>/<name>`
  once; the new repository is saved for the weekly run.

This was tested against a simulated migration of v13.7.0's real sources. Every moved file was
found, only genuine content changes were reported, and the new desktop file was flagged.

## The limit

No tool can apply *every* upstream change automatically, because Kotlin can't run inside Soncle.
This setup makes sure no upstream change that matters goes unnoticed, and applies the ones that
can be applied safely.
