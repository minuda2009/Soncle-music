# Third-party notices

Soncle is free software under the GNU General Public License, version 3 or (at your option) any
later version (see [LICENSE](./LICENSE)). It contains or is derived from the works below, each
under its own licence. Listing a work here is attribution, not affiliation: Soncle is an
independent, unofficial project, not affiliated with or endorsed by any of these projects or by
Google/YouTube.

## Metrolist for Android — GPL-3.0

<https://github.com/MetrolistGroup/Metrolist> · Copyright © the Metrolist contributors ·
[GPL-3.0](https://github.com/MetrolistGroup/Metrolist/blob/main/LICENSE)

Soncle began as a desktop port of Metrolist, the YouTube Music client for Android. It is a separate
project with its own name, and it is not affiliated with or endorsed by Metrolist Group. These
parts are derived from Metrolist:

| Used here | Taken from | How |
| --- | --- | --- |
| `src/po_token.html` | `app/src/main/assets/po_token.html` | Byte-for-byte copy |
| `src/potoken.mjs` | `app/src/main/kotlin/com/metrolist/music/utils/potoken/PoTokenWebView.kt` | Ported from Kotlin to JavaScript. `REQUEST_KEY` and `GOOGLE_API_KEY` are copied unchanged. They are public identifiers used by YouTube's web player, not secrets. |
| Stream client order (`src/yt.mjs`), the "smooth" crossfade curve (`renderer/engine.js`), lyrics and page motion timings (`renderer/app.js`, `renderer/styles.css`) | The Android app's behaviour and source | Reimplemented in JavaScript |

In code comments these are referred to as "the upstream Android app".
`tools/upstream-sync.mjs` and `upstream/upstream.json` track the files listed above for fixes. They
are developer tools only and are not part of the app.

Earlier builds also used Metrolist's app icon and some of its custom vector drawables. Both were
replaced in 1.8.0: the app icon is Soncle's own (`assets/soncle-icon.svg`, drawn by
`tools/make-icon.mjs`), and every UI icon now comes from Material Symbols (below).

## Material Symbols — Apache-2.0

<https://github.com/google/material-design-icons> · Copyright © Google LLC ·
[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

All UI icons in `renderer/icons.js` are Material Symbols (outlined, weight 600; "-fill" variants
for filled states), taken unchanged from the official `@material-symbols/svg-600` package.
`tools/icon-sources.json` names the symbol behind each icon. Apache-2.0 is compatible with
GPL-3.0; keep this notice with them.

## AutoEq headphone measurements — MIT

<https://github.com/jaakkopasanen/AutoEq> · Copyright © 2018-2022 Jaakko Pasanen · MIT License

Headphone correction uses AutoEq's published parametric EQ results. They are downloaded at runtime
when you search for or choose your headphones, and cached locally. No AutoEq data ships inside the
app. Measurements are credited in the app to their original sources (oratory1990, crinacle, Rtings
and others).

## Runtime dependencies

| Package | Licence |
| --- | --- |
| [youtubei.js](https://github.com/LuanRT/YouTube.js) 18.1.0 | MIT |
| [music-metadata](https://github.com/Borewit/music-metadata) 11.x | MIT |
| [Electron](https://github.com/electron/electron) 44.x (includes Chromium; its notices ship as `LICENSES.chromium.html` in the build) | MIT |

## C# / .NET Windows preview dependencies (`uno/heads/Soncle.Windows`)

The `windows-preview` build is a test build of the WinUI 3 head; it is not the shipping app yet.
The head is ours; these are the platform and theme packages it is built on.

| Package | Licence |
| --- | --- |
| [Uno Platform](https://github.com/unoplatform/uno) (`Uno.WinUI`, `Uno.Resizetizer`) | Apache-2.0 |
| [Uno Themes](https://github.com/unoplatform/uno.themes) (Material 3 styles) | Apache-2.0 |
| [Uno Toolkit](https://github.com/unoplatform/uno.toolkit.ui) (`Uno.Toolkit.WinUI`, `.Material`) | Apache-2.0 |
| [Microsoft Windows App SDK](https://github.com/microsoft/WindowsAppSDK) (WinUI 3 / Mica; its `license.txt` ships in the package) | Microsoft Windows App SDK license — the platform chosen in `docs/WINUI_PLAN.md` §1 |

## C# / .NET test dependencies (`uno/`)

The C# port under `uno/` is not shipped yet; these are used only to build and test it.

| Package | Licence |
| --- | --- |
| [xUnit.net](https://xunit.net/) (xunit, xunit.runner.visualstudio) | Apache-2.0 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | MIT |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | MIT |

## Algorithms and data

- Loudness measurement follows ITU-R BS.1770 / EBU R128 (K-weighting, 400 ms gated blocks). The
  K-weighting filter coefficients use the published formulas also used by libebur128.
- Key profiles in `renderer/flow/analyze.js` are the Krumhansl–Kessler major/minor key profiles
  (Krumhansl, *Cognitive Foundations of Musical Pitch*, 1990). These are published experimental
  values.
- The Camelot wheel notation is used only as a naming scheme for keys.

No lyrics, audio or artwork ship with the app. The app fetches them at runtime from YouTube Music,
LRCLIB, or the user's own files.
