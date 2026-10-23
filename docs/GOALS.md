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
- OSD pops up when any audio device (Bluetooth, USB, …) connects or disconnects; on by default, off in
  settings. It still shows the current default output/input as always (user, 2026-10-22), and also
  which device came or went, in a compact, modern way that doesn't clutter the OSD (user, 2026-10-22).
- Settings window: enable/disable sections, position, timeout, theme follows system, start with Windows.
- Only our OSD is ever visible — the native flyout must not flash (user, 2026-10-22; D-006).
- Fail-open: if the app is closed or crashes, the native OSD keeps working.
- Next features, picked from the PR #8 survey (user, 2026-10-23): **device-type icons** on the output
  and input rows, and **quick switching** of the default output/input device. Then **a hotkey per
  device**: one press makes that device the default (user, 2026-10-23).

## Later (not v1)

- v2: GitHub Actions CI (build + test + publish artifact), release packaging.
- Brightness / airplane-mode / keyboard-layout flyouts.
- Per-app volume in the OSD.

## Non-goals

- Injecting code into `explorer.exe` / modifying the native flyout itself (D-002).
- Drawing over exclusive-fullscreen games.
- Windows 10 support.
- Switching devices by clicking the OSD (user dropped it before the spike, 2026-10-23): D-008
  click-through and the never-activate rule stand; the tray menu and hotkeys cover switching.

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
- [x] OSD on audio device connect/disconnect (any device, once per burst after 500 ms, "When a device
  connects" toggle, on by default) (D-013) (2026-10-22)
- [x] Device event line in the OSD: which device connected/disconnected, one item per product,
  Bluetooth glyph, repeat bursts dropped (D-014) (2026-10-22)
- [x] Autostart toggle (HKCU Run, registry is the only state) + `scripts/install.ps1` publish to
  `%LOCALAPPDATA%\Programs\DazeMediaOsd`; installed and set to start with Windows on the dev machine
  (D-015, X-004) (2026-10-22)
- [x] Daily-driver day 1 from the log (2026-10-23): 9 h up, no crash; BT connect/disconnect with a
  default switch carries the device line; a late hands-free endpoint popped a second show → fixed
  (D-016). Media retries (0x8001010E) recover with Telegram too
- [x] Device-type icons: form factor → headphones / headset / monitor glyph, speakers keep the level
  glyph (D-017); seen in the OSD: speakers, mic, Technics EAH-AZ80 headphones (row glyph + event
  line `connected bt Headphones`, one show) (2026-10-23)
- [x] Daily-driver test: no native flyout flashes (user, 2026-10-23). Autostart at sign-in and a USB
  device deferred by the user (2026-10-23) — see "When convenient"
- [x] Plan B core + tray: `Endpoints`/`SetDefault` via `IPolicyConfig` (D-019), tray Output / Input
  submenus, hands-free outputs hidden (`DeviceChoices`); seen: input switched to Stereo Mix and back from
  the menu, OSD showed it; with the Technics on, the Output menu listed Headphones + Speakers, no
  hands-free (2026-10-23)
- [x] Plan B hotkey: Win+Alt+A cycles the default output (D-020); Settings records the chord pressed, with
  reset / turn-off in the box (D-021). Seen by the user: cycles Speakers ↔ Technics Headphones, never the
  hands-free output (2026-10-23)

**Next step:** Plan C below. When convenient: the headset glyph (Technics hands-free as default mic) and
the monitor glyph (Dell over DisplayPort).

Plan B (quick switching) is done: tray menu (D-019), cycle-output hotkey (D-020, D-021). Its third
surface, clicking a device row in the OSD, was dropped (Non-goals).

**Plan C — a hotkey per device** (M; user, 2026-10-23)
- Want: a shortcut bound to one output or input device; pressing it makes that device the default
  (eConsole + eMultimedia, D-019) and the OSD shows the switch as usual.
- Settings: a new "Shortcuts" page in the `NavigationView` (beside OSD and About). The cycle-output
  card moves there. Below it, one row per device shortcut: a device picker (active outputs and inputs
  from `Endpoints`, through `DeviceChoices` so hands-free outputs aren't offered), the recorder box
  (extract D-021's box into a reusable control), a remove button; "Add shortcut" at the end.
- Model: `deviceHotkeys: [{ id, flow, name, hotkey }]`; `name` is the last-known friendly name, so a
  row still reads right while its device is unplugged. A chord does one thing: refuse one already used
  by another row or by the cycle shortcut, with a note in Settings.
- `GlobalHotkey` takes many ids (D-020's revisit-if): an error per chord, `Pressed` says which.
  Every recorder box pauses all registrations while it records.
- A press for the device that is already default shows the OSD. A press for a device that isn't active
  doesn't switch: ask the user what the OSD should say then (e.g. "Technics not connected" on the
  event line).
- Verify with Speakers ↔ Technics and Microphone Array ↔ Stereo Mix (disable Stereo Mix again after);
  copy and restore `settings.json` around the test.

When convenient: autostart at sign-in (log `launcher: started app` right after one), a USB audio
device (one show per connect/disconnect, D-013/D-014/D-016), OSD + settings window in light theme,
OSD on a second monitor, tray icon on a light taskbar and after an explorer restart, an app/window icon (the exe and settings window have none yet),
media keys from a physical Consumer Control device (only the keyboard path is verified), media info
with Spotify/Media Player (Chromium and Telegram verified).
