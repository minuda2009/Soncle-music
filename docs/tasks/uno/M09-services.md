# M09 — Services (one PR per item)

_Branches: `openhands/uno-m09a-autoeq`, `-m09b-import`, `-m09c-devices`, `-m09d-lyrics`,
`-m09e-carryover`. Needs: M02, M03. Read `README.md` in this folder first. Each item is a separate
PR; they can run in parallel._

Port each file faithfully into `Soncle.Services/`, porting its JS tests as C# tests. HTTP goes
through an injected `HttpMessageHandler`; no real network in tests.

## M09a — AutoEq (`src/autoeq.mjs`, `test/autoeq.test.mjs`)
- **Port:** `parseIndex`, `parseProfile`, `search` (scoring with `RANK`), `match` (the
  device-name cleanup and the "confident only" rule) and `profile`. Storage is an interface, as
  in the JS (`{read, write}`). The cache file name is the first 20 hex characters of the SHA-1
  of the path, the same as today, so existing caches keep working.
- **Accept:**
  - the JS test cases (`hd 600` → oratory1990 first, `WH-1000XM4`, `Galaxy Buds2 Pro`, the
    Realtek speaker → null);
  - the profile parses to the same filters;
  - a second `profile()` is served from storage.

## M09b — Import (`src/spotify.mjs`, `test/spotify.test.mjs`)
- **Port:** `parseSpotifyLink`, `toGid`, `fetchSpotify` (the embed and `__NEXT_DATA__` path, paging),
  `parseCsv`, `parseTrackList`, `norm`, `scoreCandidate` and `matchAll` (concurrency 4,
  cancellation, progress).
- **Accept:** the JS test cases with the same fake responses; `scoreCandidate` numbers equal
  within 1e-9.

## M09c — Output classifier and device profiles (`renderer/devices.js`, `test/devices.test.mjs`)
- **Port:** `classifyOutput(label, btNames)` and `DEVICE_INFO`.
- **Also:** if Android task 01 (`docs/tasks/01-phone-output-profiles.md`) has merged, include its
  Android label cases.
- **Accept:** every JS test case gives the same result object (`label`, `model`, `endpoint`, `type`, `bluetooth`, `handsFree`).

## M09d — Lyrics (`src/main.mjs`: `cleanTitle`, `lrclib`, `lyrics`; `mobile/src/backend.js` has the same)
- **Port:**
  - LRCLIB `get` then `search` (the duration-match rule ±5 s), then the YouTube Music fallback
    through `IMusicCatalog`;
  - the `User-Agent: Soncle (https://github.com/minuda2009)` header;
  - an LRC parser (`[mm:ss.xx]` lines, the offset applied per song from
    `library.lyricsOffsets`), a port of `parseLrc` in `renderer/app.js`.
- **Accept:** fake-handler tests for each path; the LRC parsing equals the JS on 3 sample files
  (add them to `fixtures/lyrics/`).

## M09e — Carry-over and backups (`src/legacy.mjs`, `test/legacy.test.mjs`; backup/restore in `src/main.mjs`)
- **Port:** `carryOver` (copy, never move; the `.carried-over.json` marker; download paths
  rewritten).
- **New source:** add `%APPDATA%\Soncle` (the Electron app) → the new app's data folder, run
  once.
- **Backups:** backup writes `{ app: 'soncle', version: 1, at, …library without secrets }`.
  Restore accepts `soncle` and `metrolist-desktop` markers and replaces the same keys as
  `restore()`.
- **Accept:**
  - the JS legacy tests ported;
  - a backup written by the JS app restores into C# and gives the same library JSON;
  - running carry-over twice copies once.

**Ledger:** each file marked ported in its PR.
