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
spikes/FlyoutProbe/              (exists) console probe: logs/suppresses the native flyout — re-run after Windows updates
```

Rule: Win32/COM/WinRT calls live in Core behind small interfaces so view models and state logic can be
unit-tested with fakes. App never P/Invokes directly except window styling.

## Components

| Component | Responsibility | Key APIs |
|---|---|---|
| `AudioService` | Default render + capture endpoint: name, volume, mute; raise on change | `IMMDeviceEnumerator`, `IMMNotificationClient` (`OnDefaultDeviceChanged`), `IAudioEndpointVolume` + `IAudioEndpointVolumeCallback`, `PKEY_Device_FriendlyName` |
| `FlyoutWatcher` | Know when Windows wants to show the volume OSD; hide the native flyout | `SetWinEventHook` (out-of-context) on explorer.exe, `ShowWindowAsync` |
| `MediaService` | Current media session: title, artist, thumbnail, playback state, controls | `GlobalSystemMediaTransportControlsSessionManager` |
| `SettingsStore` | Load/save JSON, versioned schema, change notifications | `System.Text.Json` source-gen (AOT-safe) |
| `OsdWindow` | Render `OsdState`, auto-hide timer, position per settings | WinUI 3 `AppWindow`, backdrop, `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW \| WS_EX_TRANSPARENT` |
| `SettingsWindow` | Fluent settings UI | WinUI 3 `NavigationView`, CommunityToolkit.Mvvm |
| `TrayIcon` | Open settings, pause, exit | TBD (Q3) |

## Native flyout (verified on build 26300, D-005)

- Host: top-level `XamlExplorerHostIslandWindow`, owned by the shell `explorer.exe` (pid of
  `GetShellWindow()`), z-band 18 = `ZBID_ABOVELOCK_UX` (undocumented `GetWindowBand`). Child:
  `Windows.UI.Composition.DesktopWindowContentBridge` titled `DesktopWindowXamlSource`.
- Explorer has other `XamlExplorerHostIslandWindow`s (e.g. "Task Switching" band 16, one band 7) — the
  band is what identifies the flyout host.
- One host per explorer instance, reused for every show. Explorer creates it lazily (first key press),
  so also watch `EVENT_OBJECT_CREATE`; explorer restart → new pid → re-hook.
- Every key press raises `EVENT_OBJECT_SHOW` on the host 4–14 ms after the key, even while the flyout
  is already visible or we hid it. Native auto-hide raises `EVENT_OBJECT_HIDE` ~2.5 s after the last key.
- Suppression: `ShowWindowAsync(host, SW_HIDE)` on each SHOW. Explorer keeps working; after our process
  exits the next press shows the native flyout as stock (fail-open confirmed).

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

- **Q2:** WinUI 3 window with true per-pixel transparency + rounded corners for the OSD — check
  Windows App SDK 2.x backdrop APIs vs. WinUIEx before hand-rolling.
- **Q3:** tray icon: H.NotifyIcon.WinUI vs. own `Shell_NotifyIcon` via CsWin32 (pick the AOT-safe one).
- **Q4 (before FlyoutWatcher):** the same host very likely also shows the brightness / airplane-mode /
  other hardware-key flyouts — hiding every SHOW would swallow those too. Need a cheap "this SHOW is
  volume" signal: candidates are passive raw input (`RegisterRawInputDevices` + `RIDEV_INPUTSINK` for
  `VK_VOLUME_*`, no input latency unlike a LL hook) or an `IAudioEndpointVolumeCallback` within a few
  ms (fails at 0 %/100 % where no change fires). UIA can't read the island content (X-001). Not tested:
  brightness keys aren't scriptable on the dev machine.
- **Q5:** SW_HIDE lands 4–14 ms after SHOW — check by eye whether the native flyout's first frame
  flashes (`FlyoutProbe manual` + real key presses). If it does, revisit D-005.
