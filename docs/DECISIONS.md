# Decisions

Append-only log of choices that shape the code. Newest at the bottom. Format and rules: the
`knowledge-log` skill. A decision is changed by adding a new entry that supersedes it, never by editing
history (status line is the only thing you update on an old entry).

## D-001 · 2026-10-22 · Stack: C# / .NET 10 + WinUI 3 (Windows App SDK 2.x)
Status: accepted
- Decision: one C# app on .NET 10 (LTS to Nov 2028), WinUI 3 for both OSD and settings windows,
  CsWin32 for Win32/COM interop (Core Audio, WinEvent hooks), CsWinRT projections for WinRT
  (`Windows.Media.Control`). MVVM via CommunityToolkit.Mvvm.
- Why: WinUI 3 *is* the Win11 design system (Fluent controls, Mica/Acrylic, theme) → "modern config UI"
  for free and an OSD that matches the native look. C# is first-class for WinUI; Core Audio / GSMTC are
  well-trodden in C#. Prior art (ModernFlyouts, Flylet, EarTrumpet) is C#. Agents write C# + XAML well.
- Why not Rust: `windows-rs` is great for interop, but its WinUI layer (`windows-reactor`) was v0.0.0 in
  2026-05; egui/Slint/iced don't look native. Why not Tauri: web UI ≠ Win11 look, WebView2 per window
  is heavy for an OSD that must pop instantly. Why not C++/WinRT: same UI as C#, much slower to iterate.
  Why not WPF: no native Fluent/Mica without third-party theming. Why not Avalonia/Uno: cross-platform
  layer we don't need.
- Revisit if: WinUI resident memory/OSD show latency is unacceptable (then: OSD in raw Win32 +
  Direct2D/Composition, keep WinUI for settings only), or windows-reactor becomes stable and the user
  wants Rust.
- Ref: [research/2026-10-tech-stack.md](research/2026-10-tech-stack.md)

## D-002 · 2026-10-22 · Replace the native OSD, don't modify it
Status: accepted (Q1 spike confirmed suppression works on build 26300, see D-005)
- Decision: detect the native volume flyout (WinEvent hook on explorer.exe windows), hide it, show our
  own OSD. Same model as ModernFlyouts/Flylet.
- Why: there is no API to add content to the native flyout. Hiding it per-show is reversible and
  fail-open (our process dies → native flyout simply shows again).
- Why not DLL injection into explorer / Windhawk-style mod: fragile across Windows updates, AV/anti-cheat
  risk, can crash the shell. Why not "companion panel next to the native flyout": two flyouts look
  broken; kept only as a fallback mode if suppression breaks on some build.
- Revisit if: suppression stops working on a Windows update and can't be fixed quickly.

## D-003 · 2026-10-22 · Unpackaged, self-contained, single process, local build
Status: accepted; "single process" amended by D-006 (launcher process)
- Decision: `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, self-contained .NET publish to
  a folder, win-x64. One process hosts tray + OSD + settings window (settings window created on demand,
  destroyed on close). Settings as JSON in `%LOCALAPPDATA%\DazeMediaOsd\`. Autostart via HKCU Run key.
  Native AOT is a goal, not a v1 blocker.
- Why: no MSIX signing/cert hassle for a personal local build; nothing to install besides the folder.
  One process avoids IPC; OSD already loads WinUI, so a separate settings process saves nothing.
- Why not MSIX: needs signing and a cert in the trusted store just to run locally; revisit for v2
  distribution.
- Revisit if: v2 needs Store/winget distribution.

## D-004 · 2026-10-22 · Min OS: Windows 11 24H2 (build 26100)
Status: accepted
- Decision: TargetPlatformMinVersion 10.0.26100.0; test on the user's machine (build 26300 at time of
  writing).
- Why: personal app; native flyout window classes differ between Win11 releases, supporting only
  current builds keeps suppression code small.
- Revisit if: the app gets shared with users on older builds.

## D-005 · 2026-10-22 · Detect the flyout by WinEvent SHOW on the explorer host, hide per show
Status: superseded by D-006 (kept as the fallback when the launcher is gone)
- Decision: out-of-context `SetWinEventHook(EVENT_OBJECT_CREATE..)` filtered to the shell explorer pid;
  the flyout host is `XamlExplorerHostIslandWindow` with band `ZBID_ABOVELOCK_UX` (18); on every
  `EVENT_OBJECT_SHOW` of it call `ShowWindowAsync(SW_HIDE)` and show our OSD. Details in ARCHITECTURE.
- Why: SHOW fires on every press within ~10 ms, even when already shown/hidden by us, so it is a
  complete trigger; explorer tolerates the hide and stays stock once we exit (fail-open). Verified with
  `spikes/FlyoutProbe` on 26300.
- Why not low-level keyboard hook as trigger: adds latency to all input, anti-cheat noise, and misses
  volume changes Windows shows without a key. Why not Flylet's "permanent hide" (`SW_MINIMIZE` +
  `SW_FORCEMINIMIZE` on the host): state outlives our process → violates fail-open.
- Revisit if: a Windows update changes class/band (re-run the probe), or Q4/Q5 show SHOW-time hiding
  is not good enough.

## D-006 · 2026-10-22 · Minimize the native flyout while running; a launcher restores it on any exit
Status: accepted
Supersedes: D-005
- Decision: the app minimizes the flyout host for its whole lifetime (child `SW_MINIMIZE`, host
  `SW_MINIMIZE` + `SW_FORCEMINIMIZE`) and restores it on exit. The same exe started without args is a
  launcher: it starts `--app`, waits, and restores the host when the app exits for any reason ("finally"
  in a separate process). The app restores and falls back to D-005 if the launcher dies. Our OSD's
  trigger is raw input of the volume keys plus audio callbacks, no longer the host's SHOW.
- Why: the user saw the native flyout flash now and then under per-show hiding and wants only ours.
  Minimized, it never draws. An in-process `finally`/`ProcessExit` doesn't run on a hard kill or
  native crash, so the restore has to live in another process. The user proposed the wrapper shape.
- Why not per-show `SW_HIDE` (D-005): visible flash. Why not minimize with restore only on clean exit:
  a crash leaves the user without any volume OSD. Why not a separate launcher project: one exe is
  simpler to build, publish and locate; only worth it if launcher-mode memory is heavy (Q5). Why not
  SHOW as the trigger: a minimized host raises it only sometimes.
- Revisit if: launcher-mode working set is heavy (→ tiny AOT launcher exe), or Q4 confirms brightness
  etc. disappear and the user won't accept that.
- Ref: probe modes `minimize`/`restore` in `spikes/FlyoutProbe`.

## D-007 · 2026-10-22 · When our OSD pops up
Status: superseded by D-011 (adds media keys; the audio rules stand)
- Decision: show on a volume key (raw input), an output volume (displayed % changed) or mute change from
  any source, a default output/input device switch, and a mic mute change. Never on mic level alone or
  a device rename. Rules live in `OsdRules`.
- Why: with the native flyout minimized, ours must cover what it did (keys, incl. at 0 %/100 %) plus
  the user's ask (device changes). Volume changes can't be attributed to a key reliably, so any source
  counts; mic mute matters because the OSD shows it.
- Why not keys only: misses hardware knobs/headset buttons that don't arrive as keyboard input and
  device switches. Why not mic level: auto-gain in call apps moves it constantly → OSD spam.
- Revisit if: daily use shows popups from apps changing master volume, or Quick Settings slider drags
  feel noisy (then: ignore changes while Quick Settings is open, or use the event context GUID).

## D-008 · 2026-10-22 · OSD window: plain WinUI Window, native frame, always-active acrylic
Status: accepted
- Decision: `OsdWindow` is a stock WinUI 3 `Window` with `SetBorderAndTitleBar(true, false)`, a custom
  `SystemBackdrop` driving `DesktopAcrylicController` with `IsInputActive = true`, and Win32 ex styles
  from `Core.Windowing.OverlayWindow` (layered + transparent for click-through, no-activate, tool, topmost).
  Shown/moved only via `SetWindowPos(SWP_NOACTIVATE)`; `Activate()` is never called. Answers Q2.
- Why: native DWM frame = Win11 rounded corners, outline and shadow with zero custom drawing; acrylic
  matches the native flyout; no extra dependency. Verified visually and by hit-test on 26300.
- Why not WinUIEx: a package for ~4 window calls we already make via CsWin32. Why not borderless +
  own rounded clip: loses the system outline/shadow. Why not stock `DesktopAcrylicBackdrop`: by design
  falls back to a solid colour while the window is inactive, and ours never activates (not tried).
  Why not `AppWindow.Show(false)` / `IsAlwaysOnTop`: SetWindowPos does move+size+show+topmost in one call.
- Revisit if: a WinAppSDK update breaks drawing on layered windows (drop LAYERED/TRANSPARENT, accept
  no click-through), or show latency/memory is a problem (D-001 revisit trigger).

## D-009 · 2026-10-22 · Tray icon: own `Shell_NotifyIcon` via CsWin32, native popup menu
Status: accepted
- Decision: `Core.Tray.TrayIcon` calls `Shell_NotifyIcon` (NOTIFYICON_VERSION_4) from a hidden top-level
  window on its own thread; the menu is `TrackPopupMenuEx`, dark/light via uxtheme ordinals 135/136
  (`SetPreferredAppMode`, `FlushMenuThemes`) following the taskbar theme. Monochrome glyph icons, one per
  taskbar theme (`scripts/make-icon.ps1`). Answers Q3.
- Why: AOT-safe by construction (CsWin32 source-gen), no new dependency, keeps interop in Core like the
  rest; the menu is a few items. The native menu renders Win11-style (rounded, dark) on 26300.
- Why not H.NotifyIcon.WinUI: a package (+ its WinUI flyout window machinery) for ~6 Win32 calls, the
  same trade as WinUIEx in D-008; its AOT/trim status wasn't verified. Why not a WinUI `MenuFlyout`
  menu: needs an extra activatable window just to host it.
- Revisit if: the menu needs rich content (toggles, sliders) — then host a WinUI flyout window.

## D-010 · 2026-10-22 · Settings window: own card styles, save on every change
Status: accepted
- Decision: `SettingsWindow` is a WinUI `Window` (Mica, `TitleBar`, `NavigationView` swapping page
  panels) with Windows-Settings-like cards from a local `Grid` style; `SettingsViewModel` (CommunityToolkit.Mvvm
  partial properties) writes every edit straight to `SettingsStore`, which saves and raises `Changed`;
  the OSD previews the result. Left-click on the tray icon opens it.
- Why: no extra package beyond D-001's MVVM toolkit; partial properties are the AOT-safe form for WinRT.
  Instant apply is how Windows Settings behaves and makes the preview meaningful.
- Why not CommunityToolkit.WinUI `SettingsCard`: a second toolkit package for three cards, WinAppSDK 2.x
  support not checked. Why not `Frame` + pages: two pages, nothing to navigate back to. Why not an
  Apply/OK button: extra state and unlike native settings.
- Revisit if: pages grow past ~4 or need deep links (→ `Frame`), or cards need expanders (→ `SettingsCard`).

## D-011 · 2026-10-22 · Media keys pop the OSD; media session changes only refresh it
Status: superseded by D-013 (adds device connect/disconnect; the key and media rules stand)
Supersedes: D-007
- Decision: D-007's audio rules, plus Play/Pause, Next and Previous (keyboard `VK_MEDIA_*`, Consumer
  Control 0xCD/0xB0/0xB1/0xB5/0xB6) pop the OSD — only while a GSMTC session exists and the Media
  setting is on. A session change (track, play state) never pops it; it re-renders a visible OSD
  without extending its time (`OsdController.RefreshRequested`).
- Why: the user decided media keys show the OSD (2026-10-22). The key arrives before the player
  reacts, so the shown state is stale until GSMTC reports the new track; refresh fixes that.
  No session / Media off → nothing to show, like the native flyout.
- Why not pop on every session change: players change tracks on their own → OSD every few minutes.
  Why not wait for the session change before showing: adds latency (20–50 ms seen with Chromium,
  player-dependent) and shows nothing if the player ignores the key.
- Revisit if: the user wants track changes announced, or refresh flicker shows in daily use.

## D-012 · 2026-10-22 · OSD animation: layered-window alpha + window slide, per frame
Status: accepted
- Decision: `OsdWindow` fades via `SetLayeredWindowAttributes` alpha and slides 16 DIP from the nearest
  screen edge via `SetWindowPos`, stepped on `CompositionTarget.Rendering` while running. Progress is
  `Core.Osd.OsdAnimation` (200 ms ease-out in, 150 ms ease-in out; reverses from the current value;
  show while shown = no-op). Shows start one frame in. Windows "Animation effects" off
  (`SPI_GETCLIENTAREAANIMATION`, read per show/hide) → instant, as before.
- Why: alpha and position apply to the whole window — DWM frame, shadow and acrylic included — and the
  window is already layered (D-008). Verified on 26300 by screen capture: brightness ramps, the frame
  moves, a re-press while visible changes nothing.
- Why not XAML/Composition animations on `Root`: they leave the frame, shadow and acrylic static (not
  tried). Why not `DispatcherQueueTimer`: ~15 ms granularity, not frame-synced.
- Revisit if: the per-frame `SetWindowPos` stutters under load, or a WinAppSDK update stops honouring
  layered alpha (then: fade content only, or drop the animation).

## D-013 · 2026-10-22 · Any audio device connect/disconnect pops the OSD, once per burst
Status: accepted
Supersedes: D-011 (adds device connect/disconnect; its key and media rules stand)
- Decision: an endpoint of either flow becoming active or inactive pops the OSD, default or not
  (`AudioChangeKind.DeviceConnected/Disconnected`). `CoreAudioService` keeps the set of active endpoint
  ids and raises only on a real transition; added/removed/state-change notifications all just re-read
  the device's state. `OsdController` shows 500 ms after the last such event (`DeviceSettle`), reading
  the state then; a default-device switch in that window shows at once and cancels the pending show.
  The OSD content is unchanged: current default output/input. Setting `showOnDeviceConnect`, on by
  default.
- Why: the user wants confirmation that a headset/USB device came or went (2026-10-22); before, only a
  default switch showed. A Bluetooth headset is 2+ endpoints and the default switch often follows the
  state change, so an immediate show would pop twice and could show the old default.
- Why not trust `OnDeviceStateChanged`'s new state alone: Windows repeats states and added/removed
  arrive separately. Why not widen the 100 ms identical-request window: the burst's states differ.
- Verified on 26300 with Stereo Mix enabled/disabled (`scripts/audio-device.ps1`): one show each,
  ~510 ms after the event. Not verified: a real Bluetooth/USB connect (burst timing).
- Revisit if: a Bluetooth connect shows twice or noticeably late in daily use (tune `DeviceSettle`), or
  apps that create virtual endpoints on the fly (e.g. voice chat) cause popups.
