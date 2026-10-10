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
`windows-preview` release with the zip. The build is unsigned, so SmartScreen warns; run from a
folder, nothing is installed. Its app id is `com.minuda2009.soncle.preview` and it never reads or
writes the shipping app's `%APPDATA%\Soncle`.

Building for Windows needs Windows: Uno's SDK stops with `UNOB0014` on other systems, and
WinAppSDK's XAML compiler is a Windows binary.
