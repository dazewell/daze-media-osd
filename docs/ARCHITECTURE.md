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
 device (dis)connect ───COM cb──► AudioService ───┤
 media session (GSMTC) ─WinRT───► MediaService ───┘                                 through, Mica/Acrylic)
 taskbar wheel ──────raw input───► TaskbarWheelSource ─► WheelVolume ──────► AudioService (volume step)
                                                     └► WheelBrightness ─► BrightnessService ─WMI─► built-in screen
                                                                           └ Changed ─► OsdState (brightness) ─► OsdWindow
                                  SettingsStore ◄────────────────── SettingsWindow (MVVM)
                                                                    TrayIcon
```

## Layout

```
DazeMediaOsd.slnx
global.json                      pins .NET 10 SDK; opts `dotnet test` into Microsoft.Testing.Platform
Directory.Build.props            shared props: TFM net10.0-windows10.0.26100.0, nullable, warnings as errors;
                                 `<Version>` + product metadata (D-024)
Directory.Packages.props         central package versions
dotnet-tools.json                pins vpk (Velopack CLI) for scripts/pack.ps1 and CI (D-029)
src/DazeMediaOsd.Core/           no UI; Win32/COM via CsWin32 (NativeMethods.txt). Lifecycle/ (LaunchArgs,
                                 Launcher, AppVersion, Autostart, ExitSignal), Updates/ (UpdateService,
                                 VelopackUpdateClient), NativeFlyout/ (FlyoutHost, FlyoutSuppressor), Audio/
                                 (CoreAudioService, DeviceChoices, DeviceCycle, DeviceConnector), Input/ (RawInputKeySource, GlobalHotkey, Hotkey, Shortcuts, TaskbarWheelSource, WheelModel, WheelVolume, WheelBrightness), Brightness/ (BrightnessModel, BrightnessPlanner, BrightnessService, WmiBrightnessBackend), Osd/ (OsdState, OsdRules,
                                 OsdController, DeviceNotice, OsdPlacement, OsdAnimation), Windowing/ (OverlayWindow styles), Tray/
                                 (TrayIcon), Settings/ (AppSettings, DeviceHotkey, SettingsStore), Media/
                                 (GsmtcMediaService, CsWinRT), Diagnostics/Log.
src/DazeMediaOsd.App/            WinUI 3 exe `DazeMediaOsd.exe`: Program.Main → launcher or `--app` mode;
                                 App wires services; OsdWindow (+ AlwaysActiveAcrylicBackdrop);
                                 SettingsWindow + HotkeyRecorder (D-021 box as a control) + ViewModels/ (SettingsViewModel,
                                 DeviceHotkeyRowViewModel); Assets/ app.ico (exe + window),
                                 app.png (About, title bar), tray-dark/light.ico — all from scripts/make-icon.ps1
                                 (D-025; `-PreviewDir` writes a sheet of every frame)
tests/DazeMediaOsd.Core.Tests/   xUnit v3; logic only, fakes for IAudioService / IKeySource / IMediaService / IBrightnessBackend
scripts/                         run.ps1 (stop + build + start), stop.ps1 (safe stop), press-key.ps1
                                 (SendInput volume/media keys), audio-device.ps1 (list / set default output,
                                 dev only), install.ps1 (daily-driver copy), pack.ps1 (Velopack release, D-029)
.github/workflows/               ci.yml (build + test on PRs and main), release.yml (push to main with an untagged
                                 <Version>, or tag v<Version> → test,
                                 pack, GitHub Release with Setup.exe + update feed, D-029)
docs/images/                     README art: hero.png and framed shots, captured from the running app (demo
                                 media page, Stereo Mix as the connected device). Re-shoot after UI changes.
spikes/FlyoutProbe/              console probe for the native flyout — re-run after Windows updates.
spikes/BtProbe/                  every endpoint's state, container, KS filter and BtAudio support; connect /
                                 disconnect one (D-023). Re-run after Windows or audio driver updates.
                                 spikes/Directory.*.props opt out of the repo-wide ones.
```

Rule: Win32/COM/WinRT calls live in Core behind small interfaces so view models and state logic can be
unit-tested with fakes. App never P/Invokes directly; window styling goes through `Core.Windowing`.

## Components

| Component | Responsibility | Key APIs |
|---|---|---|
| `CoreAudioService` (`IAudioService`) | Default render + capture endpoint (role eConsole): name, volume, mute; raise on change, rename, default switch; set of active endpoints (any flow) → raise on connect/disconnect with a `DeviceEvent` (name, flow, Bluetooth, kind); `Endpoints(flow)` lists active ones, `SetDefault` switches (D-019), `Connect(id)` asks a paired Bluetooth device to connect (D-023) | `IMMDeviceEnumerator`, `IMMNotificationClient`, `IAudioEndpointVolume` + `IAudioEndpointVolumeCallback`, `PKEY_Device_FriendlyName`, `PKEY_Device_EnumeratorName`, `PKEY_AudioEndpoint_FormFactor` (→ `DeviceKind`, D-017), `EnumAudioEndpoints`, `IMMDevice.GetState`, `IMMEndpoint`, undocumented `IPolicyConfig` (Interop/PolicyConfig.cs), `IDeviceTopology` → `IConnector.GetDeviceIdConnectedTo` → `IKsControl` (`KSPROPSETID_BtAudio`), `PKEY_Device_ContainerId` |
| `DeviceConnector` | One pending press for an inactive device (D-023): `Connect` → `IAudioService.Connect`; when that endpoint's `DeviceConnected` arrives within 15 s → `SetDefault`; else `TimedOut` (deadline rechecked under the lock: a stale callback re-arms). A later press replaces it; App calls `Cancel` on every shortcut press first, and shows "Connecting…" only while `IsWaitingFor` that press | `TimeProvider` timer (pure, unit-tested) |
| `OsdController` + `OsdRules` | Audio changes + keys → `ShowRequested(OsdState)`; media session changes → `RefreshRequested` (re-render a visible OSD), or a `MediaPlayback` show on a new track / play state (D-032; App refreshes instead unless `showOnMediaChange`); the rules decide what pops the OSD (D-011); device connect/disconnect shows once, 500 ms after a burst (D-013), carrying the burst as one `DeviceNotice` (also on a default switch that cancels it); a burst repeating the device announced < 2 s ago is dropped (D-016) | — (pure, unit-tested) |
| `NativeFlyout` | Minimize the native flyout host while the app runs, re-minimize a re-created host, restore on exit | `SetWinEventHook` (out-of-context, `EVENT_OBJECT_CREATE`) on explorer.exe, `ShowWindowAsync` |
| `RawInputKeySource` (`IKeySource`) | Trigger: volume/mute and play-pause/next/previous key downs (`MediaKey`), incl. at 0 %/100 % where the volume doesn't change. Keyboard VKs + Consumer Control HID reports (`ConsumerKeyTracker`: usages 0xE9/0xEA/0xE2, 0xCD/0xB0/0xB1 → PlayPause, 0xB5/0xB6; raise on newly held) | Message-only window, `RegisterRawInputDevices` (0x01/0x06 and 0x0C/0x01) + `RIDEV_INPUTSINK` (passive, no input latency), `HidP_GetUsagesEx` |
| `TaskbarWheelSource` (`ITaskbarWheelSource`) + `WheelVolume` + `WheelBrightness` + `WheelRules` | Wheel over a taskbar → `Wheeled(zone, delta, held keys)`. Zone: root of `WindowFromPoint` must be `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` (so a fullscreen app or the tray overflow popup over it doesn't count); inside its `TrayNotifyWnd` rect → Tray, else Apps (Q6); the speaker icon → VolumeIcon, found by UIA (a `SystemTray.*` button whose name holds the output's name, or starts "Volume"), ~70 ms per lookup, run on the thread pool at start, and when the reading is > 0.5 s old as the cursor reaches the tray (move input, checked ≤ 5×/s) or a notch lands; the wheel thread only reads the cache and never waits. Unknown (not read, > 2 s old, UIA failed) skips the tray notch rather than risk doubling the icon's step: only a cursor that jumps to the tray without moving and scrolls at once loses its first notch. `WheelVolume`: `WheelRules.Matches(area, key, zone, held)` (exact key, never VolumeIcon, D-036), `WheelAccumulator` turns deltas into 120-notches, `IAudioService.StepOutputVolume(notches × step %)`, `Stepped` → App shows `OsdReason.Wheel` (unlogged; the `VolumeChanged` that follows is). `WheelBrightness`: the same rule on its own settings (D-040), stands aside when the volume rule matches the same turn, then `IBrightnessService.Adjust(notches × step)`. `WheelRules.Overlaps` / `Conflict` say whether two actions would share a turn (the settings refuse it) | Message-only window, `RegisterRawInputDevices` (0x01/0x02) + `RIDEV_INPUTSINK`, `GetAsyncKeyState`, `IUIAutomation.FindAll` (CsWin32) |
| `Launcher` | Start `--app`, wait on its handle, restore the native flyout when it exits (D-006) | `Process`, `WaitForSingleObject` |
| `GsmtcMediaService` (`IMediaService`) | Current session → `MediaState` (app id, title, artist, playing, thumbnail bytes) or null when none / no title. Starts in the background; a GSMTC failure only logs (media is optional). Every event marks dirty; one refresh loop re-reads; a failed read keeps the last state and retries (5 × 200 ms). A new track/play state is published with the previous cover before the new cover is read, then again with it (D-033; a read > 50 ms logs `media thumbnail took …`) | `GlobalSystemMediaTransportControlsSessionManager` (CsWinRT via the TFM), `TryGetMediaPropertiesAsync`, `GetPlaybackInfo`, thumbnail via `DataReader` (≤ 4 MB) |
| `GlobalHotkey` + `Hotkey` + `Shortcuts` | Every global shortcut (D-022): `Shortcuts.From(settings)` gives id 1 = cycle output (default Win+Alt+A, D-020), 2 + row = device row; `Set(id → chord)` re-registers all and returns `Errors` by id; `Pause` while any Settings recorder has focus (D-021); `Pressed(id)` → App: cycle picks `DeviceCycle.Next`, a device row `Shortcuts.Press` → `SetDefault`, or the OSD (`OsdReason.Hotkey`) when already default / nothing to cycle to. An inactive device goes to `DeviceConnector`; the event line answers with `DeviceNotice.Answer` (`PressAnswer`): "Connecting…", "Couldn't connect" (timeout) or "Not connected" (nothing took the request) | Message-only window, `RegisterHotKey` + `MOD_NOREPEAT`, `SendMessage` to hand over a new chord |
| `BrightnessService` (`IBrightnessService`) + `BrightnessPlanner` + `WmiBrightnessBackend` (`IBrightnessBackend`) | The built-in screen's brightness, safe by construction (D-039, D-041; section Brightness). `Adjust(±points)` from the wheel thread only adds to a counter; one MTA worker reads the panel, `BrightnessPlanner.Plan` picks a level the panel lists (floor 5 %), writes it, re-reads until it moves, then raises `Changed(name, %)` → App shows `OsdReason.Brightness`. `Refresh()` (settings page) only reads; `ScreenName` / `Probed` / `ScreenChanged` name the screen. A failure drops the step, resets the connection and logs once | WMI in `ROOT\WMI` (`WmiMonitorBrightness`, `WmiMonitorBrightnessMethods.WmiSetBrightness`, `WmiMonitorID` for the model) through CsWin32's `IWbemLocator` / `IWbemServices`; hand-written `CoSetProxyBlanket` (X-011) |
| `SettingsStore` + `AppSettings` | `%LOCALAPPDATA%\DazeMediaOsd\settings.json` (`version` 1): position, hide-after seconds (1–10), mic row on/off, media row on/off (`showMedia`), pop on device connect (`showOnDeviceConnect`, D-013), pop on track/play-state change (`showOnMediaChange`, off, D-032), cycle-output shortcut (`cycleOutputHotkey`, canonical "Win+Alt+A", "" = off, a typo → default; D-020), device shortcuts (`deviceHotkeys`: id, flow, last-known name, chord; bad or already-used chord → "", D-022), wheel volume (`wheelVolumeArea` Taskbar/Apps/Tray/Off, `wheelVolumeModifier` None/Ctrl/Shift/Alt, `wheelVolumeStep` 1–10 %, D-036), wheel brightness (`wheelBrightnessArea` default Off, `wheelBrightnessModifier` default Ctrl, `wheelBrightnessStep` 1–10 %, default 5; a pair that shares a turn with the volume wheel makes `Normalize` set brightness to Off, D-040); all added without a version bump: missing → constructor default, X-003. Saves on every change (temp file + move); missing → defaults, unparsable → defaults + copy to `settings.invalid.json`; out-of-range values clamped (`Normalize`). A failed save keeps the change for the session, logs the exception and sets `SaveError` ("Couldn't write to <path>."); the settings window shows it in an InfoBar | `System.Text.Json` source-gen (AOT-safe), enums as names (`"TopRight"`), relaxed escaping so `+` stays readable |
| `OsdWindow` + `OverlayWindow` | Render `OsdState` (device event line, media cover/title/artist/play state, output name/level/mute, mic name/level/mute; row glyph by device kind, D-017; or, for `OsdReason.Brightness`, one brightness row instead of all of those, D-040), fade/slide in and out (`OsdAnimation`, D-012), content changes fade through (`FadeThrough`, D-034), the volume and brightness % roll (`NumberRoll`, D-037, D-040), auto-hide after the configured time, at the configured position in the primary work area (`OsdPlacement`, 24 DIP margin) (D-008) | WinUI 3 `Window`, `OverlappedPresenter.SetBorderAndTitleBar(true,false)`, `DesktopAcrylicController`, `SetWindowPos(SWP_NOACTIVATE)` |
| `SettingsWindow` | Fluent settings UI, created on open, destroyed on close; one at a time (D-010). Pages Pop-up, Mouse wheel (Volume and Brightness sections: where, key to hold, step; the Brightness one starts with a read-only Screen card naming the supported screen, `IBrightnessService.Refresh` on open; a choice that would share a turn with the other action snaps back with a note on that card only; D-036, D-040), Shortcuts (cycle card, device rows: picker from `Endpoints` through `DeviceChoices` plus the row's own device as "Not connected", `HotkeyRecorder`, remove; a chord in use is refused with a note, D-022), About (mark, `AppVersion`, Copy details → clipboard: version, Windows build, .NET + Windows App SDK versions, exe path; app and data folders, open log, report a problem, source code). Icon: `AppWindow.SetIcon(app.ico)` + `AppTitleBar.IconSource` set from code (X-006). The Windows App SDK version comes from an `AssemblyMetadata` stamped from `Directory.Packages.props` (`ReleaseInfo` throws self-contained). `AppSettings` compares `deviceHotkeys` by value (custom `Equals`) | WinUI 3 `NavigationView` + `TitleBar`, Mica, CommunityToolkit.Mvvm partial `[ObservableProperty]` |
| `TrayIcon` | Notification-area icon + menu: Output / Input submenus (`DeviceChoices`, default checked; pick → `SetDefault`, D-019), Settings (default, bold), Exit (D-009) | `Shell_NotifyIcon` v4, `TrackPopupMenuEx`, uxtheme #135/#136 for the dark menu, `TaskbarCreated` |

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
- `RawInputKeySource` runs its own thread + message-only window; `KeyPressed` is raised there. So does
  `GlobalHotkey` (`Pressed`); App cycles / switches / connects right there, off the UI thread, since `Endpoints`
  and `Connect` wait (≤ 2 s) on the audio worker. `TaskbarWheelSource` has its own MTA thread (UIA client) and
  message-only window: every mouse move's `WM_INPUT` lands there, never on the key source's thread. `DeviceConnector` switches from the worker (`Changed`) and
  raises `TimedOut` on a timer thread. The settings window reads `Endpoints` on the UI thread, only on open and on "Add shortcut".
- `TrayIcon` runs its own thread + hidden **top-level** window (a message-only window misses the
  `TaskbarCreated` and `WM_SETTINGCHANGE` broadcasts); `ExitRequested` is raised there. Building the menu
  calls `IAudioService.Endpoints`, which waits (≤ 2 s) on the Core Audio worker — run inline when called from it.
- `BrightnessService` owns one MTA worker ("Brightness") that makes every WMI call. `Adjust` (the wheel thread) only
  adds to an `Interlocked` counter and queues one `Drain`; `Changed` and `ScreenChanged` are raised on the worker, so
  App and `SettingsViewModel` marshal with `DispatcherQueue.TryEnqueue`. A WMI call that never returns can't hold the
  exit: the thread is a background one, `Dispose` waits ≤ 3 s, and App disposes it after the flyout restore (D-006, D-041).
- GSMTC events and `GsmtcMediaService.Changed` arrive on thread-pool (MTA) threads; reads happen in
  its single refresh loop.
- `OsdController.ShowRequested` / `RefreshRequested` therefore arrive on any of those threads: App
  marshals with `DispatcherQueue.TryEnqueue`. So do the tray's `SettingsRequested` / `ExitRequested`.
- `SettingsStore.Update` is only called from the settings window (UI thread), so `Changed` arrives
  there: App applies it to `OsdWindow` and shows a `SettingsPreview` OSD so the change is visible.
- WinEvent hooks need a thread with a message loop: `FlyoutSuppressor` is created on the UI thread.
- `Process.Exited` (launcher watch) fires on a pool thread: marshal to the UI thread before touching
  the suppressor.
- `OsdState` is an immutable record; services publish a new instance, the window diffs nothing — it
  just rebinds.

## Audio and keys (verified on build 26300, 2026-10-22)

- One volume key press: raw input key-down first, then ~5 ms later **two** endpoint volume
  notifications with slightly different floats → `OsdRules` compares the displayed percent.
- `SendInput` volume and media keys reach raw input (hDevice 0) and Windows alike (a media key also
  reaches the player) → `scripts/press-key.ps1` exercises the keyboard path. It can't produce
  Consumer Control HID reports, so that path can only be checked with a physical media key/knob (log
  line `consumer-control key …`): **not verified yet**, neither the volume nor the media usages
  (2026-10-22). The dev machine has 9 consumer-control collections. A press seen on both paths raises twice; `OsdController` drops an
  identical request within 100 ms.
- Default-device switch attaches the device id the notification carries (re-querying the default
  could still return the old one).
- Default-device switch: `OnDefaultDeviceChanged` fires per role; we act on eConsole only and re-attach
  the volume callback; a late notification from the old endpoint is dropped by endpoint id.
- Device connect/disconnect (D-013): `OnDeviceStateChanged` / `OnDeviceAdded` / `OnDeviceRemoved` only
  make the worker re-read that device's state against the active set. Enabling/disabling an endpoint
  (Sound settings, `scripts/audio-device.ps1 -Enable/-Disable`) is a real state change: one
  `audio device connected/disconnected` log line each (Stereo Mix, 2026-10-22).
- Bluetooth headset (Technics EAH-AZ80, user's log 2026-10-22): two endpoints, "Headphones (…)" output
  and "Headset (…)" hands-free input. Connect: headphones, default switch ~40 ms later, headset ~35–80 ms
  after that. Disconnect: headphones, headset 0.2–0.9 s later, also when the default switches at once
  (850 ms, 2026-10-23). Hence the repeat drop (D-014, window D-016). On this Intel
  Smart Sound machine offloaded BT endpoints report enumerator INTELAUDIO; see D-014 for the BT test.
  Without a default switch (23:27): headphones + headset 80 ms apart → one burst, one show. USB bursts
  not observed yet.
- Bluetooth connect (D-023, 2026-10-23): offloaded endpoints' KS filter is `intcbttoporender_…` /
  `intcbttopocapture_…` on the INTELAUDIO adapter; the old non-offloaded endpoint of the same device
  (bthenum filter) lives on as not present. All share the device's `PKEY_Device_ContainerId`; built-in
  devices share `00000000-0000-0000-ffff-ffffffffffff`. One-shot reconnect/disconnect returns at once; only
  the hands-free filter acts (X-005). Disconnected endpoints flip between unplugged (4) and not present (8),
  and stay not present after a Windows "Disconnect". The Technics reconnect on their own ~20 s after a
  probe disconnect, not after a Windows one. Some old pairings' filters are gone (0x80070002/3, thrown as
  .NET IO exceptions, not `COMException`).
- Raw input registration is per process and per device class. WinUI doesn't register keyboard raw
  input: with the settings window focused, typing in its NumberBox works and volume keys still reach
  `RawInputKeySource` (2026-10-22). Re-check if anything else registers 0x01/0x06 in this process.
- Log: `%LOCALAPPDATA%\DazeMediaOsd\logs\app.log`, both processes (pid per line). Today only: older days
  are `app-yyyy-MM-dd.log`, 7 days kept (D-028). Every show is a
  `show <reason>: out=… in=… media=…` line; every media state change a `media: …` line.
  Read it with PowerShell: the Bash tool's sandbox served a stale copy (2026-10-23). `launcher: app
  exited with -1` is a kill (install script, Task Manager); a crash is an NTSTATUS like -1073741189.
  An exception escaping a XAML event handler is logged first (`ERROR unhandled exception: …` + stack),
  then XAML fail-fasts; the minidump in `%LOCALAPPDATA%\CrashDumps` has no managed heap.

## Media sessions (verified on build 26300, 2026-10-22; D-011)

- A Chromium page using `navigator.mediaSession` (title, artist, artwork) is a GSMTC session under
  its host app's AUMID — the desktop app's browser pane (`Claude_…!Claude`) works, so a local test page
  with a quiet `<audio>` gives real playback with metadata and cover without touching user media.
- A media key's raw input arrives before the player reacts; the session change follows ~20–50 ms
  later (Chromium) and refreshes the visible OSD.
- `TryGetMediaPropertiesAsync` intermittently throws `RPC_E_WRONG_THREAD` (0x8001010E) on the second
  read of a burst (every play/pause/next with Chromium; Telegram Desktop too, 2026-10-23), with no
  event after it; a retry 200 ms later succeeds. Hence retry, never "no session" on a failed read.
- Artwork arrives as encoded PNG/JPEG bytes; the window decodes them with `BitmapImage.SetSourceAsync`
  only when the array instance changes (the service reuses it for identical bytes).

## OSD window (verified on build 26300, 2026-10-22; D-008)

- Created once at startup (before suppression; failure → exit, Windows untouched), hidden between shows.
  Never `Activate()`d: `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE|SWP_SHOWWINDOW)` renders XAML fine.
  Topmost only sticks with `OverlappedPresenter.IsAlwaysOnTop = true`: the presenter strips
  `WS_EX_TOPMOST` and rewrites `HWND_TOPMOST` otherwise (D-018); something outside can still drop it, so
  a show from hidden puts it back and logs what was in front (D-038).
- Ex styles `NOACTIVATE|TOOLWINDOW|TOPMOST|TRANSPARENT|LAYERED` + `SetLayeredWindowAttributes(alpha 255)`:
  the acrylic backdrop and XAML still draw on the layered window; `WindowFromPoint` over it returns the
  window below (click-through); foreground window unchanged by a show.
- Frame: border without title bar gives DWM rounded corners + 1 px outline + shadow for free.
- Size: `Root` sets the **client** size × `GetDpiForWindow`/96: 340 × 100 DIP, 60 without mic, +52 with
  the media row (40-DIP cover + spacing; shown while a session exists and Media is on), +53 with the
  device event line (28 row + 12 + 1 hairline + 12; D-014), taller
  when Windows text size is above 100 %. The border frame is added on top (`OverlayWindow.FrameSize`,
  24 × 12 px at 175 %); sizing the window rect to `Root` clipped its right/bottom edge.
- Layout: 4-px grid, padding 16, rows min 28 high with shared min columns 20 / * / 44 so names, bar and
  values align; text honours the Windows text-size setting (rows/columns grow, never clip). Level bar
  is our own track + fill (X-002), set without animation; nothing drawn at 0 %.
- `--app` with the window: ~176 MB working set / 86 MB private (launcher 36 / 9 MB); after opening the
  settings window once ~239 / 122 MB; with GSMTC + a session + settings opened once ~221 / 115 MB
  (2026-10-22). Watch against D-001.
- Cover (D-033): decoded at 40 logical px by height; the old one stays until the new one is decoded, then
  the new one fades in (250 ms) on a second `Image` on top, which then hands it down. Only while already
  visible and with "Animation effects" on; a fresh show sets it directly.
- Other content that changes while visible fades through (`FadeThrough`, D-034: out 100 ms, swap, in
  150 ms): media title/artist, play state, output/input name, input icon, output icon on mute or device
  kind, "Muted" ↔ mic level. Instant: volume %, level bar, the output icon's level steps, a row appearing.
- `Refresh` (media change) re-renders and re-places only while visible and never restarts the hide
  timer; the media row sits on top, cover in place of the icon column, play-state glyph in the value column.
- Brightness (D-040): when `OsdState.Brightness` is set the media, output and input rows collapse and `BrightnessRow`
  shows on the same column grid: sun glyph (`E706`) / the screen's name over a 4-px bar / the % (`NumberRoll`, rolling
  only while a brightness OSD is already visible). `Refresh` (a media change) leaves it alone; any other show brings the
  device rows back. Seen 2026-10-29 in the user's theme and text size only.
- Device event line (D-014): topmost, same columns — glyph / name (BodyStrong, trimmed) / status
  ("Connected" accent, "Disconnected" secondary) — then a hairline. Set by `Show` from `OsdState.Device`
  (null clears it); `Refresh` leaves it. Entrance: XAML Storyboard (opacity, glyph scale), skipped when
  "Animation effects" is off. Seen 2026-10-22 in dark theme at 175 %: connect and disconnect, no
  clipping, foreground unchanged. Not seen: light theme, larger text size, with the mic or media row.
- Animation (D-012): layered alpha 0→255 + 16-DIP slide from the nearest edge (bottom positions rise,
  top ones drop), 200 ms in / 150 ms out, stepped per frame; `SW_HIDE` once fully faded. A show during
  the fade-out reverses it; a show or `Refresh` while visible re-places at the current frame without
  replaying. Measured 2026-10-22: content visible from the first frame after the key, settled ~180 ms.
  The first show after app start puts one frame darker than the background (the window's first
  composition) — not compared with the unanimated build.
- Theme: follows a live system light/dark switch (acrylic + ThemeResource brushes); seen 2026-10-24
  switching 4× from Settings, no crash (X-007).
- Not verified yet: multi-monitor/DPI change, show latency, "Animation effects" off (needs
  changing the user's setting).

## Brightness (verified on build 26300, 2026-10-29; D-039, D-040, D-041)

- Hardware seen: one built-in OLED panel (`SDC422A`, EDID model `ATNA40HQ05-0`, 2880×1800) on an ASUS laptop;
  `WmiMonitorBrightness` and `WmiMonitorBrightnessMethods` Active; `Level[]` lists every percent 0–100 (101 levels).
  No external monitor, so DDC/CI is untested.
- WMI: `ConnectServer("ROOT\WMI")` through CsWin32's COM interfaces, on an MTA thread. The proxy blanket (call level,
  impersonate) goes on the interface pointer, the `IUnknown` pointer and every enumerator, or calls fail with
  0x80041003 (X-011). Queries are forward-only, `Next` times out after 5 s, ≤ 256 rows. The model name comes from
  `WmiMonitorID.UserFriendlyName`, read once per connection ("Built-in screen" when unreadable).
  `WmiSetBrightness(Timeout=0, Brightness)`: Timeout 0 = until changed, not a revert timer.
- Timing, warm: read 3–5 ms, write 4–9 ms; a read right after a write can show the old level for up to ~20 ms, hence
  the settle poll (D-041).
- Flow: notch → `WheelBrightness` → `Adjust` → worker: read, plan, write, settle → `Changed` → App shows
  `OsdReason.Brightness`. The first step after start logs the screen once (`brightness: built-in screen "…"`), then
  one `brightness: 76 -> 81, <name>` line per burst; failures log once per streak.
- A WMI-driven change shows no native flyout (two full-screen captures ~200 ms after a step: only our OSD).
- Seen on the user's PC (left-docked vertical taskbar, one screen): Ctrl+wheel over the apps → brightness only, plain
  wheel over the apps → volume only, plain wheel over the tray with brightness set to the tray → brightness only; a
  3-notch burst moves ±15 (every notch applied); a 16-notch burst down stops at exactly 5 %; no startup probe; both
  conflict refusals on the settings page and the accepted non-overlapping pair (volume Apps, brightness Tray); no
  ERROR/WARN in `app.log`.
- Not verified: lid closed or docked (expected: no active panel, so a step finds no screen, shows nothing and logs
  `no built-in screen found`); whether a level set through WMI survives sleep or a power-plan change; Ctrl+wheel on a
  secondary or auto-hide taskbar (Q6); a physical or high-resolution wheel; external monitors; the brightness keys (Q4).

## Tray icon (verified on build 26300, 2026-10-22; D-009)

- Win11 puts a new icon in the overflow flyout; the user pins it. `Shell_NotifyIconGetRect` then returns
  the chevron's rect until the overflow is open.
- Icon by `uID` + hwnd, not a GUID (a GUID binds to the exe path and breaks across Debug/publish folders).
- Left-click and keyboard select (Enter/Space) open Settings; right-click / Shift+F10 open the menu.
  `SetForegroundWindow` before `TrackPopupMenuEx` is required for the menu to close on an outside click.
- Opening settings (new or already open) calls `WindowActivation.BringToFront` (`SetForegroundWindow`):
  `Window.Activate()` alone left it behind other apps, and after a left-click the overflow flyout kept
  focus. The shell lets the icon's process take focus after a click or menu pick.
- Theme: icon + menu follow `SystemUsesLightTheme` (taskbar), re-read on `WM_SETTINGCHANGE`
  "ImmersiveColorSet". Seen: dark taskbar → white glyph, dark rounded menu. Not verified yet: light
  taskbar swap, re-add after explorer restart (`TaskbarCreated`), runtime DPI change (icon size is
  loaded at system DPI).

## Process lifecycle (D-006)

- Launcher mode (no args): start self with `--app`, wait (log `launcher: started app <pid>, version
  0.1.0+abc1234, <exe path>`). On child exit (clean, crash, killed): restore
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
- Clean exit: tray Exit → `Application.Exit()` on the UI thread → `Program`'s `finally` disposes App and
  restores → `--app` exits 0 → launcher restores again (harmless) and exits.
- Verified 2026-10-22 on 26300: running → no native flyout; kill `--app` → launcher restores, exits;
  kill launcher → app restores, hides per show; tray Exit → both exit, native flyout stock again.
- Autostart (D-015): `Autostart` reads/writes `HKCU\…\Run\DazeMediaOsd` + its `StartupApproved\Run`
  flag (Explorer writes 12 bytes, first byte 02/06 on, 03/07 off; no value = on). Installed copy:
  `%LOCALAPPDATA%\DazeMediaOsdApp\current` via Setup.exe or `scripts/install.ps1` (D-029). Verified 2026-10-22: toggle on/off
  writes/removes the value, a Task Manager "disabled" shows off + note and "on" clears it. Not verified
  yet: an actual sign-in start.
- Installer (D-029): Velopack Setup.exe installs to `%LOCALAPPDATA%\DazeMediaOsdApp\current\` (per user,
  Start menu shortcut, no autostart). Setup, update and uninstall start the exe with `--veloapp-*`;
  `VelopackApp.Run()` at the top of `Program.Main` handles it and exits. Velopack force-kills every process
  in the install folder first (launcher + app together → host left minimized), so each hook calls
  `FlyoutHost.RestoreAll()`; uninstall also drops the Run value if it points into the install folder.
  Updates run the old version's before-update hook first: it sets `ExitSignal`, the app exits like tray
  Exit (launcher restores), the hook waits for both mutexes to go (≤ 10 s). Verified 2026-10-24 by running
  the installed exe with `--veloapp-obsolete`: clean exit 0, flyout restored, 0.8 s.
- Updates (D-030): `UpdateService` (Core/Updates, behind `IUpdateClient`; `VelopackUpdateClient` is the
  real one) checks 1 min after start, then daily; a find downloads at once; status → About's Updates card
  (raised on the checking thread, the window marshals). The launcher applies a downloaded update before
  starting the app. Verified 2026-10-24 with a local feed (`DAZEMEDIAOSD_UPDATE_SOURCE`): 0.3.0 found and
  downloaded 0.3.1 after 1 min; next launcher start applied it (before-update hook, after-update hook) and
  came back as 0.3.1, flyout minimized by the new app. Not seen: the About card on screen, Restart now,
  a GitHub-hosted feed.
  Verified 2026-10-24: install → run (host minimized) → uninstall → host restored, settings and the
  daily driver's Run value kept; `install.ps1` moved the old `Programs\DazeMediaOsd` copy (Run value
  re-pointed, folder deleted) and a second run reinstalled in place. Not verified: an update from one
  release to the next.

## Invariants

- **Fail-open:** whenever our app is not running, the native flyout works. It is minimized only while
  the app runs, and every exit path restores it (D-006). Audio and key sources start **before** any
  suppression; if either fails, the app logs it and exits with Windows untouched.
- OSD window never takes focus or activation (would steal focus from games/apps on every key press).
- No admin rights required.
- **Brightness safety (D-041):** nothing is written without a valid read of the screen in the same step; only a level
  the screen lists, never below 5 %, never against the step's direction; a failure drops the step, resets the
  connection and isn't retried; nothing touches WMI before a wheel step or the settings page asks.

## Open questions

- **Q4:** the same host very likely also shows the brightness-key / airplane-mode / other hardware-key
  flyouts, so minimizing it probably hides those too. Not tested: brightness keys aren't scriptable on
  the dev machine (a WMI-driven change, our wheel, shows no native flyout: seen 2026-10-29). If confirmed, ask the
  user: accept, or show those in our OSD (the brightness OSD exists now, D-040; an untried lead for the trigger:
  the `WmiMonitorBrightnessEvent` class in `ROOT\WMI`). UIA can't read the island content (X-001).
- **Q6 (Plan D, D-035):** tray vs tasks. Answered on 26300 (one monitor, vertical taskbar on the left, 2026-10-28,
  `spikes/TaskbarWheelProbe dump/auto`): cursor in a `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` rect → tasks, unless
  also in its child `TrayNotifyWnd` rect → tray. `TrayNotifyWnd` matched the drawn tray exactly (Show Hidden Icons
  → Show Desktop, 490 px); Widgets sits in the tasks part. `ReBarWindow32`/`MSTaskSwWClass` rects are stale — don't use
  them. `WindowFromPoint` also works when unlocked, but the rects are enough (X-009). Still open: a secondary taskbar, a horizontal one, auto-hide, and a
  fullscreen window over the taskbar (the rect test alone would still fire).
- **Q7 (Plan D):** answered on 26300 (2026-10-28, `auto`, 57 injected notches, no wheel mods running): the only native
  wheel action on the taskbar is the speaker icon (`SystemTray.OmniButtonCenter`, name "Volume …"): ±2 % per notch,
  with or without Ctrl/Shift. Nothing else reacts. The raw-input sink saw every notch. So over the speaker icon our
  action must stand aside (D-035); its rect comes from a cached UIA lookup, never on the wheel path.

