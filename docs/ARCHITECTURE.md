# Architecture

Describes the system **as it is now** (or as planned, where marked _planned_). No history here — when
something changes, rewrite the section and put the reason in [DECISIONS.md](DECISIONS.md).

## Overview

One tray-resident WinUI 3 process (D-001, D-003). Windows signals (volume keys, device changes, media
sessions) flow into a small core that builds an `OsdState`; the OSD window renders it.

```
 Windows                         DazeMediaOsd.Core                  DazeMediaOsd.App (WinUI 3)
 ─────────                       ─────────────────                  ──────────────────────────
 native flyout shown ──WinEvent──► FlyoutWatcher ─┐
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
src/DazeMediaOsd.Core/           net10.0-windows…: no UI. Audio, media, flyout watcher, settings. Testable.
src/DazeMediaOsd.App/            WinUI 3 exe: App, TrayIcon, OsdWindow, SettingsWindow, ViewModels
tests/DazeMediaOsd.Core.Tests/   xUnit; logic only (state building, settings migration)
scripts/                         dev helper scripts
```

Rule: Win32/COM/WinRT calls live in Core behind small interfaces so view models and state logic can be
unit-tested with fakes. App never P/Invokes directly except window styling.

## Components

| Component | Responsibility | Key APIs |
|---|---|---|
| `AudioService` | Default render + capture endpoint: name, volume, mute; raise on change | `IMMDeviceEnumerator`, `IMMNotificationClient` (`OnDefaultDeviceChanged`), `IAudioEndpointVolume` + `IAudioEndpointVolumeCallback`, `PKEY_Device_FriendlyName` |
| `FlyoutWatcher` | Know when Windows wants to show the volume OSD; hide the native flyout | `SetWinEventHook` (out-of-context) on explorer.exe, `ShowWindow`/`SetWindowPos` |
| `MediaService` | Current media session: title, artist, thumbnail, playback state, controls | `GlobalSystemMediaTransportControlsSessionManager` |
| `SettingsStore` | Load/save JSON, versioned schema, change notifications | `System.Text.Json` source-gen (AOT-safe) |
| `OsdWindow` | Render `OsdState`, auto-hide timer, position per settings | WinUI 3 `AppWindow`, backdrop, `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW \| WS_EX_TRANSPARENT` |
| `SettingsWindow` | Fluent settings UI | WinUI 3 `NavigationView`, CommunityToolkit.Mvvm |
| `TrayIcon` | Open settings, pause, exit | TBD (Q3) |

## Threading

- COM callbacks (Core Audio) arrive on arbitrary MTA threads: never touch UI there. Marshal to the UI via
  `DispatcherQueue.TryEnqueue`. Keep callbacks tiny — Core Audio docs warn against blocking in them.
- WinEvent hooks need a thread with a message loop: install them on the UI thread.
- `OsdState` is an immutable record; services publish a new instance, the window diffs nothing — it
  just rebinds.

## Invariants

- **Fail-open:** never permanently disable or move the native flyout; hide it only while ours is shown.
  If our process dies, Windows behaves as stock.
- OSD window never takes focus or activation (would steal focus from games/apps on every key press).
- No admin rights required.

## Open questions

- **Q1 (spike first):** exact window class/hierarchy of the Win11 26100+ volume flyout host and the
  most reliable trigger: WinEvent on the native host (mirrors when Windows shows the OSD — preferred)
  vs. low-level keyboard hook on `VK_VOLUME_*` (sees keys only, adds latency to all input, anti-cheat
  noise). Read Flylet's MIT source (github.com/ItsMrZxD/Flylet) for current class names. Note: Win11
  creates the host lazily, after the first volume key press.
- **Q2:** WinUI 3 window with true per-pixel transparency + rounded corners for the OSD — check
  Windows App SDK 2.x backdrop APIs vs. WinUIEx before hand-rolling.
- **Q3:** tray icon: H.NotifyIcon.WinUI vs. own `Shell_NotifyIcon` via CsWin32 (pick the AOT-safe one).
