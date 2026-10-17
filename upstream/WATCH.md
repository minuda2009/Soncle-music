# Upstream watch · 2026-10-12

New releases and relevant changes in projects Soncle learns from. Nothing here is applied automatically.

## pear-desktop (pear-devs/pear-desktop)
_YouTube Music desktop app (Electron; formerly th-ch/youtube-music). Same platform as Soncle: sign-in, playback, plugins such as crossfade, equalizer, lyrics, skip silences._

- Nothing relevant since last week.

## youtubei.js (LuanRT/YouTube.js)
_The library Soncle uses for every YouTube request. Most 'YouTube changed something' fixes land here first (Dependabot also opens the version bump)._

- Nothing relevant since last week.

## yt-dlp (yt-dlp/yt-dlp)
_Early warning: yt-dlp usually reacts within hours when YouTube changes stream clients, client versions or PO token rules._


Relevant commits (2 of 2):
- [`8973412`](https://github.com/yt-dlp/yt-dlp/commit/897341241f2bfb2439fe0367f4c7c9ccd29a3828) [ie/youtube] Set `tv_samsung` as a default client (#17805) _(client)_
- [`817450d`](https://github.com/yt-dlp/yt-dlp/commit/817450d28d72e12c9fbac6b4c435ee97e68083ef) [ie/youtube] Add `tv_samsung` client  (#17723) _(client)_

## metrolist (MetrolistGroup/Metrolist)
_Releases and fix commits of the upstream Android app (file-level porting is handled by tools/upstream-sync.mjs)._


Relevant commits (4 of 4):
- [`76fa5a7`](https://github.com/MetrolistGroup/Metrolist/commit/76fa5a78b5110674442884b6f21c0cef51cdb782) fix(innertube): restore artist, owner and byline parsing regressions _(fix)_
- [`ec8fa56`](https://github.com/MetrolistGroup/Metrolist/commit/ec8fa564e5ea28ab03d3027714205c9755428c09) fix(playback): let external controllers resume after task clear _(fix, playback)_
- [`ecdae06`](https://github.com/MetrolistGroup/Metrolist/commit/ecdae06c89f12df1c6b51e209886b39062d77d98) fix(sync): propagate YouTube removals without dropping local-only items _(fix)_
- [`b989947`](https://github.com/MetrolistGroup/Metrolist/commit/b98994745d2a995b92b175f3b3734f5ff8331844) fix(playback): bump InnerTubeX to v0.8.4 (#4436) _(fix, playback)_
