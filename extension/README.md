# Soncle Companion (browser extension)

Unofficial. Not affiliated with YouTube or Google. It does **not** block ads, so artists are paid
as usual: this is for people who want some of Soncle's features while staying on the real YouTube Music.

Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later. Written with AI coding agents.

## What it does (version 0.1)

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

## Honest status

- The page script finds the song through YouTube Music's own elements (`ytmusic-player-bar`, `.title`,
  `.byline`, the `<video>` element). They were written from how the site is built, tested here against a
  copy of that structure, **not** against the live site (this build environment cannot reach it). If the
  button shows "No lyrics" on every song, those selectors are the first thing to check.
- Not on any extension store yet. Firefox is untested.
- Planned next: Soncle's colours on the page, equaliser and loudness through Web Audio on the page's own
  player, keyboard shortcuts.

## How it shares code

`extension/build.mjs` copies `renderer/lrc.js` and `renderer/lyriccard/*` unchanged into the build, so the
apps and the extension stay in step.
