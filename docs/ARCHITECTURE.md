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
 endpoint volume/mute ─COM cb───► AudioService ───┼─► OsdController ─► OsdState ─► OsdWindow (topmost,
 default device change ─COM cb──► AudioService ───┤   (OsdRules)      (immutable)  no-activate, click-
 media session (GSMTC) ─WinRT───► MediaService ───┘                                 through, Mica/Acrylic)
                                  SettingsStore ◄────────────────── SettingsWindow (MVVM)
                                                                    TrayIcon
```

## Layout

```
DazeMediaOsd.slnx
global.json                      pins .NET 10 SDK; opts `dotnet test` into Microsoft.Testing.Platform
Directory.Build.props            shared props: TFM net10.0-windows10.0.26100.0, nullable, warnings as errors
Directory.Packages.props         central package versions
src/DazeMediaOsd.Core/           no UI; Win32/COM via CsWin32 (NativeMethods.txt). Lifecycle/ (LaunchArgs,
                                 Launcher), NativeFlyout/ (FlyoutHost, FlyoutSuppressor), Audio/
                                 (CoreAudioService), Input/ (RawInputKeySource), Osd/ (OsdState, OsdRules,
                                 OsdController), Windowing/ (OverlayWindow styles), Diagnostics/Log.
                                 Later: media, settings.
src/DazeMediaOsd.App/            WinUI 3 exe `DazeMediaOsd.exe`: Program.Main → launcher or `--app` mode;
                                 App wires services; OsdWindow (+ AlwaysActiveAcrylicBackdrop). Later:
                                 TrayIcon, SettingsWindow, ViewModels
tests/DazeMediaOsd.Core.Tests/   xUnit v3; logic only, fakes for IAudioService / IKeySource
scripts/                         stop.ps1 (safe stop), press-key.ps1 (SendInput volume keys),
                                 audio-device.ps1 (list / set default output, dev only)
spikes/FlyoutProbe/              console probe for the native flyout — re-run after Windows updates.
                                 Own Directory.*.props opt out of the repo-wide ones.
```

Rule: Win32/COM/WinRT calls live in Core behind small interfaces so view models and state logic can be
unit-tested with fakes. App never P/Invokes directly; window styling goes through `Core.Windowing`.

## Components

| Component | Responsibility | Key APIs |
|---|---|---|
| `CoreAudioService` (`IAudioService`) | Default render + capture endpoint (role eConsole): name, volume, mute; raise on change, rename, default switch | `IMMDeviceEnumerator`, `IMMNotificationClient`, `IAudioEndpointVolume` + `IAudioEndpointVolumeCallback`, `PKEY_Device_FriendlyName` |
| `OsdController` + `OsdRules` | Audio changes + keys → `ShowRequested(OsdState)`; the rules decide what pops the OSD (D-007) | — (pure, unit-tested) |
| `NativeFlyout` | Minimize the native flyout host while the app runs, re-minimize a re-created host, restore on exit | `SetWinEventHook` (out-of-context, `EVENT_OBJECT_CREATE`) on explorer.exe, `ShowWindowAsync` |
| `RawInputKeySource` (`IKeySource`) | Trigger: volume/mute key downs, incl. at 0 %/100 % where the volume doesn't change. Keyboard VKs + Consumer Control HID reports (`ConsumerKeyTracker`: usages 0xE9/0xEA/0xE2, raise on newly held) | Message-only window, `RegisterRawInputDevices` (0x01/0x06 and 0x0C/0x01) + `RIDEV_INPUTSINK` (passive, no input latency), `HidP_GetUsagesEx` |
| `Launcher` | Start `--app`, wait on its handle, restore the native flyout when it exits (D-006) | `Process`, `WaitForSingleObject` |
| `MediaService` | Current media session: title, artist, thumbnail, playback state, controls | `GlobalSystemMediaTransportControlsSessionManager` |
| `SettingsStore` | Load/save JSON, versioned schema, change notifications | `System.Text.Json` source-gen (AOT-safe) |
| `OsdWindow` + `OverlayWindow` | Render `OsdState` (output name/level/mute, mic name/level/mute), auto-hide 2.5 s after the last show, bottom-centre of the primary work area (D-008) | WinUI 3 `Window`, `OverlappedPresenter.SetBorderAndTitleBar(true,false)`, `DesktopAcrylicController`, `SetWindowPos(SWP_NOACTIVATE)` |
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
  no native flyout at all. Host stays iconic and "visible" as a 276×45 rect at the bottom-left of the
  screen, but draws nothing and isn't hit (`WindowFromPoint` returns the window below). Explorer then
  raises SHOW only sometimes (LOCATIONCHANGE on every press), so it can't be our trigger. State **survives our
  process being killed**: no native OSD until child+host `SW_RESTORE` then host `SW_HIDE` (probe
  `restore` mode), or an explorer restart.

## Threading

- COM callbacks (Core Audio) arrive on arbitrary MTA threads: never touch UI there. Marshal to the UI via
  `DispatcherQueue.TryEnqueue`. Keep callbacks tiny — Core Audio docs warn against blocking in them.
- `CoreAudioService` owns one MTA worker thread that makes every COM call. Callbacks copy their
  arguments (pointers die with the call) and post to it — Register/Unregister must never run inside a
  Core Audio callback. `Changed` is raised on that worker.
- `RawInputKeySource` runs its own thread + message-only window; `KeyPressed` is raised there.
- `OsdController.ShowRequested` therefore arrives on either thread: App marshals with
  `DispatcherQueue.TryEnqueue`.
- WinEvent hooks need a thread with a message loop: `FlyoutSuppressor` is created on the UI thread.
- `Process.Exited` (launcher watch) fires on a pool thread: marshal to the UI thread before touching
  the suppressor.
- `OsdState` is an immutable record; services publish a new instance, the window diffs nothing — it
  just rebinds.

## Audio and keys (verified on build 26300, 2026-10-22)

- One volume key press: raw input key-down first, then ~5 ms later **two** endpoint volume
  notifications with slightly different floats → `OsdRules` compares the displayed percent.
- `SendInput` volume keys reach raw input (hDevice 0) and Windows alike → `scripts/press-key.ps1`
  exercises the keyboard path. It can't produce Consumer Control HID reports: that path is only
  verified with a physical media key/knob (log line `consumer-control key …`). The dev machine has 9
  consumer-control collections. A press seen on both paths raises twice; `OsdController` drops an
  identical request within 100 ms.
- Default-device switch attaches the device id the notification carries (re-querying the default
  could still return the old one).
- Default-device switch: `OnDefaultDeviceChanged` fires per role; we act on eConsole only and re-attach
  the volume callback; a late notification from the old endpoint is dropped by endpoint id.
- Raw input registration is per process and per device class: if a future window (settings) or WinUI
  itself registers keyboard raw input, one overrides the other — check text input when SettingsWindow
  lands.
- Log: `%LOCALAPPDATA%\DazeMediaOsd\logs\app.log`, both processes (pid per line). Until OsdWindow
  exists every would-be show is a `show <reason>: out=… in=…` line.

## OSD window (verified on build 26300, 2026-10-22; D-008)

- Created once at startup (before suppression; failure → exit, Windows untouched), hidden between shows.
  Never `Activate()`d: `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE|SWP_SHOWWINDOW)` renders XAML fine.
- Ex styles `NOACTIVATE|TOOLWINDOW|TOPMOST|TRANSPARENT|LAYERED` + `SetLayeredWindowAttributes(alpha 255)`:
  the acrylic backdrop and XAML still draw on the layered window; `WindowFromPoint` over it returns the
  window below (click-through); foreground window unchanged by a show.
- Frame: border without title bar gives DWM rounded corners + 1 px outline + shadow for free.
- Size: XAML `Root` is 340 DIP wide, height measured per show; × `GetDpiForWindow`/96 → physical px.
- `--app` with the window: ~176 MB working set / 86 MB private (launcher 36 / 9 MB). Watch against D-001.
- Not verified yet: light theme, multi-monitor/DPI change, show latency.

## Process lifecycle (D-006)

- Launcher mode (no args): start self with `--app`, wait. On child exit (clean, crash, killed): restore
  the host, exit. Autostart points at the launcher.
- App mode: minimize the host at start (also fixes stale state from an earlier double kill), restore on
  clean exit. Watches the launcher's handle: if the launcher dies, restore and fall back to per-show
  `SW_HIDE` (D-005) for the rest of the session.
- Only killing both at once (e.g. "End process tree") leaves the host minimized → fixed by the next
  app start, `DazeMediaOsd.exe --restore`, explorer restart or sign-out.
- The app watches the launcher **before** choosing a mode: Minimize only with a live, watched
  launcher; `--app` without `--launcher-pid`, a dead/recycled pid, or any process-query race starts
  in per-show mode.
- Single instance: a `Local\DazeMediaOsd.Launcher` mutex; a second launcher exits at once. The app
  holds `Local\DazeMediaOsd.App` too, so an app orphaned by a killed launcher blocks a second app
  (the new launcher's app exits, the launcher restores and exits).
- Per-show mode starts by restoring the host, clearing a minimized state left by an earlier double kill.
- Launcher identity: same process name as us and started before us, else it's a recycled pid.
- Launcher mode costs ~9 MB private / ~34 MB working set (2026-10-22), so it stays the same exe.
- Verified 2026-10-22 on 26300: running → no native flyout; kill `--app` → launcher restores, exits;
  kill launcher → app restores, hides per show. Clean exit untested until the tray has an Exit item.

## Invariants

- **Fail-open:** whenever our app is not running, the native flyout works. It is minimized only while
  the app runs, and every exit path restores it (D-006). Audio and key sources start **before** any
  suppression; if either fails, the app logs it and exits with Windows untouched.
- OSD window never takes focus or activation (would steal focus from games/apps on every key press).
- No admin rights required.

## Open questions

- **Q3:** tray icon: H.NotifyIcon.WinUI vs. own `Shell_NotifyIcon` via CsWin32 (pick the AOT-safe one).
- **Q4:** the same host very likely also shows the brightness / airplane-mode / other hardware-key
  flyouts, so minimizing it probably hides those too. Not tested: brightness keys aren't scriptable on
  the dev machine. If confirmed, ask the user: accept, or show those in our OSD (brightness is already
  "Later" in GOALS). UIA can't read the island content (X-001).
