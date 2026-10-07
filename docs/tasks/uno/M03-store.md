# M03 — Settings defaults and the library store

_Branch: `openhands/uno-m03-store`. Needs: M02. Read `README.md` in this folder first._

## Goal
`Soncle.Core` reads and writes `library.json` exactly as the desktop app does: same defaults, same
merge rules, same upgrades, same safety. Then the new app can open an existing user's data.

## Inputs
- `src/defaults.mjs`: `defaultSettings` and `defaults`.
- `src/main.mjs`, from the top of the file to `save()`:
  - load order: read → `upgradeSettings` → merge defaults (with `eq` merged one level deep) →
    `welcomed` rule for old installs;
  - writes: atomic (`.tmp` + rename) and debounced 400 ms;
  - the `cookie` is never stored in plain text and never returned by `store:get`.
- `src/legacy.mjs`: `upgradeSettings`, `isBackupMarker`, `BACKUP_MARKER` (the carry-over itself is
  M09).
- `mobile/src/backend.js`: the phone-only overrides (`MOBILE_SETTINGS`, Auto quality on first run,
  `mobileQuality`).
- `fixtures/store/library.sample.json` (M01).

## Build
- **`Settings.Defaults` and `Library.Defaults`:** values identical to `defaults.mjs`. Add a
  generated JSON copy test: the JS defaults are exported to `fixtures/store/defaults.json` (add
  that export to M01's script if missing), and the C# defaults must equal it.
- **`LibraryStore`:**
  - `LoadAsync(path)` and `SaveAsync()` (debounced 400 ms, `.tmp` + atomic replace, and a
    `SaveNowAsync()` for exit);
  - the merge and upgrade rules above;
  - `GetForUi()`, which returns everything except the account secret;
  - `Set(key, value)`, which refuses `cookie` and `downloads` like `store:set`.
- **Secret handling:** an `ISecretProtector` interface (`Protect`/`Unprotect`, bytes in and out).
  The Windows head will implement it with DPAPI and Android with the Keystore. Here, a test fake.
  The cookie field round-trips only through it.
- **Platform overrides:** `ApplyPlatformOverrides(Platform)` reproduces `MOBILE_SETTINGS` and the
  phone's Auto-quality first-run rule.

## Acceptance
- `library.sample.json` → load → save gives JSON equal to what the desktop app writes. Unknown
  keys are kept.
- An old-style file with no `welcomed` key gets `welcomed: true`. A file with a legacy `xfCurve`
  is upgraded exactly like `upgradeSettings`. Test both against outputs exported from the JS.
- A corrupted or partial file falls back to defaults without throwing, as the JS does.
- `GetForUi()` never contains the cookie (test).
- Ledger: `defaults.mjs` ported; `main.mjs` store section noted as partially ported.
