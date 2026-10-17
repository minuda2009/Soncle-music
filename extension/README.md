# Soncle Companion (browser extension)

Unofficial. Not affiliated with YouTube or Google. It is **strictly not an ad blocker**. It does **not** hide or remove ads, so artists are paid
as usual: this is for people who want some of Soncle's features while staying on the real YouTube Music.

Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later. Written with AI coding agents.

## What it does (version 0.3.0)

- **Soncle's look on YouTube Music (deeper)**: full-page palette from the apps (dark / black / light /
  system; nine accents; or cover-art colours that change per song). Maps a wider set of YTM and yt-spec
  variables, softens nav, player bar, cards, chips, menus and the side guide, and keeps Soncle's font
  stack. Still **only** recolors and rounds — ads and promos stay visible. Toggle under **Style**.

- **Synced lyrics** next to the player, from [LRCLIB](https://lrclib.net), highlighted line by line from the
  page's own player clock. Click a line to jump to it. Per-song timing nudge (−0.5 / −0.1 / +0.1 / +0.5 s).
- **Lyric cards**: pick a few lines and save a picture, or a short silent video, of the words in mixed
  sizes (9:16, 4:5, 1:1) for Reels and Stories. The video has no audio on purpose: add the song in
  Instagram's own music picker.

It reads the song title, artist and the player's clock, and talks to lrclib.net. It does not read your
account, cookies or the audio, and it never downloads or alters the stream.

## Try it (Chrome, Edge, Brave)

```
node extension/build.mjs          # writes extension/dist/
```

Then open `chrome://extensions`, switch on **Developer mode**, choose **Load unpacked** and pick
`extension/dist`. Open https://music.youtube.com, play a song and press the **Soncle** button at the
bottom right.

## Features

| Feature | Status |
|---|---|
| Soncle look (theme, accent, cover colours) | **On** |
| Synced lyrics (LRCLIB) + timing nudge | **On** |
| Lyric cards (image / silent video) | **On** |
| Equaliser | Coming soon |
| Loudness (LUFS) | Coming soon |
| True-peak limiter | Coming soon |
| Gapless / crossfade | Coming soon |
| AutoEq headphones | Coming soon |
| Keyboard shortcuts | Coming soon |
| Flow radio | Coming soon |

## Honest status

- The page script finds the song through YouTube Music's own elements (`ytmusic-player-bar`, `.title`,
  `.byline`, the `<video>` element). They were written from how the site is built, tested here against a
  copy of that structure, **not** against the live site (this build environment cannot reach it). If the
  button shows "No lyrics" on every song, those selectors are the first thing to check.
- Not on any extension store yet. Firefox is untested.
- The style rules use the colour variables public YouTube Music themes are known to use (general background a/b/c, brand background, player bar, text colours) and a few element names; checked here on a mock page, not the live site. Some surfaces may keep their own colours until the variables are checked against the real page.
- Planned next: equaliser and loudness through Web Audio on the page's own player, keyboard shortcuts,
  a denser Now Playing layout if the live site still leaves hard-coded surfaces.

## How it shares code

`extension/build.mjs` copies `renderer/lrc.js`, `renderer/palette.js` and `renderer/lyriccard/*` unchanged into the build, so the
apps and the extension stay in step.
