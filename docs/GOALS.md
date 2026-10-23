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
  and input rows, and **quick switching** of the default output/input device.

## Later (not v1)

- v2: GitHub Actions CI (build + test + publish artifact), release packaging.
- Brightness / airplane-mode / keyboard-layout flyouts.
- Per-app volume in the OSD.

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
  the menu, OSD showed it (2026-10-23). Not seen yet: an output switch (only the speakers were active)

**Next step:** Plan B surface 2 (hotkey). First agree the default chord with the user. Then surface 3
(spike). When convenient: switch outputs from the tray with the Technics connected (the hands-free
"Headset (… Hands-Free)" output must not be listed), the headset glyph (Technics hands-free as default
mic) and the monitor glyph (Dell over DisplayPort).

**Plan B — quick device switching** (M, 1–2 days; tray + hotkey + OSD-click spike: user, 2026-10-23)
- Core: `IAudioService.Endpoints(flow)` (active only: id, name, kind) and `SetDefault(id, flow)`. Setting
  the default needs the undocumented `IPolicyConfig` COM interface (no documented API: `MediaDevice`
  only reads, `AudioRoutingManager` is phone-only). No admin, no injection; EarTrumpet and SoundSwitch
  ship it. Set roles eConsole + eMultimedia, as Settings does; leave eCommunications. A failure only
  logs. `scripts/audio-device.ps1 -SetDefault` already calls it (`870AF99C-…` client) and works on
  26300; record the decision when it moves into the app.
- Surface 1: tray menu gets "Output" and "Input" submenus with a check on the
  current default (native menu, D-009; the tray may take focus). The OSD shows the switch through the
  existing `OutputDeviceChanged` path.
- Surface 2: a global hotkey cycles the default output (`RegisterHotKey`, no hook, no
  admin); the OSD shows the new default. Configurable in Settings, default to agree with the user (avoid
  Win+Ctrl+V: Windows 11's own output picker). A failed registration shows in Settings. Pure
  `DeviceCycle.Next(endpoints, currentId)` (sorted by name), unit-tested. Same plumbing later gives
  the survey's "show OSD on demand" hotkey for free.
- Surface 3, spike first (user asked, 2026-10-23): click a device row in the OSD to pick another
  device. Conflicts with D-008 (click-through) and must keep the hard rule (never take focus or
  activation). Questions: can a `WS_EX_NOACTIVATE` window without `WS_EX_TRANSPARENT` take XAML clicks
  without activating (foreground window unchanged)? Hover keeps the OSD open, leave hides it; only the
  rows are hit-testable, the rest stays click-through (`WM_NCHITTEST` → `HTTRANSPARENT`?). How does
  the list open — a non-activating popup inside the OSD, not a `Flyout` that grabs focus. Result goes
  in DECISIONS (superseding D-008) or DEADENDS; drop the surface if it can't keep the rule.
- Hands-free outputs ("Headset (Technics EAH-AZ80 Hands-Free)", form factor 5): `DeviceChoices.For`
  already hides them for the menu; the cycle must use it too.

When convenient: autostart at sign-in (log `launcher: started app` right after one), a USB audio
device (one show per connect/disconnect, D-013/D-014/D-016), OSD + settings window in light theme,
OSD on a second monitor, tray icon on a light taskbar and after an explorer restart, an app/window icon (the exe and settings window have none yet),
media keys from a physical Consumer Control device (only the keyboard path is verified), media info
with Spotify/Media Player (Chromium and Telegram verified).
