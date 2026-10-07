# M07 — InnerTube client, stream clients, BotGuard helpers

_Branch: `openhands/uno-m07-yt-streams`. Needs: M06. Read `README.md` in this folder first._

## Goal
The request side of the YouTube port:
- talk to InnerTube;
- resolve a playable audio stream with the same client order, probing and fallbacks as `yt.mjs`;
- prepare the places where the heads plug in PO tokens and a JavaScript engine.

## Inputs
- `src/yt.mjs`:
  - `configure`, `create`/`yt()`/`ytAnon()` (cookie vs anonymous sessions);
  - `STREAM_SPECS` and `setStreamClients`;
  - `headersFor`, `uaFor`, `clientCanSeek` (mid-file range probe), `probeLength`, `tryClient`,
    `resolveInOrder`, `resolveStream`, `resolveStreamRotating`;
  - the stream cache (`STREAM_CACHE_MAX`, expiry minus 10 min), `invalidateStream`, `rate`,
    `premiumStatus`, `library`.
- `src/botguard.mjs` (all of it) and `test/botguard.test.mjs`.
- `src/potoken.mjs`, for behaviour only: one minter shared by concurrent requests, init gives up
  after 25 s, an idle close, and a failed token never breaks another request in flight. The
  WebView part itself belongs to the heads.
- `mobile/src/native.js` (`mintPoToken`) for the same rules on Android.
- `docs/WINUI_PLAN.md` §6, especially deciphering and the WebView2 origin.

## Build (in `Soncle.YouTube/`)
- **`InnerTubeClient`:**
  - builds the InnerTube `context` (client name/version, hl/gl from settings, visitor data) and
    posts to `/youtubei/v1/{browse,search,next,player,music/get_search_suggestions,…}`;
  - takes an `HttpMessageHandler` so tests can replay recorded responses;
  - signed-in requests add the cookie and the `SAPISIDHASH` authorization header computed exactly
    as youtubei.js does. Add a test vector for that hash; the cookie value is only ever read from
    `IAccount`, never logged.
- **`Catalog : IMusicCatalog`:** implements home, search, album, … using M06's `Pages.*`.
- **`StreamResolver : IStreams`:**
  - same `STREAM_SPECS` order (YTMUSIC+PO, IOS, ANDROID_VR, WEB+PO, TV, WEB_EMBEDDED) and
    `SetClients(...)`;
  - same audio-format choice (no DRC, the original-language track, highest bitrate, or lowest
    for `low`);
  - same `clen` / `probeLength` length fallback;
  - the same mid-file seek probe;
  - the same cache and expiry, and the same rotating retry on 403.
- **Plug-in points:**
  - `IPoTokenProvider.MintAsync(string identifier, CancellationToken)`;
  - `IJsRuntime.EvaluateAsync(string code)`, used for n/sig deciphering, with the plan's order:
    clients that need no deciphering first, a browser engine (head-provided) as the expected
    path, and Jint only as an experiment behind a flag.
- **`BotGuard`:** a port of `botguard.mjs` (`b64bytes`, `tokenFromBytes`, `parseBody`,
  `parseChallenge`, the constants, `bgHeaders`) plus a `PoTokenMinter` class that holds the shared
  / timeout / idle rules over an injected `IBotGuardPage` (the WebView, provided by a head).

## Acceptance
- **BotGuard:** the `test/botguard.test.mjs` vectors pass in C#.
- **Resolver:** tested with a fake handler and fake players (recorded `/player` fixtures for IOS
  and ANDROID_VR):
  - client order;
  - fallback when a client fails;
  - a PO-token failure skips the token clients without retrying them (the regex rule in
    `resolveInOrder`);
  - the 403 rotation;
  - the cache hit and expiry;
  - `low` quality picks the lowest bitrate.
- **Minter:** with a fake `IBotGuardPage`:
  - two concurrent mints share one init;
  - a failed init fails fast for 2 minutes;
  - a failed mint doesn't null a newer minter;
  - init times out at 25 s (use a fake clock).
- **No real network in tests.** M12 checks against real YouTube.
- **Ledger:** `yt.mjs` and `botguard.mjs` ported, with the commit.

## Out of scope
The WebView2/WebView implementations (heads), sign-in (Windows head), the downloader (M08).
