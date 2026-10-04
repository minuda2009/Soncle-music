# M00 — `uno/` solution, projects and CI

_Branch: `openhands/uno-m00-scaffold`. Needs: nothing. Read `README.md` in this folder first._

## Goal
An empty but complete C# solution in `uno/` that builds and tests on Linux in CI, so every later
task only adds code.

## Build
1. **`uno/global.json`.** Pin the current .NET LTS SDK (`net10.0`), with `rollForward:
   latestFeature`.
2. **`uno/Directory.Build.props`**, applied to all projects:
   - `Nullable` enable;
   - `TreatWarningsAsErrors` true;
   - `LangVersion` latest;
   - `InvariantGlobalization` true for libraries;
   - `Deterministic`.
3. **`uno/Soncle.sln`** with these class libraries under `uno/src/` (all `net10.0`, no Uno or
   Windows references yet): `Soncle.Core`, `Soncle.YouTube`, `Soncle.Streams`, `Soncle.Flow`,
   `Soncle.Audio`, `Soncle.Services`.
   - Each has a placeholder `AssemblyInfo.cs` with the file header.
   - Project references follow plan §1. Core is referenced by all. Audio references nothing but
     Core. YouTube and Streams reference Core.
4. **Test projects** under `uno/tests/`: one xUnit project per library (`Soncle.Core.Tests`, …),
   plus a shared `Soncle.TestKit` project that later tasks fill. It contains:
   - `Fixtures.Path(string relative)`: finds the repo's `fixtures/` folder by walking up from the
     test's working directory;
   - `JsonAssert.Equal(expected, actual, numberTolerance)`: compares two JSON documents
     structurally, ignores property order, and reports the JSON path of the first difference;
   - `AudioAssert` helpers: `PeakDb`, `TruePeakDb` (4× oversampled), `RmsDb`, all `float[]`-based.

   Include unit tests for the TestKit itself (e.g. `JsonAssert` reports `$.items[3].title`).
5. **Header check.** A test in `Soncle.Core.Tests` fails if any `.cs` file under `uno/` lacks the
   Soncle header line (see `AGENTS.md`).
6. **CI: `.github/workflows/uno.yml`.**
   - Triggers on push and PR with `paths: ['uno/**', 'fixtures/**', '.github/workflows/uno.yml']`.
   - Runs on `ubuntu-latest`: `actions/setup-dotnet` (version from `global.json`), then
     `dotnet build uno/Soncle.sln -warnaserror` and `dotnet test uno/Soncle.sln`.
   - Use a `concurrency` group like the other workflows.
   - Don't touch `android.yml`, `android-preview.yml` or `ci.yml`.
7. **`uno/README.md`:** what the folder is (one paragraph pointing to `docs/WINUI_PLAN.md`), how
   to build and test, and the project list.

## Acceptance
- `cd uno && dotnet build -warnaserror && dotnet test` passes locally and in CI.
- The header-check test fails when a header is removed (show this in the PR by describing the
  check; don't commit a failing file).
- `npm test` and `npm run lint` at the root still pass, and no files outside `uno/`,
  `.github/workflows/uno.yml` and `THIRD_PARTY_NOTICES.md` changed.
- `THIRD_PARTY_NOTICES.md` lists xUnit (Apache-2.0) and any other test packages.

## Out of scope
Uno Platform packages, heads (Windows/Android), any ported code.
