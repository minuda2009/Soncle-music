# Upstream watch · 2026-10-26

New releases and relevant changes in projects Soncle learns from. Nothing here is applied automatically.

## pear-desktop (pear-devs/pear-desktop)
_YouTube Music desktop app (Electron; formerly th-ch/youtube-music). Same platform as Soncle: sign-in, playback, plugins such as crossfade, equalizer, lyrics, skip silences._

- Nothing relevant since last week.

## youtubei.js (LuanRT/YouTube.js)
_The library Soncle uses for every YouTube request. Most 'YouTube changed something' fixes land here first (Dependabot also opens the version bump)._

- Release **v18.3.0**, 2026-10-23: https://github.com/LuanRT/YouTube.js/releases/tag/v18.3.0

Watched files changed (1); full diff: https://github.com/LuanRT/YouTube.js/compare/c4a7b3f876114fea656de3d26ca9dfade94aa034...204715b6acfb063dc306d8677e36fdbf09f293c5
- `src/core/managers/PlaylistManager.ts`
- Compare with ours: `src/yt.mjs`

## yt-dlp (yt-dlp/yt-dlp)
_Early warning: yt-dlp usually reacts within hours when YouTube changes stream clients, client versions or PO token rules._

- Release **2026.10.21**, 2026-10-21: https://github.com/yt-dlp/yt-dlp/releases/tag/2026.10.21

Relevant commits (1 of 13):
- [`12ac9ac`](https://github.com/yt-dlp/yt-dlp/commit/12ac9aca79671420724227aca37bc888c604a6f8) [ie/youtube] Fix initial data extraction (#17703) _(fix)_

## metrolist (MetrolistGroup/Metrolist)
_Releases and fix commits of the upstream Android app (file-level porting is handled by tools/upstream-sync.mjs)._


Relevant commits (5 of 48):
- [`c4c61fb`](https://github.com/MetrolistGroup/Metrolist/commit/c4c61fb3b54fc440c9a61094399c7c644dcc410f) fix(i18n): correct Catalan radio and alarm text _(fix)_
- [`68e6347`](https://github.com/MetrolistGroup/Metrolist/commit/68e6347b12219e7cfe781f0703b0240ff683eafd) fix(i18n): repair Weblate resource conflicts and invalid translations _(fix)_
- [`fae57e7`](https://github.com/MetrolistGroup/Metrolist/commit/fae57e7f5924405e76285b97582ed9593b7d349d) fix(ux): expose queue removal in contextual menu (#4457) _(fix)_
- [`2872811`](https://github.com/MetrolistGroup/Metrolist/commit/2872811c8f6d5943dad903fcd15c28918603f9b6) fix(player): control playback from fullscreen lyrics thumbnail (#4425) _(fix, playback, lyrics)_
- [`40d9c70`](https://github.com/MetrolistGroup/Metrolist/commit/40d9c70d157e1eb12a21986b0179ad8aad3063bc) fix(playback): report YouTube Music history while listening _(fix, playback)_
