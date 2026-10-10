# Soncle — design decisions (UI, UX, structure)

_Agreed with minuda2009 on 10 Oct 2026 after a full review of `main` @ b892a3c (Electron 1.0, Android
0.1.x, Uno port up to M12). **Every agent working on Soncle reads this file** before UI, UX or
`uno/` app work. Where `docs/WINUI_PLAN.md` §3 and this file differ, this file wins; say so in your PR._

**Scope.** These decisions are built into the **Uno (C#) app**, not retro-fitted to Electron
(see §7.1). Some sections still describe Electron (current state, line numbers): they are the
starting point for the port.

**How to read it.** `Must` = agreed decision. `Should` = agreed direction, details are yours to
propose (with screenshots). Rules marked **(test)** are behaviour rules that get a unit test in
`Soncle.App`, like the Android playback rules in plan §9.

---

## 0. Why we migrate

**One codebase for Windows and Android.** Note that today's JS apps already share code
(`mobile/` reuses `renderer/` and `src/` unchanged). The C# app must keep that property while
going native: anything shared today stays shared, and platform-specific code lives only behind the
seams in §5.6. A change that makes Windows and Android behave differently without a platform reason
is a bug.

Order: **Windows head first**, with the Android viability gate (§6.2) run *before* any page work.

## 1. Design ideology (unchanged, restated)

- **Material 3 is the look** (tonal surfaces, M3 controls, Material Symbols Outlined weight 600,
  spring motion). **Fluent / Windows is the platform underneath** (Mica, title bar, SMTC, tray,
  taskbar, snap, accelerators, Narrator). We don't adopt Fluent controls visually.
- **Soft colour from the artwork** stays the signature. Colour changes are slow and gentle
  (`--d-slow`/`--ease`), never abrupt. Schemes come from Material Color Utilities (plan §2.3), not raw
  pixel colours, so contrast is guaranteed on any cover.
- **How Soncle differs from the other Metrolist-style desktop ports:** not by a new visual style, but
  by putting **what Soncle does with sound** (transitions, Flow, device profiles, loudness, local
  files) where people can see and steer it — calmly (§2).

## 2. Detail levels — keep it calm (Must)

minuda2009's concern: some ideas can feel like "feature show-off". So every piece of technical
detail belongs to one of three levels:

| Level | Who sees it | Examples |
| --- | --- | --- |
| **1 · Always** | everyone | play controls, queue, lyrics, device name, Resume |
| **2 · In context** | everyone, only when relevant, subtle, then gone | transition chip near the end of a song, Flow tags, AutoEq suggestion on first connect, device-profile chip |
| **3 · Audio details** | only with Settings → *Show audio details* on (default **off**) | BPM/key chips, signal path, LUFS numbers, limiter gain reduction, codec/bitrate line |

Nothing is removed by this; only *how loudly it's shown*. A new feature must state its level in
its PR.

## 3. Information architecture (Must)

### 3.1 Destinations
Four main destinations, plus pinned items:

| Destination | Contains |
| --- | --- |
| **Home** | merged Home + Explore (§3.2) |
| **Library** | tabs: Playlists, Albums, Artists, Songs (Liked), Downloads, **History**, **Stats**. Local files appear as an **"On this computer"** filter/source in Library (the command bar already treats them as one pool), with folder management kept in it |
| **Sound** | everything you hear: EQ, AutoEq, loudness, transitions, output devices, **per-device profiles** (editable list), A/B hold, meters (§4.6) |
| **Settings** | app settings only (account, appearance, content, storage, backup, privacy, about) |

- Below the destinations: **pinned playlists/albums with artwork** (today the sidebar is mostly
  empty under "Liked songs").
- **Adaptive:** the same four destinations are the left rail on desktop (collapsing to icons at
  narrow widths) and the bottom bar on a phone. Same for the player: player bar on desktop, mini
  player on phone, one shared Now Playing.
- Search stays in the title bar (desktop) / top of Home (phone); Ctrl+K stays global.

### 3.2 Home = Home + Explore (named "Home")
Reasons: mood chips on Home and the Moods & genres grid on Explore duplicate each other; signed out,
Home is already generic; a shorter rail; every other port copies YouTube Music's split.

Order of the page:
1. **Continue card** — last song/queue, tinted from its artwork: *Resume*, *Start Flow from here*.
2. **Quick picks.**
3. **Shortcut row** (replaces the mood chips): New releases · Charts · Moods & genres · Podcasts —
   each opens the existing full page (the `browse` routes stay).
4. YouTube home shelves interleaved with *New albums & singles* and a *Charts* preview.
5. Moods & genres grid at the bottom.

- Signed in: personalised shelves first. Signed out: new releases and charts move up.
- Both feeds load **in parallel**; sections fill in as they arrive with skeletons; shelf count is
  capped (the shortcuts lead to the full lists).
- Ctrl+K still finds "Explore", "Charts", "New releases".

## 4. UI decisions per surface

### 4.1 Player bar (Must)
- **Empty state:** never a blank square and `0:00 / 0:00` — show *Resume: <last song>* tinted from
  its artwork.
- **Grouped right-hand controls:** always visible = lyrics, queue, volume, expand. In a `⋯`
  overflow = sleep, focus, mini player. Device becomes a **device chip** ("HD 600", level 1; "HD 600 ·
  AutoEq" level 2 when a profile was applied automatically).
- **Every tooltip shows its shortcut** (from the command registry, §5.4).
- **Transition moment (level 2):** in the last ~15 s, "Mixing into <next> · Smart mix" with both
  artworks cross-blending (§4.5).

### 4.2 Shelves and grids (Should)
- Carousels: edge fade instead of a hard clip; arrows appear on hover (always available to keyboard);
  Shift+wheel and trackpad scroll horizontally.
- Moods & genres: responsive columns (fit as many as the width allows) — today's fixed 5 columns
  truncate labels ("Christian & gospe…"); hover fills the tile with a tint of its mood colour.

### 4.3 Settings page (Must)
- Sticky list of groups on the left at desktop widths; settings are searchable from the page and
  from Ctrl+K (typing "crossfade" jumps to the row).
- **Unique icon per row** (today Theme / Dynamic colours / Accent colour share one icon; Motion style
  / Squiggly seekbar share another).
- **Fix naming:** Motion style options are "Classic / Android" but the description says
  "Expressive" — use one name everywhere.
- Audio rows move to Sound (§3.1); Settings links there.
- Each group gets *Reset to defaults*; values set by a layer show it ("set by HD 600 profile",
  "Focus mode", §5.1).

### 4.4 Colour and motion (Should)
- Soft wash of the current artwork's scheme at the top of Home, album and artist pages, cross-fading
  on song change.
- **Connected animations**: card artwork → collection header; player-bar thumbnail → Now Playing art
  (M3 container transform). Springs for small controls, slow ease for colour.
- Respect reduced motion (Windows *Animation effects* / Android *Remove animations*), high contrast
  and text scaling.

### 4.5 Windows integration (Should, Uno Windows head)
Mica / Mica Alt only on title bar and rail (pages keep tonal surfaces); taskbar thumb buttons + SMTC;
jump list (Flow, recent playlists); `CompactOverlay` mini player; a "Windows accent" swatch next to
the accent colours; remember window size/position; single instance and YouTube Music links open in
Soncle; optional close-to-tray / start with Windows. (Plan §7 has the APIs.)

### 4.6 Empty states (Should)
Show a faded preview of what will appear plus one action (e.g. Stats: example charts + *Play
something*), not just an icon and a sentence.

## 5. Structure — `Soncle.App` architecture (Must)

These are the foundation: most UX rules in §6 become small once they exist, and they are far cheaper
to design in now than to retro-fit. They live in **portable `Soncle.App`/`Soncle.Core`** (no Windows
or Android APIs), so both heads share them.

### 5.1 Layered settings
Effective settings = **base** (user) ⊕ **device profile** ⊕ **session overlay** (focus mode) ⊕
**transient** (e.g. a one-off change), resolved per key, top wins.
- Replaces today's focus-mode snapshot/restore (`FOCUS_KEYS` copied into `settings.focusBackup`,
  plus crash recovery) and the separate device-profile copy.
- Ending focus = removing the overlay; nothing to restore, nothing to recover after a crash. **(test)**
- The UI can show which layer set a value (§4.3). **(test)** for the resolver.
- Settings have a typed schema with defaults and versioned migrations (the JSON shape on disk stays
  compatible with `library.json`, plan §1).

### 5.2 Queue with origins
Each queue item carries an **origin**: `user` (Play next / Add to queue), `source` (the album or
playlist you started), `flow`, `autoplay`.
- Replaces `P.queue` + `P.flow.queue` + `P.original` (shuffle) + `P.flow.locked`.
- Gives for free: "Up next" vs "Then from Flow" sections, Flow tags, "Flow stopped re-arranging",
  undo for Clear queue, and shuffle as an ordering view over the same items. **(test)**

### 5.3 Playback state machine
`PlaybackController` (M11) is the single owner of playback state: `idle → loading → playing ⇄
paused → crossfading → error`, with ducking, sleep timer and focus as **layers**, not extra flags
(today: `P.loading`, `P.loadToken`, `P.errTok`, `P.ducked`, `P.duckPaused`, `P.sleep`, global
`FOCUS`). Every transition is unit-tested, including skip during crossfade and double loads.

### 5.4 Command registry
One list of commands: id, title, icon, shortcut, `canExecute`, `execute`, level (§2). It feeds
**Ctrl+K, keyboard accelerators, context menus, tooltips, the shortcut list (`?`), SMTC/taskbar
buttons and Android media actions**. No command is wired by hand in a second place.

### 5.5 Notification service
Replaces direct toasts (88 `toast()` calls in `app.js` today, one slot, newer replaces older).
Levels: **silent** (the control's state already shows it — shuffle, repeat, sleep set),
**info**, **action** (Undo / Turn on / Choose), **important**. Messages queue by priority; duplicates
within a few seconds merge; an action message is never replaced by an info one. **(test)**

### 5.6 Platform seams
Everything platform-specific is an interface in `Soncle.Core` with one implementation per head:
audio output (WASAPI / AAudio or AudioTrack), media session (SMTC / MediaSession), stream unlocking
& PO tokens (WebView2 / Android WebView), output-device detection, file/folder pickers, storage
paths, power, ducking source, window shell. **No `#if WINDOWS` / `#if ANDROID` outside the heads.**

### 5.7 Data
History, plays, skips and stats move to **SQLite** (`Microsoft.Data.Sqlite`) in the C# app. Today
skips are capped at 3,000 by deleting the oldest and stats scan everything. Settings, playlists and
library keep the JSON shape (carry-over unchanged).

### 5.8 Behaviour rules are shared tests
The Android playback rules (plan §4, §9) plus every **(test)** rule in this file run in
`dotnet test` once and protect both heads.

## 6. UX rules

### 6.0 Already in Soncle — keep them (port as-is)
Session restore on launch (queue, position); Undo when removing from the queue or a playlist; skips
in the first 30 s feed Flow's taste; auto-skip on playback error; click a lyric line to seek, lyrics
offset; Ctrl+K runs actions, settings and finds your music; drag-reorder queue with
Now playing / Next from / Played sections and the transition chip; AutoEq suggestion and device toasts
on connect; per-device profiles; Smart duck (lowers volume; optional pause mode); Sound page A/B hold
and meters; focus mode; offline wait-and-resume.

### 6.1 Transitions (level 2 unless noted)
- T1. The transition is shown in the player bar near the end of a song (§4.1), not only in the queue.
- T2. The transition chip opens an **inline menu** for that pair (Smart mix / Crossfade / Gapless /
  Cut). It must not close Now Playing or navigate away (today it jumps to the Sound page). **(test)**
  for the per-pair override in the planner.

### 6.2 Flow radio
- F1. Flow queue header: "Flow from <seed> · arranged by tempo & key"; level 3 adds a tiny BPM/energy
  sparkline of what's coming.
- F2. Row tags from data Flow already uses: *Liked*, *Familiar artist*, *Discovery*, *Key match*
  (level 2; one tag max per row).
- F3. A manual drag in a Flow queue locks the plan — say so: "Flow stopped re-arranging · Resume".
  **(test)**
- F4. Skip learning is visible once: after a second early skip of the same song, "Flow will play
  this less · Undo". Song menu gets *More like this* / *Less like this*. **(test)**
- F5. Starting Flow shows the queue arranging (existing FLIP animation), not a toast that vanishes.

### 6.3 Sound and devices
- D1. One home for everything you hear: the Sound page (§3.1). Device profiles become an editable
  list there (today: one comma-joined line in Settings with only *Clear*).
- D2. When a profile is applied automatically, the device chip shows it (§4.1), so a change in sound
  never looks random; the connect message offers *Change*.
- D3. **Signal path** (level 3), opened from the quality line in Now Playing: Source (codec/bitrate)
  → Loudness (target, gain) → EQ → Headphone correction → Limiter (gain reduction) → Output
  (device, rate). Needs `Soncle.Audio` to report each stage — Uno only.

### 6.4 Queue, notifications, keyboard
- Q1. *Clear queue* offers Undo like removing a single song (today it doesn't). **(test)**
- Q2. Notification levels per §5.5: no toast for state the UI already shows.
- K1. Multi-select (Shift/Ctrl) in every song list with bulk Play next / Add to queue / Add to
  playlist / Like; drag songs onto the queue or onto pinned playlists.
- K2. The same song context menu everywhere (from the registry): Play next, Add to queue, Go to
  album, Go to artist, Start Flow from here, Share link.
- K3. Small ones: wheel over volume; hover seek bar shows the time; arrows seek ±5 s; Enter plays the
  focused row; `?` lists shortcuts.

### 6.5 Focus mode, ducking, first run, stats
- M1. Ctrl+Shift+F starts the last focus length directly (no dialog); the dialog stays on the button.
- M2. Say up front that focus continues with Flow if the queue is short; at the end show a summary
  ("50 min · 14 songs · your settings are back").
- M3. Smart duck **pause mode** only: the "Paused while another app plays sound" message offers
  *Resume anyway*.
- M4. First run becomes setup, not a feature tour: detect headphones → offer AutoEq (logic exists);
  choose loudness Quiet / Normal / Loud with a preview; import (Spotify/CSV); optional sign-in. Keep
  the honest note (artists aren't paid through the app) unchanged. Goal: something good playing within
  30 s.
- M5. Stats gains "Your Flow" (level 2/3): average BPM, favourite keys, best transitions, focus
  hours — cheap once §5.7 exists.

## 7. Process rules

- **7.1 Electron/Capacitor UI freeze.** Once the Uno shell work starts (plan stage 4), the JS apps
  get **bug fixes and committed roadmap items only** — no new UI/UX. Otherwise the port chases a
  moving target. Until then, small JS-side fixes from §6 (e.g. Q1) are fine if minuda2009 asks.
- **7.2 Android viability gate — now, before page work.** Build an empty Uno Android app (Material
  theme, one page, a list, the player bar) and measure against plan §9: arm64 APK ≤ 30 MB, cold start
  ≤ 2 s on a 4 GB-class phone, memory while playing no worse than the Capacitor app. Record the numbers
  in plan §9. **If it fails:** keep the portable libraries (`Soncle.Core/YouTube/Streams/Flow/Audio/
  Services/App` view models) and give Android a .NET for Android head with a native UI (or keep
  Capacitor) — the shared logic is most of the win, so no C# work is wasted.
- **7.3 Drift check.** Any change to a JS file that has a C# port (plan §12.5 ledger) needs the C#
  update in the same PR, or a ledger note. The CI `drift` job (`tools/drift-check.mjs` +
  `tools/port-map.json`) fails when a ported file changes without its C# counterpart or a ledger
  edit; add each new port to the map. (First drift found: `renderer/devices.js` gained `androidOutputLabel`
  after M09c.)
- **7.4 UI changes are shown first.** Screenshots (and side-by-side with Electron where a page
  exists) before merging, as in `AGENTS.md`. Visual changes beyond this file need minuda2009's OK.
