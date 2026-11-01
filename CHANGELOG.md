# Changelog

## Unreleased: Lyric videos for Reels (app + Companion extension 0.5.1)

- **Reels safe area** for 9:16 cards (on by default): the words, the song name and the cues stay clear of
  Instagram's top bar, the like/comment/share column and the caption and music strip at the bottom. Turn it
  off under Lyric card → Reels safe area to get the old layout. The 9:16 image keeps the top and bottom
  clearance but stays centred, since a picture or Story has no button column on the right.
- **Text size**: Normal, Large or Huge. Large and Huge put fewer words on each screen so the type really
  gets bigger.
- **Animation**: Pop (as before), Fade, Slide up, Typewriter or Karaoke (the whole line shows dim and lights
  up word by word).
- **Start cue** (optional): "Start the song at m:ss" is burned into the first seconds of the video, so you
  still know where to start the song when you pick it in the music picker later. For chosen lines the video
  now starts on a whole second of the song (never before 0:00), so that time is exact.
- **Sync cues** (optional): a 3-2-1 countdown before the first word, a short pulse on each line and the song
  time in the corner, for lining the song up by hand in CapCut, VN or another editor, or for recording a
  voiceover over it. A clip that opens on or inside a line has no room for the countdown; the status line
  says so and the pulses and timecode still show.
- The video is still silent on purpose: Soncle never reads or records the song. Add the song in the
  platform's music picker or your editor so the artist is paid. The status line under the preview now says
  where to start the song for chosen lines too.
- In the app on desktop and Android, and in the Companion extension (0.5.1).

## Unreleased: Soncle Preview for Windows starts songs while they download (C# app)

- Songs now **start playing as soon as the first piece has downloaded** (about a second on a normal
  connection) instead of after the whole song, and the rest downloads while it plays.
- **No more 20-minute limit**: long songs, mixes and videos play, using the same small amount of
  memory as a short song.
- If the connection drops mid-song, the sound fades out once the downloaded part runs out, the player
  bar shows its spinner while it waits, and the song fades back in where it stopped when the network
  returns. Seeking far ahead of the download works too (that part is fetched straight away).
  On a slow connection it waits for a little more audio (a tenth of a second) before fading back in, so a
  download that only just keeps up no longer drops out every moment. If the download gives up mid-song, the
  song fades out instead of stopping with a click.
- A song whose download fails before it starts shows "Couldn't play this song." instead of skipping, and
  going back to an earlier song can no longer cut off the one playing.
- What to check on Windows: time from picking a song to the first sound, a song over 20 minutes,
  seeking far ahead right after a song starts, and turning the network off and on mid-song.
  `uno/heads/Soncle.Windows/README.md` has the steps.
- Under the hood (no change you can see yet): the C# app's proof-of-origin token code now runs Google's
  BotGuard check the same way the desktop app does, so the clients that need a token can be turned on
  once the Windows app can also read YouTube's player script. Until then the Windows preview keeps using
  only the clients that need neither. The Windows preview has the hidden page for it (WebView2, already on
  every Windows 11 PC); it only opens for a self-test (`SONCLE_POTOKEN_SELFTEST=1`) and closes after 10
  minutes without use.

## Unreleased: Soncle Preview for Windows plays songs (C# app)

- The C# / WinUI 3 test build on the **windows-preview** release (`Soncle-preview-win-x64.zip`,
  run `Soncle.Windows.exe` from the folder) now plays music. It is still **Soncle Preview**, a
  separate app with its own data folder; it never reads or writes the shipping app's data.
- What works: type in the **search box in the title bar** (or press Ctrl+K) and press Enter; pick a
  song from the list to play it, with the rest of the results as the queue. The player bar shows the
  artwork, title and artists, play/pause, previous/next and a seek bar with the time, a spinner while
  the song loads and a short message when it can't be played. The Windows media flyout and the media
  keys show and control the song too. Sound goes through the new C# engine (loudness levelled,
  limiter last) to the default output device over WASAPI, and pausing or skipping fades instead of
  clicking.
- What to check on Windows: that search finds what you expect, how long a song takes to start, that
  play/pause/next/seek and the media keys work, and that nothing clicks or stutters (also with a
  44.1 kHz device and after unplugging headphones: the bar should say the sound device stopped, and
  Play should start it again). `uno/heads/Soncle.Windows/README.md` has the steps.
- Not there yet: only songs YouTube offers as WebM/Opus play (AAC-only ones
  say they can't be played), it is signed out only (no account, library, likes or playlists), the
  stream lookup uses only the two YouTube clients that need no PO token and no deciphering (more
  come with the PO-token and player-script work), and there is no queue view, lyrics, Sound page,
  device picker or exclusive mode yet. The player bar's icons are placeholders until the
  Material Symbols font arrives. Playback CPU and memory are still to be measured on a real PC.

## Unreleased: Soncle Uno preview for Android (C# app)

- A first test build of the new C# / Uno Platform Android app is on the **uno-android-preview**
  release (`Soncle-uno-preview-arm64.apk`, 64-bit phones). It installs as a separate app,
  **Soncle Uno**, next to Soncle and Soncle Preview, and shares no data with them; every new build
  installs over the last one.
- What works: the app's shell with Home · Library · Sound · Settings in a bottom bar on a phone and
  in a side rail on a wide screen, the player bar's "Nothing playing" state, and a **Test sound**
  button that plays a built-in 8 s clip on repeat through the new C# audio engine (limiter last).
  It pauses when another app takes the audio, and pausing or resuming fades instead of clicking.
  Settings shows how long the app took to open.
- What to check on your phone: how fast it opens (compare with Soncle), memory while idle and while
  the test sound plays, and whether the sound ever stutters. `uno/heads/Soncle.Android/README.md`
  has the steps.
- Not there yet: songs, search and the library, the pages themselves (each shows a placeholder),
  lock-screen and notification controls (the sound may stop when the app is in the background),
  and icons in the bottom bar.
- Each build is checked before it is published: size against the 30 MB budget, and an emulator run
  that must open the app and start the test sound.

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
