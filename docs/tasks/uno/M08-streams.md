# M08 — Whole-song downloader and cache (`Soncle.Streams`)

_Branch: `openhands/uno-m08-streams`. Needs: M00. Read `README.md` in this folder first._

## Goal
One downloader for both apps. It replaces desktop's `mstream:` proxy and Android's
`SoncleStreams.java`, taking the Android 0.1.4–0.1.6 design because that is the more resilient
one (built for weak mobile networks). The C# audio decoder will read songs through it.

## Inputs
- **`mobile/android/app/src/main/java/com/minuda2009/soncle/SoncleStreams.java`: the reference.**
  - `Download`: sequential whole-file download into the cache, a 256 KB first piece then ≤ 1 MB
    requests, kept-alive connections, back-off retry for ~10 minutes, refresh on 403 for the
    *same* length only;
  - `FromFile`: reads that wait for bytes;
  - `Chunks`: direct network reads for a far seek;
  - `AHEAD = 1.5 MB`, and keep 4 songs;
  - a prefetched song waits until the playing one is complete (`wanted` / `otherDownloadRunning`).
- `src/streamproxy.mjs` and `test/streamproxy.test.mjs`: desktop's rules (switch client
  mid-stream on 403 and continue from the same byte, short chunks from the origin,
  416/suffix-range handling, the chunk cache).
- `mobile/src/backend.js` (`nativeStreamUrl`, the `streamExpired` listener): how the app side
  registers songs, reuses a song's key and refreshes URLs.

## Build (in `Soncle.Streams/`)
- **`SongDownloader`:**
  - `Register(songKey, StreamSource)`, where `StreamSource` has url, headers, length and mime;
  - `Forget`;
  - `OpenRead(songKey, long position)` returns a `Stream` that reads from the cache file, waiting
    for bytes, or from the network for a far seek;
  - a `Status` (bytes, done, failed) and progress events.
- **`IStreamRefresher`:** called on 403; returns a new source for the same song or null. The
  downloader only accepts a refresh with the same length.
- **A cache folder passed in,** with eviction (keep N=4), cleared at start.
- **Priority:** the song being read wins; prefetches wait for it to finish.
- **Everything async,** with no thread per download (use `HttpClient` with one connection pool),
  and cancellation everywhere.

## Acceptance (tests against a fake googlevideo server, an in-process `HttpMessageHandler` or Kestrel)
- **First bytes:** readable after the first piece (≤ 256 KB), long before the song is complete.
- **Dead zone:** a dead zone mid-song (the fake refuses connections for 20 s) is survived with
  back-off. The reader resumes with no corrupted bytes (SHA-256 of the result equals the source).
- **403 mid-song:** the refresher is called and the download continues from the same byte.
  - A refresh with a different length is rejected and the song fails cleanly.
  - With no refresher, it fails after the retry budget, using a fake clock so the test is fast.
- **Far seek:** a seek far past the downloaded part is served from the network immediately, and
  the download continues.
- **Prefetch:** a registered next song doesn't start until the current one is complete, unless it
  is read.
- **Eviction:** keeps 4 songs and deletes cancelled files.
- **Range edge cases:** the `test/streamproxy.test.mjs` cases (short origin chunks, end ranges)
  are ported.
- **Ledger:** `streamproxy.mjs` and `SoncleStreams.java` marked "replaced by Soncle.Streams
  (pending heads)".

## Out of scope
HTTP serving to a WebView (not needed in C#), decoding, choosing clients (M07).
