# soncle — command line for the C# libraries

A small tool that exercises the libraries end to end (search → resolve → download → decode →
normalise → play) before any UI exists.

## Two modes
Say where the data comes from, every time:
- `--offline`: recorded fixtures, no network (what CI runs).
- `--live`: real YouTube, anonymous. **Only `resolve` and `download` work live for now.**

## Offline (works end to end)
Everything runs against **recorded fixtures, with no network**:

```
dotnet run --project uno/tools/Soncle.Cli -- --offline search daft punk
dotnet run --project uno/tools/Soncle.Cli -- --offline resolve offline-song
dotnet run --project uno/tools/Soncle.Cli -- --offline download offline-song song.bin
dotnet run --project uno/tools/Soncle.Cli -- --offline analyze offline-song
dotnet run --project uno/tools/Soncle.Cli -- --offline play offline-song out.wav --target -14
```

- `search` answers from a recorded YouTube Music response (`fixtures/yt/raw/search-all.json`).
- The only playable song is `offline-song`, a 6-second stereo tone. Its loudness is measured at
  start-up and reported the way YouTube reports it, so `play` shows the real normalisation:
  it brings the song to the target (default −14 LUFS), never boosts by more than +6 dB, and never
  goes over the −1 dBTP limiter ceiling.
- `play` writes a 32-bit float stereo WAV. At the end it prints its own CPU time and peak memory.
- It never prints stream URLs, tokens or cookies, and there is no sign-in.

## Live (`--live`), for you to try on your PC
```
dotnet run --project uno/tools/Soncle.Cli -- --live resolve <videoId>
dotnet run --project uno/tools/Soncle.Cli -- --live download <videoId> song.webm
```
- Uses the same request shapes as the JS app (taken from youtubei.js 18.1.0) and the clients that
  return plain URLs: **IOS, then ANDROID_VR**. No PO token and no player script, so the web-style
  clients (YTMUSIC, WEB, TV, WEB_EMBEDDED) are not used here.
- It is anonymous. No cookie is read, sent or printed, and stream URLs are never printed.
- **Not yet tried against real YouTube**, because the build sandbox can't reach it. If `resolve`
  fails, the error lists each client's reason; please paste it.
- `--live search` says it isn't built yet.

## What does not work yet
- **Live search**, and signed-in features. They need the catalog's InnerTube calls.
- **Web-style stream clients.** They need a head's browser engine to run YouTube's player script
  (`IPlayerScript`) and a PO-token minter.
- **Decoding real songs.** YouTube streams are WebM/Opus or M4A. The WebM reader and an Opus
  decoder are not in this build, so `play` of such a stream exits with code 3 and says so.
- **Playing through speakers.** Output to the sound card (WASAPI on Windows) comes with the
  Windows head; elsewhere `play` writes a WAV.

## Exit codes
`0` ok · `1` usage · `2` failed · `3` can't decode that format yet.
