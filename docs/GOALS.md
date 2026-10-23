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
- OSD also appears when the default output/input device changes, and on media keys
  (play/pause/next/previous) (user, 2026-10-22).
- OSD animates in and out like the native flyout (user, 2026-10-22).
- OSD pops up when a Bluetooth audio/media device connects or disconnects; on/off in settings
  (user, 2026-10-22).
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
- [x] Tray icon with Exit (own `Shell_NotifyIcon`, D-009); clean exit restores the native flyout
  (2026-10-22)
- [x] Settings window (tray left-click / menu) + `settings.json`: position, hide-after, mic row; live
  preview (D-010) (2026-10-22)
- [x] Media session info in OSD (GSMTC: cover, title, artist, play state; "Media" toggle); media keys
  pop it, session changes refresh it (D-011) (2026-10-22)
- [x] OSD show/hide animation: fade + slide, reversible, off with Windows "Animation effects" (D-012)
  (2026-10-22)
- [ ] OSD on Bluetooth audio device connect/disconnect (configurable)
- [ ] Autostart, publish to a local folder, daily-driver test

**Next step:** pop the OSD when a Bluetooth audio device connects or disconnects, with a settings
toggle (`AppSettings`, no version bump, X-003). Today only a *default* device switch pops it (D-011),
so a headset connecting as non-default, or disconnecting, shows nothing. Start from
`IMMNotificationClient.OnDeviceStateChanged` (ACTIVE ↔ UNPLUGGED/NOTPRESENT) in `CoreAudioService`,
render and capture alike; tell Bluetooth from other endpoints by the device's enumerator
(`BTHENUM` / `BTHLEENUM`, e.g. `PKEY_Device_EnumeratorName`) — verify on this machine first. One BT
headset is several endpoints (A2DP + hands-free) → expect a burst; show once, naming the device and
"connected"/"disconnected". Ask the user the default (on/off) and whether wired USB counts. Record as a
decision superseding D-011.

**Then:** autostart (HKCU Run → launcher, D-003), publish to a local folder, daily-driver test.

When convenient: OSD + settings window in light theme, OSD on a second monitor, tray icon on a light
taskbar and after an explorer restart, an app/window icon (the exe and settings window have none yet),
media keys from a physical Consumer Control device (only the keyboard path is verified), media info
with Spotify/Media Player (only Chromium verified).
