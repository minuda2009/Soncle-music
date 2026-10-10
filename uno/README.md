# Soncle for C# / .NET (Uno Platform)

This folder is the new app: one C# / .NET codebase that will replace the Electron desktop app and
the Capacitor Android app with a shared Uno Platform UI, a WinUI 3 head on Windows and a .NET for
Android head. The design and the reasoning are in `docs/WINUI_PLAN.md`; the work is broken into
agent-sized tasks in `docs/tasks/uno/`.

Until the port passes the plan's parity checklist (§4), the JavaScript apps in `src/`, `renderer/`
and `mobile/` keep shipping and nothing here changes their behaviour.

## Build and test

```bash
cd uno
dotnet build -warnaserror   # the whole solution must build with no warnings
dotnet test                 # xUnit tests, including the golden-diff tests against fixtures/
```

Everything here targets `net10.0` (pinned in `global.json`) and must build and test on Linux. The
libraries use no Uno or Windows APIs; platform code comes later in the heads.

## Projects

Libraries (`src/`):

| Project | What it holds |
| --- | --- |
| `Soncle.Core` | models, settings and library store, the `window.api` contracts |
| `Soncle.YouTube` | YouTube Music: InnerTube client, parsing, stream resolution, BotGuard |
| `Soncle.Streams` | whole-song downloader and cache for both heads |
| `Soncle.Flow` | Flow radio: WebM cues, tempo/key/energy analysis, the planner |
| `Soncle.Audio` | decode → two decks → master chain → limiter (no output device) |
| `Soncle.Services` | AutoEq, import, devices, lyrics, carry-over, backups |

Tests (`tests/`): one xUnit project per library, plus `Soncle.TestKit` with the shared helpers
(`Fixtures.Path`, `JsonAssert.Equal`, the `AudioAssert` level meters) that later tasks use.

Windows head (`heads/`, not in `Soncle.sln` — it is Windows only):

| Project | What it holds |
| --- | --- |
| `Soncle.Windows` | the WinUI 3 head (Soncle Preview): the shell window, Mica, the custom title bar and the media session; built on `windows-latest` (`.github/workflows/windows-preview.yml`), never on Linux |
