# Goals

What the user wants and where the project stands. Keep it current: an agent should be able to read only
this file and know what to build next. History of *why* lives in [DECISIONS.md](DECISIONS.md).

## User intent

- Extend the Windows 11 media OSD (the flyout shown on volume/media keys) with extra information. The
  first concrete ask: **show the current output and input device when changing volume.**
- Windows 11 only. No cross-platform concerns, ever.
- A configuration UI with a modern design (should feel like a native Windows 11 app). Its wording is
  plain and friendly, not geeky (user, 2026-10-23; D-026).
- The repo is driven purely by AI agents. Decisions, reasons, rejected options and dead ends are written
  down so the next agent understands *why* the code is the way it is — but kept lean, no bloat.
- v1 builds on the local machine. GitHub CI and a Setup.exe from GitHub Releases came early (user, 2026-10-24; D-029).

## v1 scope

- Tray-resident app that replaces the native volume OSD with its own Win11-styled OSD (see D-002).
- OSD shows: volume level + mute state, **default output device name**, **default input device name**
  (+ mic mute state). Media info (title/artist/controls) when a media session is active.
- OSD also appears when the default output/input device changes, and on media keys
  (play/pause/next/previous) (user, 2026-10-22). Optionally also on a track change or play/pause that
  didn't come from a key, off by default (user, 2026-10-24; D-032).
- OSD animates in and out like the native flyout (user, 2026-10-22).
- OSD pops up when any audio device (Bluetooth, USB, …) connects or disconnects; on by default, off in
  settings. It still shows the current default output/input as always (user, 2026-10-22), and also
  which device came or went, in a compact, modern way that doesn't clutter the OSD (user, 2026-10-22).
- Settings window: enable/disable sections, position, timeout, theme follows system, start with Windows.
- Logs keep at most the last 7 days, older ones are deleted automatically (user, 2026-10-23).
- Only our OSD is ever visible — the native flyout must not flash (user, 2026-10-22; D-006).
- Fail-open: if the app is closed or crashes, the native OSD keeps working.
- Next features, picked from the PR #8 survey (user, 2026-10-23): **device-type icons** on the output
  and input rows, and **quick switching** of the default output/input device. Then **a hotkey per
  device**: one press makes that device the default (user, 2026-10-23). A press on a paired Bluetooth
  device that isn't connected connects it first (user, 2026-10-23).
- Plan D, mouse wheel over the taskbar (user, 2026-10-28; D-035): the wheel over the task buttons and the
  wheel over the tray (notification area + clock) are **separate** settings, each with its own action.
  First both can change the volume. Then the tray gets **brightness** of the monitor under the cursor.

## Later (not v1)

- v2: code signing (SmartScreen, D-029).
- Airplane-mode / keyboard-layout flyouts. Brightness keys (Q4); the brightness OSD from Plan D may cover them.
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
- [x] Plan C: a hotkey per device (D-022): Shortcuts page (cycle card + device rows, recorder control),
  `deviceHotkeys` in settings, `GlobalHotkey` ids. Seen: Speakers ↔ Technics, Microphone Array ↔ Stereo
  Mix by chord; already default → OSD; Stereo Mix disabled → "Not connected" line, no switch; duplicate
  chords refused with a note (2026-10-23)

- [x] Bluetooth connect on a device shortcut (D-023): inactive device → `KSPROPERTY_ONESHOT_RECONNECT` to all
  its endpoints, switch once connected; "Connecting…" / "Couldn't connect" / "Not connected" line. Seen:
  Technics after a probe disconnect (2.9 s) and after a Windows "Disconnect" (9 s), made default
  (2026-10-23). Not seen: the OSD lines on screen (log only), a non-Intel-offload Bluetooth adapter

- [x] App identity: own mark for exe, settings window, title bar and tray (D-025); version 0.1.0 + commit in
  About and the launcher log (D-024); About page with Copy details, folders, log, GitHub. Seen: About,
  title bar and taskbar icons, copied details (2026-10-23)
- [x] Settings wording rewritten by a UX-writer pass (D-026): "pop-up" for the OSD, "What to show",
  positions in screen order, plain notes. Seen: all three pages (2026-10-23)
- [x] README as a showcase: hero banner, pop-up and settings shots in docs/images (user, 2026-10-23)
- [x] License: GPL-3.0-or-later + Windows App SDK exception, picked by the user in a questionnaire (D-027)
  (2026-10-23)
- [x] Log rotation (D-028): first write of a day renames `app.log` to `app-<date>.log`, archives ≥ 7 days
  old deleted. Seen: `Log` on a temp folder with a stale `app.log` (archived) and 7/6-day-old archives
  (deleted / kept) (2026-10-23); a real midnight on the daily driver: `app-2026-10-23.log` + fresh `app.log`
- [x] Crash on a system light/dark switch fixed (X-007); unhandled XAML-event exceptions now logged with
  their stack. Seen: 4 switches from Settings, OSD light then dark (2026-10-24)
- [x] CI + release packaging (D-029): `ci.yml` builds/tests PRs; a `v<Version>` tag publishes a GitHub Release
  with a per-user Velopack Setup.exe; `install.ps1` installs through the same Setup.exe (one folder). Seen
  locally: pack, install, run, uninstall restored the native flyout, settings kept (2026-10-24). v0.3.0
  released by tag (2026-10-24): Setup.exe, zip, full package, feed; the installed app's check said up to date
- [x] Release on merge (D-031): a push to main whose `<Version>` has no tag releases it. Not seen yet: the
  first automatic release (the next feat/fix merge)
- [x] In-app updates (D-030): daily check of GitHub Releases, background download, applied at the next
  start; About shows status, Check for updates and Restart now. Seen with a local feed: 0.3.0 → 0.3.1
  (2026-10-24). Not seen: the About card on screen, Restart now, the real GitHub feed
- [x] Opt-in pop on track change / play-pause (D-032): "When the song changes or pauses" toggle under Now
  playing, off by default; unit-tested. Not seen yet: the toggle on screen, real players (log `show MediaPlayback`)
- [x] Cover lag (user saw it, 2026-10-24; D-033): title first, cover crossfades in when read and decoded,
  decoded at display size. Not seen yet: on screen; how long Spotify's cover read takes (log `media thumbnail took`)
- [x] Changes in a visible OSD fade through (user liked the cover fade, 2026-10-24; D-034): track text, play
  state, device names, mute icons. Volume stays instant. Not seen yet: on screen

- [ ] Plan D (wheel over the taskbar, D-035), three PRs in order:
  1. **Spike** `spikes/TaskbarWheelProbe` (answers Q6, Q7): log every wheel notch with the hit zone (tasks /
     tray / none) and the window under the cursor, on the primary and a secondary taskbar, auto-hide on, the
     taskbar at 100 %/125 %. Also: does Windows itself change the volume on a wheel over the speaker icon,
     and does a wheel over task buttons do anything native.
  2. **Volume** (`feat/taskbar-wheel-volume`): `TaskbarWheelSource` (Core/Input) → `TaskbarZone` hit test →
     `WheelAction` per zone from settings → output volume ± step per notch (`IAudioEndpointVolume`); the
     existing volume-changed rule pops the OSD. Settings: a "Mouse wheel" card with two pickers (Over the
     taskbar apps / Over the tray: Change volume, Do nothing) + step size. Tests: delta accumulation,
     zone → action mapping, settings round-trip.
  3. **Brightness** (`feat/tray-wheel-brightness`): `Brightness` service (internal panel via WMI
     `WmiMonitorBrightnessMethods`, external via DDC/CI `SetMonitorBrightness`), target = monitor under the
     cursor; "Change brightness" added to the tray picker (its default then); a brightness OSD layout
     (sun glyph, %, monitor name). Coalesce DDC/CI writes on a worker (slow, ~50 ms+).

**Next step:** Plan D step 1, the spike (above). Unrelated, still pending from before: look at the new toggle in Settings and turn it on for a day: a track change in Spotify/Chromium
pops once (log `show MediaPlayback`), a media key doesn't pop twice visibly, browser videos don't spam.
Then: the next feat/fix merge is the first automatic release (D-031): check its Release run and
that the daily driver (0.3.0 in `%LOCALAPPDATA%\DazeMediaOsdApp`) downloads it within a day (log
`updates: <v> ready`) and runs it after the next start; look at About → Updates on screen once. Then the user lives with the finished Plan C and Bluetooth connect (D-023) on the daily driver for a
day (real device shortcuts, presses while the headphones are off or disconnected) and reports back. Then pick the next feature from the PR #8 survey with the user. When convenient: the headset glyph
(Technics hands-free as default mic) and the monitor glyph (Dell over DisplayPort).

Plan B (quick switching) is done: tray menu (D-019), cycle-output hotkey (D-020, D-021). Its third
surface, clicking a device row in the OSD, was dropped (Non-goals).

Plan C (a hotkey per device) is done (D-022). A paired Bluetooth device that isn't connected gets
connected, then switched to (D-023); a device that can't be asked (wired, unpaired, old pairing) shows
"<name> · Not connected" and doesn't switch (user, 2026-10-23). A tray Output/Input pick doesn't cancel a pending
connect (only shortcuts do); the user left that for now (2026-10-23).

When convenient: autostart at sign-in (log `launcher: started app` right after one), a USB audio
device (one show per connect/disconnect, D-013/D-014/D-016), OSD + settings window in light theme,
OSD on a second monitor, tray icon on a light taskbar and after an explorer restart (the new mark: also
how its 16/20 px frames read at 100 %/125 %),
media keys from a physical Consumer Control device (only the keyboard path is verified), media info
with Spotify/Media Player (Chromium and Telegram verified).
