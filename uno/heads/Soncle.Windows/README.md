# Soncle.Windows — the WinUI 3 head (Soncle Preview)

Stage 4 of the migration (`docs/WINUI_PLAN.md` §1, §11): the real WinUI 3 / Windows App SDK
head that opens Soncle's shell on Windows. It is **Windows only** and is deliberately not in
`uno/Soncle.sln`, so the Linux job keeps building and testing the libraries unchanged.

## What this scaffold has

- a window with **Mica** (`MicaBackdrop`, solid when unsupported), the **48 px custom title bar**
  (`ExtendsContentIntoTitleBar` + the `Tall` caption row) and an honest **media session**
  (`SystemMediaTransportControls` reporting Soncle with nothing playing);
- the four destinations — **Home · Library · Sound · Settings** — in a rail;
- the **player bar** with the empty state from `docs/DESIGN.md` §4.1 (never a blank square with
  `0:00 / 0:00`).

Not here yet, by scope: tray / close to tray, rail collapse, `Soncle.App` pages, `Soncle.Design`
(stage 3) and the own-data-folder carry-over. See the Issue for the exact boundary.

## Measured (shell, `windows-latest`, 10 Oct 2026)

The workflow does not just compile the head: it extracts the packed build, launches it and fails
if the process exits or shows no window within 90 s. From that run:

| | Shell, idle | Electron 1.8.0 |
| --- | --- | --- |
| Window shown after start | 0.8–3.3 s (first-frame proxy, three runs) | not recorded |
| Working set | 102–116 MB, 117–120 MB after 5 s | ~500 MB, all processes (`CHANGELOG.md`) |
| Download | 136.8 MB zip, run from a folder | installer |

The window title the shell sets is `Soncle Preview` (`Window.Title`), the same string the custom
title bar draws. The numbers come from the runner (a shared VM, no GPU, self-contained and
untrimmed publish — trimming, ReadyToRun and Native AOT are the stage 6 performance pass), so they
are the first WinUI build's, not minuda2009's PC: CPU and the on-PC run come from the preview link.

## Notes

- The rail is a WinUI `NavigationView` in `Left` mode. `docs/WINUI_PLAN.md` §3 names the Uno
  Toolkit `TabBar` for it, but `TabBar` is a single horizontal line in Uno Toolkit (no vertical
  rail), so the shell uses the platform rail until `Soncle.Design` styles the final one.
- The head is not in `uno/Soncle.sln`: adding a `net10.0-windows` project would put it on the
  ubuntu job's path. The `windows-preview` workflow builds the csproj directly.

## Build and run (Windows)

```powershell
dotnet publish uno/heads/Soncle.Windows/Soncle.Windows.csproj `
  -c Release -f net10.0-windows10.0.26100 -r win-x64 -p:Platform=x64 --self-contained true `
  -p:WindowsPackageType=None -p:PublishTrimmed=false -p:PublishReadyToRun=false -o publish
./publish/Soncle.Windows.exe
```

`.github/workflows/windows-preview.yml` does exactly this on `windows-latest` and replaces the
`windows-preview` release with the zip. That job first launches the packed build (a window must
appear) and only then touches the release, so a head that no longer starts never replaces the last
good preview. The build is unsigned, so SmartScreen warns; run from a folder, nothing is installed.
Its app id is `com.minuda2009.soncle.preview` and it never reads or writes the shipping app's
`%APPDATA%\Soncle` (the window exists; nothing persists yet — the carry-over arrives with the
switch).

Building for Windows needs Windows: Uno's SDK stops with `UNOB0014` on other systems, and
WinAppSDK's XAML compiler is a Windows binary.
