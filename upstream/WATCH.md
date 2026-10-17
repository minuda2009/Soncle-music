# Upstream watch · 2026-10-17

New releases and relevant changes in projects Soncle learns from. Nothing here is applied automatically.

## pear-desktop (pear-devs/pear-desktop)
_YouTube Music desktop app (Electron; formerly th-ch/youtube-music). Same platform as Soncle: sign-in, playback, plugins such as crossfade, equalizer, lyrics, skip silences._

- Nothing relevant since last week.

## youtubei.js (LuanRT/YouTube.js)
_The library Soncle uses for every YouTube request. Most 'YouTube changed something' fixes land here first (Dependabot also opens the version bump)._


Relevant commits (6 of 19):
- [`cb4a44e`](https://github.com/LuanRT/YouTube.js/commit/cb4a44e508cbc04d30cb9b774da86fa884917743) fix(LockupView): `duration` getter not working with very long videos (#1306) _(fix)_
- [`02c5c8d`](https://github.com/LuanRT/YouTube.js/commit/02c5c8d4c48fd9d9e87be6337ae99833e9a9c879) fix(parser): Add `ToggleableListItemView` class (#1282) _(fix)_
- [`710900a`](https://github.com/LuanRT/YouTube.js/commit/710900af6c76f469d4be522e5e19cd1debd6baf6) fix: Handle missing session config and import `Text` in `ToggleFormField` (#1276) _(fix)_
- [`f268ba1`](https://github.com/LuanRT/YouTube.js/commit/f268ba1f57995cc3bc751af2c824a0b87b917a68) fix(LockupMetadataView): Handle missing titles (#1278) _(fix)_
- [`935364f`](https://github.com/LuanRT/YouTube.js/commit/935364fed3fd042213231d08bd544fa6f42d2165) fix(Video): `is_live` getter not working (#1291) _(fix)_
- [`ae4e655`](https://github.com/LuanRT/YouTube.js/commit/ae4e655587f839bf732ff2ddc93cc4258de79c7c) fix(LockupView): Parser error when `content_type` is `undefined` (#1297) _(fix)_

## yt-dlp (yt-dlp/yt-dlp)
_Early warning: yt-dlp usually reacts within hours when YouTube changes stream clients, client versions or PO token rules._

- Nothing relevant since last week.

## metrolist (MetrolistGroup/Metrolist)
_Releases and fix commits of the upstream Android app (file-level porting is handled by tools/upstream-sync.mjs)._

- Nothing relevant since last week.
