# Architecture

Describes the system **as it is now** (or as planned, where marked _planned_). No history here — when
something changes, rewrite the section and put the reason in [DECISIONS.md](DECISIONS.md).

## Overview

One tray-resident WinUI 3 app process, started and guarded by a launcher process of the same exe
(D-001, D-003, D-006). Windows signals (volume keys, device changes, media sessions) flow into a small
core that builds an `OsdState`; the OSD window renders it.

```
 DazeMediaOsd.exe (launcher mode) ── starts, waits ──► DazeMediaOsd.exe --app   (they watch each other)
   └─ when the app exits for any reason: restore the native flyout ("finally"), then exit

 Windows                         DazeMediaOsd.Core                  DazeMediaOsd.App (WinUI 3)
 ─────────                       ─────────────────                  ──────────────────────────
 flyout host create ─WinEvent───► NativeFlyout (minimize/restore)
 volume keys ───────raw input───► KeySource ──────┐
 endpoint volume/mute ─COM cb───► AudioService ───┼─► OsdState ───► OsdWindow (topmost, no-activate,
 default device change ─COM cb──► AudioService ───┤   (immutable)    click-through, Mica/Acrylic)
 media session (GSMTC) ─WinRT───► MediaService ───┘
                                  SettingsStore ◄────────────────── SettingsWindow (MVVM)
                                                                    TrayIcon
```

## Layout _(planned)_

```
DazeMediaOsd.slnx
global.json                      pins .NET 10 SDK
Directory.Build.props            shared props (nullable, TFM, warnings as errors)
Directory.Packages.props         central package versions
src/DazeMediaOsd.Core/           net10.0-windows…: no UI. Audio, media, native flyout, settings. Testable.
src/DazeMediaOsd.App/            WinUI 3 exe `DazeMediaOsd.exe`: custom Main → launcher or `--app` mode;
                                 App, TrayIcon, OsdWindow, SettingsWindow, ViewModels
tests/DazeMediaOsd.Core.Tests/   xUnit; logic only (state building, settings migration)
scripts/                         dev helper scripts
spikes/FlyoutProbe/              (exists) console probe: logs/suppresses the native flyout — re-run after Windows updates
```

Rule: Win32/COM/WinRT calls live in Core behind small interfaces so view models and state logic can be
unit-tested with fakes. App never P/Invokes directly except window styling.

## Components

| Component | Responsibility | Key APIs |
|---|---|---|
| `AudioService` | Default render + capture endpoint: name, volume, mute; raise on change | `IMMDeviceEnumerator`, `IMMNotificationClient` (`OnDefaultDeviceChanged`), `IAudioEndpointVolume` + `IAudioEndpointVolumeCallback`, `PKEY_Device_FriendlyName` |
| `NativeFlyout` | Minimize the native flyout host while the app runs, re-minimize a re-created host, restore on exit | `SetWinEventHook` (out-of-context, `EVENT_OBJECT_CREATE`) on explorer.exe, `ShowWindowAsync` |
| `KeySource` | Trigger: volume/mute key presses, incl. at 0 %/100 % where the volume doesn't change | `RegisterRawInputDevices` + `RIDEV_INPUTSINK` (passive, no input latency) |
| `Launcher` | Start `--app`, wait on its handle, restore the native flyout when it exits (D-006) | `Process`, `WaitForSingleObject` |
| `MediaService` | Current media session: title, artist, thumbnail, playback state, controls | `GlobalSystemMediaTransportControlsSessionManager` |
| `SettingsStore` | Load/save JSON, versioned schema, change notifications | `System.Text.Json` source-gen (AOT-safe) |
| `OsdWindow` | Render `OsdState`, auto-hide timer, position per settings | WinUI 3 `AppWindow`, backdrop, `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW \| WS_EX_TRANSPARENT` |
| `SettingsWindow` | Fluent settings UI | WinUI 3 `NavigationView`, CommunityToolkit.Mvvm |
| `TrayIcon` | Open settings, pause, exit | TBD (Q3) |

## Native flyout (verified on build 26300; strategy D-006)

- Host: top-level `XamlExplorerHostIslandWindow`, owned by the shell `explorer.exe` (pid of
  `GetShellWindow()`), z-band 18 = `ZBID_ABOVELOCK_UX` (undocumented `GetWindowBand`). Child:
  `Windows.UI.Composition.DesktopWindowContentBridge` titled `DesktopWindowXamlSource`.
- Explorer has other `XamlExplorerHostIslandWindow`s (e.g. "Task Switching" band 16, one band 7) — the
  band is what identifies the flyout host.
- One host per explorer instance, reused for every show. Explorer creates it lazily (first key press),
  so also watch `EVENT_OBJECT_CREATE`; explorer restart → new pid → re-hook.
- Stock behaviour: every key press raises `EVENT_OBJECT_SHOW` on the host 4–14 ms after the key, even
  while already visible. Native auto-hide raises `EVENT_OBJECT_HIDE` ~2.5 s after the last key.
- Per-show hide: `ShowWindowAsync(host, SW_HIDE)` on each SHOW. Explorer keeps working; after our process
  exits the next press shows the native flyout as stock (fail-open). But the native flyout visibly
  flashes now and then before the hide lands (user, real keys, 2026-10-22).
- Minimized host (Flylet's "permanent hide": child `SW_MINIMIZE`, host `SW_MINIMIZE` + `SW_FORCEMINIMIZE`):
  no native flyout at all. Host stays iconic and "visible" at a tiny rect; explorer then raises SHOW
  only sometimes (LOCATIONCHANGE on every press), so it can't be our trigger. State **survives our
  process being killed**: no native OSD until child+host `SW_RESTORE` then host `SW_HIDE` (probe
  `restore` mode), or an explorer restart.

## Threading

- COM callbacks (Core Audio) arrive on arbitrary MTA threads: never touch UI there. Marshal to the UI via
  `DispatcherQueue.TryEnqueue`. Keep callbacks tiny — Core Audio docs warn against blocking in them.
- WinEvent hooks need a thread with a message loop: install them on the UI thread.
- `OsdState` is an immutable record; services publish a new instance, the window diffs nothing — it
  just rebinds.

## Process lifecycle (D-006)

- Launcher mode (no args): start self with `--app`, wait. On child exit (clean, crash, killed): restore
  the host, exit. Autostart points at the launcher.
- App mode: minimize the host at start (also fixes stale state from an earlier double kill), restore on
  clean exit. Watches the launcher's handle: if the launcher dies, restore and fall back to per-show
  `SW_HIDE` (D-005) for the rest of the session.
- Only killing both at once (e.g. "End process tree") leaves the host minimized → fixed by the next
  app start, explorer restart, sign-out, or `FlyoutProbe restore`.

## Invariants

- **Fail-open:** whenever our app is not running, the native flyout works. It is minimized only while
  the app runs, and every exit path restores it (D-006).
- OSD window never takes focus or activation (would steal focus from games/apps on every key press).
- No admin rights required.

## Open questions

- **Q2:** WinUI 3 window with true per-pixel transparency + rounded corners for the OSD — check
  Windows App SDK 2.x backdrop APIs vs. WinUIEx before hand-rolling.
- **Q3:** tray icon: H.NotifyIcon.WinUI vs. own `Shell_NotifyIcon` via CsWin32 (pick the AOT-safe one).
- **Q4:** the same host very likely also shows the brightness / airplane-mode / other hardware-key
  flyouts, so minimizing it probably hides those too. Not tested: brightness keys aren't scriptable on
  the dev machine. If confirmed, ask the user: accept, or show those in our OSD (brightness is already
  "Later" in GOALS). UIA can't read the island content (X-001).
- **Q5:** working set of the launcher-mode process (same exe, WinUI never started). If it's heavy, split
  the launcher into a tiny Native AOT console exe (D-006 "Revisit if").
