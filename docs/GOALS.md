# Goals

What the user wants and where the project stands. Keep it current: an agent should be able to read only
this file and know what to build next. History of *why* lives in [DECISIONS.md](DECISIONS.md).

## User intent

- Extend the Windows 11 media OSD (the flyout shown on volume/media keys) with extra information. The
  first concrete ask: **show the current output and input device when changing volume.**
- Windows 11 only. No cross-platform concerns, ever.
- A configuration UI with a modern design (should feel like a native Windows 11 app).
- The repo is driven purely by AI agents. Decisions, reasons, rejected options and dead ends are written
  down so the next agent understands *why* the code is the way it is — but kept lean, no bloat.
- v1 builds on the local machine. GitHub CI comes in v2.

## v1 scope

- Tray-resident app that replaces the native volume OSD with its own Win11-styled OSD (see D-002).
- OSD shows: volume level + mute state, **default output device name**, **default input device name**
  (+ mic mute state). Media info (title/artist/controls) when a media session is active.
- OSD also appears when the default output/input device changes.
- Settings window: enable/disable sections, position, timeout, theme follows system, start with Windows.
- Fail-open: if the app is closed or crashes, the native OSD keeps working.

## Later (not v1)

- v2: GitHub Actions CI (build + test + publish artifact), release packaging.
- Brightness / airplane-mode / keyboard-layout flyouts.
- Per-app volume in the OSD, quick device switching from the OSD.

## Non-goals

- Injecting code into `explorer.exe` / modifying the native flyout itself (D-002).
- Drawing over exclusive-fullscreen games.
- Windows 10 support.

## Status

- [x] Repo scaffolded: docs, skills, stack decision (2026-10-22)
- [ ] Spike: detect + suppress native volume flyout on current Win11 build (open question Q1 in ARCHITECTURE)
- [ ] Solution skeleton per ARCHITECTURE layout; update `build` skill with real commands
- [ ] Core: audio endpoint volume + default device tracking
- [ ] OSD window (Win11 look) with device names
- [ ] Tray icon + settings window + persisted settings
- [ ] Media session info in OSD
- [ ] Autostart, publish to a local folder, daily-driver test

**Next step:** install the .NET 10 SDK (`build` skill → prerequisites), then the Q1 spike.
