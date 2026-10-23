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
- Only our OSD is ever visible — the native flyout must not flash (user, 2026-10-22; D-006).
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
- [x] .NET 10 SDK on the dev machine (10.0.402) (2026-10-22)
- [x] Spike Q1: native flyout detected + suppressed on build 26300 → minimize while running, launcher
  restores on any exit (D-006) (2026-10-22)
- [x] Solution skeleton: launcher/`--app` split, native flyout minimized while running; `build` skill
  updated (2026-10-22)
- [x] Core: default output/input tracking, volume keys, OSD show rules (D-007) — logged, no window yet
  (2026-10-22)
- [x] OSD window: output/input names, volume, mute; acrylic, no-activate, click-through, auto-hide
  (D-008) (2026-10-22)
- [ ] Tray icon + settings window + persisted settings
- [ ] Media session info in OSD
- [ ] Autostart, publish to a local folder, daily-driver test

**Next step:** tray icon (Q3 first: H.NotifyIcon.WinUI vs. own `Shell_NotifyIcon`, AOT-safe) with
Exit — a clean exit is still untested (ARCHITECTURE → Process lifecycle) — then the settings window and
`SettingsStore` (position, timeout, sections; today hard-coded in `OsdWindow`). Also eyeball the OSD in
light theme and on a second monitor when convenient (ARCHITECTURE → OSD window).
