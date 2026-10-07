# M06 — YouTube Music: parsing and normalisation

_Branch: `openhands/uno-m06-yt-parse`. Needs: M02 (and M01's fixtures). Read `README.md` in this
folder first._

## Goal
Turn raw InnerTube JSON into exactly the same plain JSON that `src/yt.mjs` produces, in C#, with
no network. This is the read side of the YouTube port; requests are M07.

## Important difference from the JS
`yt.mjs` doesn't read raw JSON itself. youtubei.js parses it into a node tree (`MusicResponsiveListItem`,
`MusicTwoRowItem`, `MusicCarouselShelf`, …) and `yt.mjs` normalises those nodes. In C# there's no
youtubei.js, so `Soncle.YouTube` must:
1. walk the raw renderers itself (`musicResponsiveListItemRenderer`, `musicTwoRowItemRenderer`,
   `musicCarouselShelfRenderer`, `musicShelfRenderer`, headers, continuations, …);
2. apply `yt.mjs`'s normalisation rules on top.

The golden diff covers both steps at once: raw in, `fixtures/yt/expected` out. Metrolist's Kotlin
`innertube` module (`innertube/src/main/kotlin/.../pages/`, credited in THIRD_PARTY_NOTICES.md)
shows how to read the raw renderers directly. Use it as a reference for structure, not as a
source of behaviour: `yt.mjs` decides what the output looks like.

## Inputs
- `src/yt.mjs`: everything under `__parse` (`normItem`, `normShelf`, `pickThumb`, `fixThumb`,
  `parseDuration`, `artistsFromRuns`, `kindFromPage`, `pageType`) and the page builders `home`,
  `homeMore`, `explore`, `browse`, `search`, `searchMore`, `album`, `artist`, `playlist`,
  `upNext`, `radio`, `related`, `ytLyrics` and `songInfo`.
- `test/yt.parse.test.mjs`, `test/home.test.mjs`, and `fixtures/yt/raw` + `expected`.

## Build (in `Soncle.YouTube/Parsing/`)
- **A small typed accessor over `JsonElement`.** Missing paths return null and never throw;
  YouTube renames things constantly, and the JS silently skips unknown nodes
  (`Parser.setParserErrorHandler(() => {})`), so the C# must too.
- **`Normalize.*`:** one method per `yt.mjs` helper, with the same names.
- **`Pages.*`:** one method per page builder, taking raw responses and returning the M02 models.
  Continuation tokens are carried in the same place as the JS does.

## Acceptance
- **Golden diff:** for every file in `fixtures/yt/raw` that has an expected output, `Pages.X(raw)`
  serialised with M02's context is JSON-equal to `fixtures/yt/expected/X.json`. Thumbnails are
  compared as URLs, so `fixThumb` sizes must match.
- **The cases from `yt.parse.test.mjs`** are ported as C# unit tests with the same inputs.
- **Robustness:** a test feeds each raw fixture with random renderers removed or renamed. Nothing
  throws, and the remaining items still parse.
- **Ledger:** `yt.mjs` marked partially ported (parse side), with the JS commit.

## Out of scope
HTTP, sessions, stream URLs, sign-in, PO tokens (M07).
