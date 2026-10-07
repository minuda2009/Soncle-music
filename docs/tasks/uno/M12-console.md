# M12 — Console app: search, resolve, download, decode, play

_Branch: `openhands/uno-m12-console`. Needs: M06, M07, M08, M10. Read `README.md` in this folder
first._

## Goal
The first end-to-end proof that the C# libraries work against real YouTube Music, before any UI.
It's a small command-line tool that minuda2009 runs on his own PC. The CI part stays offline.

## Build
- **`uno/tools/Soncle.Cli`** (`net10.0` console). Commands:
  - `search <query>`: prints the top songs, albums and artists;
  - `resolve <videoId>`: prints the client used, mime, bitrate, length and loudness. No URLs or
    tokens are printed;
  - `download <videoId> <file>`: uses `Soncle.Streams` and prints progress;
  - `play <videoId>`: decodes and plays through the default output device.
    - **Decoding:** WebM/Opus via the WebM reader from M05 plus Concentus (add it to
      THIRD_PARTY_NOTICES), and M4A via Media Foundation on Windows only.
    - **Output:** NAudio `WasapiOut` on Windows.
    - Elsewhere, `play` writes a WAV file instead.
  - `analyze <videoId>`: runs Flow analysis (tempo/key/energy) on a real song.
- **No PO tokens yet** (they need a head's WebView). Configure the resolver with the clients that
  don't need them, and print a clear line when a song needs one.
- **No sign-in.** Anonymous only. Never read or print cookies.

## Acceptance
- **CI (offline):** the commands run against the fake handler and fixtures; `play` writes a WAV
  whose loudness is at the normalisation target ±0.5 LU.
- **On minuda2009's PC:** a short "how to run" in `uno/tools/Soncle.Cli/README.md`. In the PR he
  confirms:
  - `search` and `resolve` work for 3 songs of his choice;
  - `play` sounds right compared with the Electron app at the same volume.
- **Report** the peak memory and CPU of `play` over 5 minutes (from the tool's own counters).

## After this task
Stage 1 and 2 of the plan are done once M02–M12 are merged. The next task files (design system,
then the first head) get written after minuda2009 records the head-order decision at the top of
`docs/WINUI_PLAN.md`.
