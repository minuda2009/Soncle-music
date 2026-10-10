# Upstream watch · 2026-10-08

New releases and relevant changes in projects Soncle learns from. Nothing here is applied automatically.

## pear-desktop (pear-devs/pear-desktop)
_YouTube Music desktop app (Electron; formerly th-ch/youtube-music). Same platform as Soncle: sign-in, playback, plugins such as crossfade, equalizer, lyrics, skip silences._

- Nothing relevant since last week.

## youtubei.js (LuanRT/YouTube.js)
_The library Soncle uses for every YouTube request. Most 'YouTube changed something' fixes land here first (Dependabot also opens the version bump)._

- Nothing relevant since last week.

## yt-dlp (yt-dlp/yt-dlp)
_Early warning: yt-dlp usually reacts within hours when YouTube changes stream clients, client versions or PO token rules._

- Nothing relevant since last week.

## metrolist (MetrolistGroup/Metrolist)
_Releases and fix commits of the upstream Android app (file-level porting is handled by tools/upstream-sync.mjs)._


Relevant commits (7 of 7):
- [`fe33568`](https://github.com/MetrolistGroup/Metrolist/commit/fe33568eecb6a61566e76165668618133a8e2c38) fix(playback): keep shuffle order across crossfade and player rebuilds _(fix, playback, crossfade)_
- [`1e673d3`](https://github.com/MetrolistGroup/Metrolist/commit/1e673d36bfb72c3fa973ad22fc1b12747d88de77) fix(ai): omit parameters OpenAI GPT-5 models reject (#4401) _(fix)_
- [`0a035d7`](https://github.com/MetrolistGroup/Metrolist/commit/0a035d77fb37940fc4958651756f16119e22e129) fix(lyrics): draw joined Indic scripts as whole lines (#3467) _(fix, lyrics)_
- [`6301969`](https://github.com/MetrolistGroup/Metrolist/commit/63019699da9a5ec3c8c3f9c1b3379f7641e6bf2a) fix(playlist): sort by artist then album (#4418) _(fix)_
- [`71173b3`](https://github.com/MetrolistGroup/Metrolist/commit/71173b3c2eca826206d3219361147d63f5dd88bb) fix(queue): keep the selected start song when filtering hidden items (#4420) _(fix)_
- [`9dbe585`](https://github.com/MetrolistGroup/Metrolist/commit/9dbe585f65a6950e9a8c088f1603e01742401838) fix(downloads): restore unthrottled range requests and offline length lookup (#4369) _(fix)_
- [`5ceae5f`](https://github.com/MetrolistGroup/Metrolist/commit/5ceae5f4e709f1ea5042a984e11835ff344bdaca) fix(playback): bump InnerTubeX to v0.7.4 (#4372) _(fix, playback)_
