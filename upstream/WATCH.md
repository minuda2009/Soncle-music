# Upstream watch · 2026-11-16

New releases and relevant changes in projects Soncle learns from. Nothing here is applied automatically.

## pear-desktop (pear-devs/pear-desktop)
_YouTube Music desktop app (Electron; formerly th-ch/youtube-music). Same platform as Soncle: sign-in, playback, plugins such as crossfade, equalizer, lyrics, skip silences._

- Release **v3.12.1**, 2026-11-15: https://github.com/pear-devs/pear-desktop/releases/tag/v3.12.1

Relevant commits (4 of 8):
- [`a417061`](https://github.com/pear-devs/pear-desktop/commit/a417061c53646c2a6c9b943510cf40ad11cb79dc) fix: fix CI _(fix)_
- [`00adc89`](https://github.com/pear-devs/pear-desktop/commit/00adc89724218ac03517818d24f7b2399c4807dc) fix: fix deps version, remove ia32/armv7 support _(fix)_
- [`21a5303`](https://github.com/pear-devs/pear-desktop/commit/21a5303041706d3b9709e31b04c65055f2614258) fix: bump deps _(fix)_
- [`da1d943`](https://github.com/pear-devs/pear-desktop/commit/da1d943ddb37004f581bdd077393b86d58626c43) fix: fix layout _(fix)_

Watched files changed (8); full diff: https://github.com/pear-devs/pear-desktop/compare/3337446b448454125ad261099a25f6b10fbb8cb6...ec52f56a930c0f6bd6274b4b767660cc0839945b
- `src/plugins/audio-compressor.ts`
- `src/plugins/downloader/main/index.ts`
- `src/plugins/downloader/style.css`
- `src/plugins/downloader/templates/download.tsx`
- `src/plugins/synced-lyrics/providers/MusixMatch.ts`
- `src/plugins/synced-lyrics/providers/index.ts`
- `src/plugins/synced-lyrics/providers/renderer.ts`
- `src/providers/song-info-front.ts`
- Compare with ours: `src/main.mjs`, `renderer/app.js`, `renderer/engine.js`

## youtubei.js (LuanRT/YouTube.js)
_The library Soncle uses for every YouTube request. Most 'YouTube changed something' fixes land here first (Dependabot also opens the version bump)._

- Nothing relevant since last week.

## yt-dlp (yt-dlp/yt-dlp)
_Early warning: yt-dlp usually reacts within hours when YouTube changes stream clients, client versions or PO token rules._


Relevant commits (2 of 3):
- [`6c6d097`](https://github.com/yt-dlp/yt-dlp/commit/6c6d09761a0368c19da5795fff4fe4b4276209a4) [ie/youtube] Fix post-live fragment generation (#18058) _(fix)_
- [`ee25591`](https://github.com/yt-dlp/yt-dlp/commit/ee2559128c7ddc471de5aeaa584a9a4ae6836ec3) [ie/youtube] Fix `web_embedded` client policies and deprioritize (#18051) _(fix, client)_

## metrolist (MetrolistGroup/Metrolist)
_Releases and fix commits of the upstream Android app (file-level porting is handled by tools/upstream-sync.mjs)._

- Upstream history was rewritten; watching again from its latest commit.
- Release **v14.0.0**, 2026-11-14: https://github.com/MetrolistGroup/Metrolist/releases/tag/v14.0.0
