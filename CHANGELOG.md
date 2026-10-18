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

## Unreleased: Companion extension 0.5 — Soncle Now Playing

- **Soncle Now Playing** (Alt+N, or Full screen in the panel, or Settings → Appearance): a full-screen player in
  the app's design: big artwork over a soft wash of its colours, the synced lyrics beside it, Soncle's controls
  (play/pause, previous/next, seek, volume), a rail with Home / Search / Library / Sound (coming soon) /
  Settings, and a Lyric card button. On a phone the rail becomes a bottom bar.
- It only drives YouTube Music's own player and tabs and hides nothing: ads the site plays still play, artists
  are paid as usual, Esc brings the site back. It never reads or records the audio.
- Not yet on the live site: the page's own button/tab names are guessed from how it is built (tested on a mock).
  If previous/next or the rail tabs do nothing, tell me.
- Still to come toward the app's full look: Home, Library, search and album pages redrawn (colours and shapes
  are already done), a queue view, the Sound page.

## Unreleased: Companion extension 0.4.1 — whole-song lyric videos

- The extension's lyric card gets what the app already had: **Video from → Part of the song**, with a start
  point, a length (15 s, 30 s, 60 s or the whole song, up to 6 min) and a share button on phones. The video is
  silent and says where to start the song in Instagram's, TikTok's or YouTube Shorts' music picker so the words
  match and the artist is paid through the platform. Song name can be hidden.
- Still no audio in the video: the extension does not copy the song.

## Unreleased: Companion extension 0.4 — settings page, lyrics cache, shortcuts

- **Settings page** in the panel listing every Soncle function. What works has its control; the rest says
  **Coming soon**, **In the Soncle app** (with the reason) or **On YouTube Music**. A test keeps it in step
  with the app's settings groups.
- New working options: lyrics text size, alignment, auto-scroll, glow, a delay for all songs, a sleep timer
  (pauses the page's player), keyboard shortcuts (Alt+L, Alt+[ / Alt+], Esc) and small confirmation toasts.
- Lyrics are cached in the browser (30 days; "not found" 1 day), failed requests are retried, and there is a
  **Clear cache** button. Being offline is never remembered as "no lyrics".
- The page now reacts to song changes (MutationObserver) instead of checking every 0.7 s.
- Still strictly not an ad blocker, and the lyric video stays silent: the extension does not read, record or
  save the song's audio. (Ideas that would have: audio in lyric videos, full-song recording, uploads, usage
  reporting — left out on purpose.)
- The extension's build is also published as a single `soncle-companion-<version>.zip` from the Build extension workflow.
- Version **0.4.0**.

## Unreleased: Companion extension 0.3 — deeper look + feature panel

- **Deeper Soncle theming** of music.youtube.com (more YTM / yt-spec variables, nav, player bar, cards, chips).
- **Feature panel** lists every Soncle capability; unfinished items show **Coming soon**.
- Stated clearly: the extension is **strictly not an ad blocker** — ads stay.
- Version **0.3.0**.

## Unreleased: Soncle's look for YouTube Music (extension 0.2)

- The extension recolours music.youtube.com with Soncle's palette: dark, black, light or system theme, nine
  accents, or colours taken from the cover of the current song, with rounded cards and covers. It points the
  site's own colour variables at Soncle's (so it survives markup changes) and only recolours and rounds: it
  hides and blocks nothing, ads included. Settings under **Style** in the panel. Checked on a mock page, not
  the live site.
- The palette code moved out of `renderer/app.js` into `renderer/palette.js` (shared with the extension). A
  golden test (`fixtures/palette/golden.json`, made from the old code) proves the colours are unchanged.

## Unreleased: Soncle Companion, a browser extension (first slice)

- **`extension/`**: an unofficial extension for music.youtube.com that adds synced lyrics (LRCLIB), a per-song
  timing nudge and the lyric cards, while leaving the real YouTube Music as it is. It does not block
  ads, so artists are paid as usual. Build with `node extension/build.mjs`, load `extension/dist` unpacked
  (see `extension/README.md`). Tested here against a copy of the page structure, not the live site.
- `parseLrc` moved from `renderer/app.js` to `renderer/lrc.js` so the apps and the extension share one parser.
