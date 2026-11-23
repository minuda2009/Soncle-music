# Upstream watch · 2026-11-23

New releases and relevant changes in projects Soncle learns from. Nothing here is applied automatically.

## pear-desktop (pear-devs/pear-desktop)
_YouTube Music desktop app (Electron; formerly th-ch/youtube-music). Same platform as Soncle: sign-in, playback, plugins such as crossfade, equalizer, lyrics, skip silences._

- Nothing relevant since last week.

## youtubei.js (LuanRT/YouTube.js)
_The library Soncle uses for every YouTube request. Most 'YouTube changed something' fixes land here first (Dependabot also opens the version bump)._

- Nothing relevant since last week.

## yt-dlp (yt-dlp/yt-dlp)
_Early warning: yt-dlp usually reacts within hours when YouTube changes stream clients, client versions or PO token rules._


Relevant commits (2 of 22):
- [`7940e14`](https://github.com/yt-dlp/yt-dlp/commit/7940e14e3fdda9b6030053bc5800060e0e416ce1) [ie/youtube] Add `ios_producer` player client (#17866) _(client, player)_
- [`f66c9c7`](https://github.com/yt-dlp/yt-dlp/commit/f66c9c725ee92c3fc1853ac301ebfb4f40e1bee8) [ie/youtube] Fix live adaptive formats with `dashy` extractor-arg (#18092) _(fix)_

## metrolist (MetrolistGroup/Metrolist)
_Releases and fix commits of the upstream Android app (file-level porting is handled by tools/upstream-sync.mjs)._

- Release **v14.0.1**, 2026-11-19: https://github.com/MetrolistGroup/Metrolist/releases/tag/v14.0.1

Relevant commits (117 of 878):
- [`3403049`](https://github.com/MetrolistGroup/Metrolist/commit/3403049bd84a1865f323e073ee66d04d9f701537) fix(android): let sign-in finish when YouTube Music never reports a page load _(fix)_
- [`39fa99b`](https://github.com/MetrolistGroup/Metrolist/commit/39fa99bffdd1a4745ba10af5e2d27851a5b8494e) fix(desktop): stop applying video frame snapshots on the decoder thread _(fix, desktop)_
- [`460a316`](https://github.com/MetrolistGroup/Metrolist/commit/460a3168ac280061ec56f68913cd2d01e6447175) fix(canvas): fetch Spotify canvases from Spotify directly _(fix)_
- [`83fa2ca`](https://github.com/MetrolistGroup/Metrolist/commit/83fa2ca2408e67e75e0349d9ca9b9e663f84329c) fix(backup): keep v13 artist edits when converting the library _(fix)_
- [`685ccfd`](https://github.com/MetrolistGroup/Metrolist/commit/685ccfd59b219f3572679859eb98ac3ac7df607d) fix(import): keep ViMusic and SimpMusic playlists as local copies _(fix)_
- [`4856c6b`](https://github.com/MetrolistGroup/Metrolist/commit/4856c6bba619e3f483318419dbb08e4a04c077f5) fix(album): open downloaded albums locally and cache descriptions _(fix)_
- [`a6d60dc`](https://github.com/MetrolistGroup/Metrolist/commit/a6d60dca3cb9cbe04abbe5a90dc2dadf7a1b4282) fix(library): count an empty Episodes for later list as synced _(fix)_
- [`5faf258`](https://github.com/MetrolistGroup/Metrolist/commit/5faf258b38d353359e4d8917899b864ec9c4f3bc) fix(locale): send US for unserved regions without a same-language region (#5257) _(fix)_
- [`ce99dbd`](https://github.com/MetrolistGroup/Metrolist/commit/ce99dbd73d5305988653a3916683044a961b4d0c) fix(locale): send YouTube-accepted language tags (#5251) _(fix)_
- [`d575282`](https://github.com/MetrolistGroup/Metrolist/commit/d5752825db9ece2269ef941114b12c19b78ec43b) fix(ios): play AAC instead of remuxed Opus before iOS 17 (#5249) _(fix)_
- [`44a0034`](https://github.com/MetrolistGroup/Metrolist/commit/44a0034b98795c78b1675d1df5c509ebf3fbd9ae) fix(playlist): stop waiting for endless mix continuations (#5237) _(fix)_
- [`dc7865f`](https://github.com/MetrolistGroup/Metrolist/commit/dc7865f74a934a10e0c2338821ea43261b20953b) fix(desktop): remux downloads instead of transcoding to AAC (#5236) _(fix, desktop)_
- [`49803a4`](https://github.com/MetrolistGroup/Metrolist/commit/49803a4dc7afc086af5b951039d46e266519f4c1) fix(parser): keep greyed playlist rows playable (#5234) _(fix)_
- [`e243175`](https://github.com/MetrolistGroup/Metrolist/commit/e243175940ffdde6731237ec27327a76b90e898e) fix(details): restore upload date and like count on song details (#5238) _(fix)_
- [`f00d67b`](https://github.com/MetrolistGroup/Metrolist/commit/f00d67b9a37552f1d2ad7942b636616e34cb3f13) fix(library): queue library playlists with the play loader and report failures (#5235) _(fix)_
- [`06a4d11`](https://github.com/MetrolistGroup/Metrolist/commit/06a4d119760bf52e796978173383e91953e60be2) fix(home): keep hidden sections hidden when YouTube rotates shelf params (#5230) _(fix)_
- [`e40a04d`](https://github.com/MetrolistGroup/Metrolist/commit/e40a04d22e23b844ba58613e2aa7d113d8c58c36) fix(locale): map unserved system regions to a YouTube Music region (#5228) _(fix)_
- [`db10fc9`](https://github.com/MetrolistGroup/Metrolist/commit/db10fc94ce28040221843b0780b6d972e20e5610) fix(android): preserve persisted queue during transient empty state (#5157) _(fix)_
- [`52d0763`](https://github.com/MetrolistGroup/Metrolist/commit/52d0763becc07ea4f0230c94f70fd7e9ddde9850) fix(cast): send real track duration to DLNA renderers (#5229) _(fix)_
- [`e15341e`](https://github.com/MetrolistGroup/Metrolist/commit/e15341e081454a7ef7db45efc00bf347409d9ba7) fix(desktop): support non-QWERTY shortcuts (#5078) _(fix, desktop)_
- [`cebc487`](https://github.com/MetrolistGroup/Metrolist/commit/cebc487d1d626428ddb15f4b93934046bd422505) fix(desktop): stop macOS WebView and accessibility deadlock (#5227) _(fix, desktop)_
- [`c9dd273`](https://github.com/MetrolistGroup/Metrolist/commit/c9dd27378d4d4ee0f06e246b1583db08f2f12e46) fix(desktop): start scrobble cleanup after Koin (#5154) _(fix, desktop)_
- [`0d43806`](https://github.com/MetrolistGroup/Metrolist/commit/0d438064619298bd0356783690444d441a4aadd1) fix(android): rearm watchdog after offload fallback (#5155) _(fix)_
- [`89e8eca`](https://github.com/MetrolistGroup/Metrolist/commit/89e8eca2ee3cacdb42b670af6befad48c964e915) fix(sheet): accept input while visible sheet settles (#5158) _(fix)_
- [`2d686d4`](https://github.com/MetrolistGroup/Metrolist/commit/2d686d4ee8440e560dacaf0d0d34e4ff6c234ecb) perf(desktop): add process-tree profiler and cut UI and playback overhead _(playback, desktop)_
- [`7e0146e`](https://github.com/MetrolistGroup/Metrolist/commit/7e0146e65b8e66dd13a54b3e75d8b0e1157af1cb) fix(android): preserve Unicode export filenames _(fix)_
- [`0882dc1`](https://github.com/MetrolistGroup/Metrolist/commit/0882dc1c483d3c6a5e18898f82039991ccca86c3) fix(desktop): align dropped frame stats _(fix, desktop)_
- [`fe1ab64`](https://github.com/MetrolistGroup/Metrolist/commit/fe1ab64058758a6d343eacd96286d717f5e2b45d) fix: zap the WebView profile under an absolute XDG_DATA_HOME _(fix)_
- [`484f1d2`](https://github.com/MetrolistGroup/Metrolist/commit/484f1d292586046574dcda9cbdcf78a95b7d0b46) fix: declare the Homebrew cask macOS minimum and WebView cleanup _(fix)_
- [`587695e`](https://github.com/MetrolistGroup/Metrolist/commit/587695eebf19a43cbdc6d7aa3bfa3c7b0779f207) fix(android): keep PiP ratios within system bounds _(fix)_
- [`4a00434`](https://github.com/MetrolistGroup/Metrolist/commit/4a00434219db93119104a0013a0798d4647ab45f) fix(desktop): seek shortcuts using playback duration _(fix, playback, desktop)_
- [`ac53670`](https://github.com/MetrolistGroup/Metrolist/commit/ac53670609274fdb5daf71122af822a20201b5ca) fix(cast): preserve whole volume percentages _(fix)_
- [`c118db2`](https://github.com/MetrolistGroup/Metrolist/commit/c118db25606ffc454965baa37926100065e9c6f4) fix(desktop): preserve exports when likes fail to load _(fix, desktop)_
- [`5f58b40`](https://github.com/MetrolistGroup/Metrolist/commit/5f58b40817399c59af3cd80aec3890e9ee22652b) fix(lyrics): align countdowns with word timing _(fix, lyrics)_
- [`a19d8e9`](https://github.com/MetrolistGroup/Metrolist/commit/a19d8e95772b959e629e30fa3498332b041eb51d) fix(links): ignore party links without a room code _(fix)_
- [`ad3dfed`](https://github.com/MetrolistGroup/Metrolist/commit/ad3dfed60de92e5c2a758162e2bcb928058fb966) fix(desktop): play episodes from quick search _(fix, desktop)_
- [`cd27c46`](https://github.com/MetrolistGroup/Metrolist/commit/cd27c464b120124fb2cfcacbbdc7580a6529cbed) feat(android): pause playback when leaving the app on Android TV (#5209) _(playback)_
- [`31373ec`](https://github.com/MetrolistGroup/Metrolist/commit/31373ecfd3b1ea5c1cfb9bcc69d1183d5cc0b34a) fix(playback): play saved files for non-video songs in video mode (#5207) _(fix, playback)_
- [`07b14b8`](https://github.com/MetrolistGroup/Metrolist/commit/07b14b8570f64ea463db64ea5ad5193eebdffb3c) fix(widget): use a linear progress bar for widget playback progress _(fix, playback)_
- [`b0c831b`](https://github.com/MetrolistGroup/Metrolist/commit/b0c831b35a345c9469dde3ac99ca479bb37647c3) fix(android): remove duplicate loading indicator on update screen (#5199) _(fix)_
- …and 77 more: https://github.com/MetrolistGroup/Metrolist/compare/36bb564b4a148c6b415dc3c1c74faf790ae76bc6...3403049bd84a1865f323e073ee66d04d9f701537
