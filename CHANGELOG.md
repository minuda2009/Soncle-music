# Changelog

## Unreleased: Better lyrics engine (desktop and phone)

Bug fix for lyrics that were the wrong version or out of time.

- **One shared engine** (`src/lyrics-engine.mjs`) now finds every LRCLIB version of a song (exact lookup plus two
  searches), scores each against what is playing (title, artist, album, length, live/remix/acoustic tags, and
  whether the timing runs past the end of the song) and picks the best. Before, the first synced result of about
  the right length won, which often meant a live cut or another edit. Failed requests are retried.
- **Versions** button in the lyrics view: lists the other versions (length, synced or text only) and remembers
  your choice per song (included in backups). It reads **Check version** when Soncle is not sure or the timing
  looks wrong (runs past the end of the song, or stops far too early).
- **Groundwork for fixing lyrics by listening** (`src/lyrics-align.mjs`): given the words a speech model hears in
  the song you are playing, it can tell which version matches, measure how far off the timing is (an offset, and
  a small speed difference between edits) and correct it, find lyrics for a song that has none by searching for
  a few heard words, and, as a last resort, write out what was heard, labelled as automatic. All of it is tested
  with made-up songs. **The speech model itself is not in this change** (it needs a model download and a
  decision on size; see the PR), so nothing is listened to yet.
