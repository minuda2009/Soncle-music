# M02 — Models and the `window.api` contract in C#

_Branch: `openhands/uno-m02-contracts`. Needs: M00, M01. Read `README.md` in this folder first._

## Goal
Everything the UI asks the platform for today, written as C# interfaces and models in
`Soncle.Core`. That gives every later port a fixed target, and lets the UI (later) be written
against interfaces.

## Inputs
- `src/preload.cjs`: the full `window.api` surface (about 70 methods and events).
- `mobile/src/backend.js`: the Android implementation of the same surface, plus the extras
  `srcFor`, `range`, `diagnostics` and `mobile`.
- `renderer/app.js`: how each method's result is used (grep `api.<name>`). This is the real
  definition of the return shapes.
- `fixtures/yt/expected/*.json` (M01): the normalised JSON shapes of tracks, albums, artists,
  shelves and pages.

## Build
1. **`fixtures/contract/api.json`.** For every `window.api` member, record:
   - its name;
   - its arguments with types;
   - its return shape: a JSON-schema-like description taken from app.js usage and the expected
     fixtures;
   - whether it's an event;
   - the C# interface + member it maps to.

   Also add a JS test (`test/contract.test.mjs`) that fails if `preload.cjs` or `backend.js`
   gains or loses a member not listed in `api.json`. This keeps the contract honest while the JS
   keeps changing.
2. **Models** in `Soncle.Core/Models/`:
   - `Track`, `ArtistRef`, `AlbumRef`, `Album`, `Artist`, `Playlist`, `Shelf`, `Page`
     (home/explore/search results with continuations), `LyricsResult`, `StreamInfo`
     (client, mime, bitrate, lufs);
   - `Settings` (all keys of `defaultSettings`), `Library`, `DeviceProfile`, `EqState`,
     `FlowFeatures`.
   - Use `System.Text.Json` with a **source-generated** `JsonSerializerContext` (no reflection),
     camelCase names matching the JS JSON exactly.
   - Unknown properties must survive a read/write round trip (`JsonExtensionData`), because
     `library.json` may hold keys newer than the C# code.
3. **Interfaces** in `Soncle.Core/Contracts/`, grouped as in plan §1: `IMusicCatalog`, `IStreams`,
   `IAccount`, `IStore`, `IDownloads`, `ILyrics`, `IHeadphones`, `IFlowCache`, `ILocalLibrary`,
   `IImport`, `ISystem`, `IWindowShell`, `IMediaSession`.
   - All methods are async (`Task`/`ValueTask`) and take `CancellationToken`.
   - Events become C# events, or `IObservable<T>` where they stream (download progress, import
     progress).
   - Each member has an XML doc comment citing the JS member it replaces.
4. **Tests.**
   - Every `fixtures/yt/expected` file deserialises into the models and serialises back to
     JSON-equal output (`JsonAssert`, no tolerance).
   - Every member in `api.json` has exactly one C# member (a reflection test is fine in tests).

## Acceptance
- `dotnet test` and `npm test` pass. The two contract tests are in place.
- `docs/WINUI_PLAN.md` §12.5 ledger: `preload.cjs → Contracts` marked ported.

## Out of scope
Implementations (other tasks), UI.
