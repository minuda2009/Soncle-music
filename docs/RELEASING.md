# Releasing

The README's Download link points at the **latest release**. Three other releases
(`android-latest`, `android-preview`, `windows-preview`) are rolling test builds that CI replaces on
every push; they are marked pre-release and are never "latest".

To cut a stable release of the Electron (Windows) and Android apps:

1. Set the version in `package.json` (desktop) and, if the phone app changed, in `mobile/` (`package.json` and `android/app/build.gradle`).
2. Move the "Unreleased" entries in `CHANGELOG.md` under a heading for that version.
3. Merge to `main`, then run **Actions → Release → Run workflow** (tag blank = `v<package.json version>`), or push a tag such as `v1.0.1`.

The workflow runs the tests, builds `Soncle-Setup-<v>.exe`, `Soncle-Portable-<v>.exe` and the APK,
adds `SHA256SUMS.txt`, and publishes the release (replacing one with the same tag). The builds are
unsigned; the release notes say so.
