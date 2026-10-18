# Soncle Companion (browser extension)

Unofficial. Not affiliated with YouTube or Google. It is **strictly not an ad blocker**. It does **not** hide or remove ads, so artists are paid
as usual: this is for people who want some of Soncle's features while staying on the real YouTube Music.

Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later. Written with AI coding agents.

## What it does (version 0.5.0)

- **Soncle's look on YouTube Music (deeper)**: full-page palette from the apps (dark / black / light /
  system; nine accents; or cover-art colours that change per song). Maps a wider set of YTM and yt-spec
  variables, softens nav, player bar, cards, chips, menus and the side guide, and keeps Soncle's font
  stack. Still **only** recolors and rounds — ads and promos stay visible. Toggle under **Style**.

- **Synced lyrics** next to the player, from [LRCLIB](https://lrclib.net), highlighted line by line from the
  page's own player clock. Click a line to jump to it. Per-song timing nudge (−0.5 / −0.1 / +0.1 / +0.5 s).
- **Lyric cards**: pick a few lines and save a picture, or a short silent video, of the words in mixed
  sizes (9:16, 4:5, 1:1) for Reels and Stories. The video can cover chosen lines or a part of the song, up to
  the whole song, from a start point you pick. It has no audio on purpose: add the song in the platform's own
  music picker (the card tells you where to start it) so the artist is paid.

It reads the song title, artist and the player's clock, and talks to lrclib.net. It does not read your
account, cookies or the audio, and it never downloads or alters the stream.

## Try it (Chrome, Edge, Brave)

```
node extension/build.mjs          # writes extension/dist/
```

Then open `chrome://extensions`, switch on **Developer mode**, choose **Load unpacked** and pick
`extension/dist`. Open https://music.youtube.com, play a song and press the **Soncle** button at the
bottom right.

## Soncle Now Playing (Alt+N)

A full-screen player in the app's design over music.youtube.com: large artwork on a soft wash of its colours,
the synced lyrics beside it, a rail (Home, Search, Library, Sound soon, Settings) and Soncle's controls. It
drives the page's own player and tabs and hides nothing, so ads the site plays still play. Its button and tab
lookups are guesses from the site's structure (tested on a mock, not the live site); if one does nothing, the
site's own control still works under it.

## Settings (the **Settings** button in the panel)

One page lists every Soncle function and where it stands: what works now has its control, the rest says
**Coming soon**, **In the Soncle app** (with the reason) or **On YouTube Music** (the site already has it).
The list is `extension/src/features.js`; a test fails if the app gains a settings group it does not cover.

| Works now | Where |
|---|---|
| Soncle style, theme, accent, colours from the cover | Appearance |
| Lyrics: text size, alignment, auto-scroll, glow, delay for all songs, per-song nudge, lyric cards, clear cache | Lyrics |
| Sleep timer (pauses the page's player) | Sleep timer |
| Keyboard: **Alt+L** panel, **Alt+[** / **Alt+]** lyrics 0.1 s earlier/later (Shift: 0.5 s), **Esc** closes | Keyboard shortcuts |

Coming soon: equaliser, AutoEq headphones, normalisation, crossfade/gapless, tempo and pitch, skip silence,
per-device profiles, word-by-word lyrics, queue options, listening stats, Spotify import, a Soncle Now Playing
page. Staying in the app because they need the audio stream or the desktop: Flow radio, smart crossfade,
tempo/key analysis, downloads, local files, Discord presence.

Lyrics are kept in this browser for 30 days (a "not found" for a day) so they appear instantly; a failed
request is tried again twice. Settings has a **Clear cache** button. Nothing else is stored or sent anywhere.

## Honest status

- The page script finds the song through YouTube Music's own elements (`ytmusic-player-bar`, `.title`,
  `.byline`, the `<video>` element). They were written from how the site is built, tested here against a
  copy of that structure, **not** against the live site (this build environment cannot reach it). If the
  button shows "No lyrics" on every song, those selectors are the first thing to check.
- Not on any extension store yet. Firefox is untested.
- The style rules use the colour variables public YouTube Music themes are known to use (general background a/b/c, brand background, player bar, text colours) and a few element names; checked here on a mock page, not the live site. Some surfaces may keep their own colours until the variables are checked against the real page.
- Planned next: sound controls (equaliser, loudness). They would process the page's own player output inside
  the browser, never save or send it, and that changes the "never reads the audio" line above, so it is a
  decision for minuda2009 before it is built. A denser Now Playing layout if the live site still leaves hard-coded surfaces.

## How it shares code

`extension/build.mjs` copies `renderer/lrc.js`, `renderer/palette.js` and `renderer/lyriccard/*` unchanged into the build, so the
apps and the extension stay in step.
