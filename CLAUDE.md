# daze-media-osd

Windows 11-only tray app that replaces the native volume/media OSD with one that shows more — first of
all the current output and input device. C# / .NET 10 + WinUI 3 (D-001). Written entirely by AI agents.

## The repo is the memory

No agent remembers the last session. Read before working, write before finishing:

- [docs/GOALS.md](docs/GOALS.md) — user intent, scope, status, **next step**. Read first, always.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — how it's built now, invariants, open questions.
- [docs/DECISIONS.md](docs/DECISIONS.md) — why / why not (D-xxx). Grep before re-deciding anything.
- [docs/DEADENDS.md](docs/DEADENDS.md) — what was tried and failed (X-xxx). Grep before trying anything clever.

Keep these lean: every line must help the next agent decide better. Rules in the `knowledge-log` skill.

## Skills

- `repo-workflow` — orient → branch → work → record → commit → definition of done. Use for every task.
- `build` — prerequisites, build/run/test/publish commands, troubleshooting.
- `knowledge-log` — what goes into which doc, entry formats, brevity rules.

If a skill or this file is wrong, fix it in the same change that found the problem.

## Hard rules

- Fail-open: the native flyout is suppressed only while our app runs; every exit path, crashes included,
  must restore it so Windows behaves as stock (D-006).
- The OSD window never takes focus or activation.
- No code injection into `explorer.exe` or other processes (D-002). No admin rights.
- Don't contradict a recorded decision silently — follow it, or ask the user and record a superseding one.
