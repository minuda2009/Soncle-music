# Tasks handed to agents

One file per task. A task file is the brief for a single pull request: goal, what to build, the
acceptance list, and what is out of scope. **Read `AGENTS.md` first**, then the task file you were
given.

## What is here

| File | What it is |
| --- | --- |
| `01-phone-output-profiles.md` | Android: know the audio output and switch sound profiles per output (done — kept for reference) |
| `uno/` | the C# migration tasks (M00–M12). Start with `uno/README.md`: it has the order, the dependencies and the ground rules. |

## How tasks are written (so you can follow the house style)

- **Goal** — one or two sentences on the user-visible outcome.
- **How it is done today** — the JS files and functions to reuse or port, named exactly, so you
  never have to guess where the behaviour lives.
- **What to build** — a numbered list, each item small enough to check off.
- **Acceptance** — the concrete, checkable conditions. For C# ports these are golden-diff tests
  against `fixtures/…`; for app behaviour they are what to test on a device.
- **Out of scope** — what this task deliberately leaves for the next one, so a PR stays small.

## House rules every task inherits

- One task per branch (`openhands/<short-topic>`), one pull request, small commits.
- A port is done when its output equals the JS on the same fixtures, not when it compiles.
- Register a new port in `tools/port-map.json` and update the ledger (`docs/WINUI_PLAN.md` §12.5).
- `npm run check` must pass before you push (it runs the JS checks and, where possible, the C# ones).
- Add a `CHANGELOG.md` entry under "Unreleased", and update `THIRD_PARTY_NOTICES.md` if you add a
  package.
- Never push to `main`; open a pull request and let minuda2009 merge it.
