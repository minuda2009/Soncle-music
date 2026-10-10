# soncle — command line for the C# libraries

A small tool that exercises the libraries end to end (search → resolve → download → decode →
normalise → play) before any UI exists.

## What works in this build
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

## What does not work yet
- **Real YouTube.** Without `--offline` the tool says so and stops. It needs the InnerTube client
  (`IStreamClient`, the request + decipher side) and a transport; both plug into `ICliBackend`.
- **Decoding real songs.** YouTube streams are WebM/Opus or M4A. The WebM reader and an Opus
  decoder are not in this build, so `play` of such a stream exits with code 3 and says so.
- **Playing through speakers.** Output to the sound card (WASAPI on Windows) comes with the
  Windows head; elsewhere `play` writes a WAV.

## Exit codes
`0` ok · `1` usage · `2` failed · `3` can't decode that format yet.
