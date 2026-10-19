# Upstream watch · 2026-10-19

New releases and relevant changes in projects Soncle learns from. Nothing here is applied automatically.

## pear-desktop (pear-devs/pear-desktop)
_YouTube Music desktop app (Electron; formerly th-ch/youtube-music). Same platform as Soncle: sign-in, playback, plugins such as crossfade, equalizer, lyrics, skip silences._

- Nothing relevant since last week.

## youtubei.js (LuanRT/YouTube.js)
_The library Soncle uses for every YouTube request. Most 'YouTube changed something' fixes land here first (Dependabot also opens the version bump)._

- Release **v18.2.0**, 2026-10-18: https://github.com/LuanRT/YouTube.js/releases/tag/v18.2.0

Relevant commits (6 of 17):
- [`32323cd`](https://github.com/LuanRT/YouTube.js/commit/32323cdda631b5e4351fffdf49a9509e4fba1abd) fix(parser): Improve storyboard data parsing _(fix)_
- [`6dec600`](https://github.com/LuanRT/YouTube.js/commit/6dec600dadc02b30480b63b3a9791f18ac974d3b) chore(Feed): Fix `page_contents` return type _(fix)_
- [`20e3251`](https://github.com/LuanRT/YouTube.js/commit/20e32519fde1ac0d106edc72e4435aca4543c0cb) fix(StructuredDescriptionContent): Add `VideoTitleHeaderView` type to `items` _(fix)_
- [`fb801f9`](https://github.com/LuanRT/YouTube.js/commit/fb801f9ea0cbe9445ab698be1b810373d65b5315) fix(StructuredDescriptionContent): Add `ItemSection` type to `items` _(fix)_
- [`3048496`](https://github.com/LuanRT/YouTube.js/commit/3048496b5f99a8040ebf4ce02be9710f2df88991) fix(Tab): Add `Message` type to `content` _(fix)_
- [`b95ee26`](https://github.com/LuanRT/YouTube.js/commit/b95ee26f2d63f9eb210e64cde11675850fabc8a1) fix(LockupView): `duration` parsing large values incorrectly (#1308) _(fix)_

## yt-dlp (yt-dlp/yt-dlp)
_Early warning: yt-dlp usually reacts within hours when YouTube changes stream clients, client versions or PO token rules._

- Nothing relevant since last week.

## metrolist (MetrolistGroup/Metrolist)
_Releases and fix commits of the upstream Android app (file-level porting is handled by tools/upstream-sync.mjs)._

- Nothing relevant since last week.
