---
name: repo-workflow
description: How to start, carry out and finish any piece of work in the daze-media-osd repo — orienting from docs, branching, committing, opening PRs, and the definition of done. Use this at the start of every coding, refactoring, bug-fix or docs task in this repo, whenever the user asks "what's next", "where are we", "continue", or to commit/push/open a PR, and before ending a task, even if the request looks small.
---

# Repo workflow

This repo is written entirely by AI agents across many sessions. No agent remembers the previous one;
the repo is the only memory. So every task starts by reading that memory and ends by updating it.
Skipping either step is how the next agent ends up re-deciding settled questions or retrying dead ends.

## 1. Orient (before touching code)

1. `docs/GOALS.md` — user intent, scope, **Status** checklist and **Next step**.
2. `docs/ARCHITECTURE.md` — the sections relevant to the task (components, invariants, open questions).
3. Grep `docs/DECISIONS.md` and `docs/DEADENDS.md` for the area you're touching (component name, API
   name, library name). Five seconds of grep beats an hour retrying something that already failed.
4. `git status` / `git log --oneline -15` — see what's in flight.

If the user's request conflicts with a recorded decision, say so and ask — the user may have changed
their mind (then record a superseding decision) or may not remember the trade-off.

## 2. Work

- Branch from `main`: `feat/<topic>`, `fix/<topic>`, `docs/<topic>`, `chore/<topic>`, `spike/<topic>`.
  Spikes are throwaway experiments; their *result* goes into the docs even if the code is discarded.
- Build and test with the `build` skill. Keep the tree building at every commit.
- Respect the invariants in ARCHITECTURE (fail-open, never steal focus, no admin).
- Write a code comment only where the code can't explain itself — mostly interop quirks and Windows
  behaviour. Point to the decision instead of repeating it: `// Hide per-show, never permanently (D-002)`.

## 3. Record knowledge (as you go, not only at the end)

Use the `knowledge-log` skill whenever you: choose between real alternatives, reject an approach, hit a
dead end, learn a non-obvious Windows behaviour, change the architecture, or the user states a
preference or changes scope. Do it in the same commit as the code it explains.

## 4. Commit

Conventional Commits, imperative subject ≤ 72 chars. The body says **why**, not what (the diff shows what).
Reference decisions: `Refs: D-002`.

```
feat(core): track default capture device

The OSD must show the active mic (user intent in GOALS). IMMNotificationClient
fires for every role; we filter to eConsole to match what Settings shows.

Refs: D-001
```

Never commit `bin/`, `obj/`, publish output, local settings, or anything with secrets.

## 5. Definition of done

- [ ] `build` skill: build + tests green; for UI changes, the app was actually run and the change seen.
- [ ] Knowledge recorded (decisions / dead ends / architecture / open questions closed).
- [ ] `docs/GOALS.md` Status ticked and **Next step** rewritten for the next agent.
- [ ] If any instruction in `CLAUDE.md` or a skill turned out wrong or stale, fix it in this same change.
      The workflow is part of the repo and evolves with it.
- [ ] Pushed and a PR opened with `gh pr create` (when a remote exists); the user merges. Commit to
      `main` directly only if the user says so.

PR description: what changed, why (link D-xxx), how it was verified, what's left.
