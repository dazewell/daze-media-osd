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
Status: accepted; "single process" amended by D-006 (launcher process); "local build" amended by D-029 (Velopack release)
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
Status: superseded by D-018 (topmost needs `IsAlwaysOnTop`; the rest stands)
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
Status: accepted; tray glyph superseded by D-025
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
Status: superseded by D-013 (adds device connect/disconnect; the key and media rules stand); D-032 adds an opt-in pop on track/play-state changes
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
Status: superseded by D-014 (the OSD now names the device; the burst rule stands)
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

## D-014 · 2026-10-22 · Device connect/disconnect: one event line at the top of the OSD
Status: superseded by D-016 (repeat window widened; everything else stands)
Supersedes: D-013 (adds what the show says and drops repeated bursts; when it pops is unchanged)
- Decision: a device show carries `OsdState.Device` (`DeviceNotice`: name, flow, connected, Bluetooth,
  others), rendered as one header line above the media/device rows: Fluent glyph (Bluetooth E702, else
  speaker E7F5 / mic E720), the name, "Connected" in accent or "Disconnected" in secondary text, then a
  hairline. Only the show that announces it has the line; the next show clears it, a media `Refresh`
  keeps it (a disconnect often pauses the player). A default switch that cancels the pending show
  carries the notice. A burst's endpoints merge by product — the trailing "(…)" of the name — and
  Bluetooth or multi-endpoint products show just that ("WH-1000XM5"); a single wired endpoint keeps its
  full name ("Stereo Mix (Realtek(R) Audio)", whose "(…)" is the sound card). A burst that starts within
  `DeviceSettle` of announcing the same device doing the same thing is dropped. Line fades in, glyph
  scales 0.6→1, off with "Animation effects".
- Why: the user found the D-013 popup didn't say what happened and asked for the event "in a nice and
  fancy way without cluttering" (2026-10-22). One line in the existing 4-px grid reuses the row
  columns, so it reads as part of the OSD. The repeat drop: a real Technics EAH-AZ80 (log, 2026-10-22)
  popped twice per connect/disconnect — default switch 41 ms after the headphones endpoint, then the
  hands-free endpoint 33–930 ms later started a new burst.
- Bluetooth test: on this Intel Smart Sound machine offloaded endpoints (all hands-free, some A2DP) have
  `PKEY_Device_EnumeratorName` = INTELAUDIO; the BTHENUM instance id is in undocumented
  `{b3f8fa53-0004-438e-9003-51a46e139bfc}` pid 2 (native) or 39 (offloaded). Checked all three; a miss
  only costs the glyph.
- Verified on 26300: Stereo Mix enable/disable (screenshots), and the user's Technics EAH-AZ80 — both
  endpoints flagged Bluetooth (the hands-free one via pid 39), one "Technics EAH-AZ80" show per
  connect and per disconnect, "works well" (user, 2026-10-22). Not seen: a default-switch connect
  with the new line, a USB device.
- Why not a toast / separate card / per-endpoint rows: toasts go to the notification centre and can
  take focus; a card or rows double the OSD for one fact. Why not a different window or badge on the
  device row: the device that came may not be the default, so it has no row. Why not
  `PKEY_DeviceInterface_FriendlyName` for the product: same result as the "(…)" parse on every endpoint
  here, one more property read, and the parse is unit-testable.
- Revisit if: a device with an unhelpful "(…)" merges with another, the drop hides a real second
  device, or a Bluetooth endpoint shows the speaker glyph (log line lacks `bluetooth`).

## D-015 · 2026-10-22 · Autostart: the Run value is the only state; the copy that turns it on registers itself
Status: accepted; install folder amended by D-029 (`%LOCALAPPDATA%\DazeMediaOsdApp`)
- Decision: "Start with Windows" writes `HKCU\…\CurrentVersion\Run\DazeMediaOsd` = `"<this exe>"` (no
  args → launcher, D-006) and deletes our `Explorer\StartupApproved\Run` value; off deletes both. The
  toggle reads the registry each time the settings window opens: on only if the Run value exists and
  Task Manager hasn't disabled it (odd first byte in StartupApproved). Notes under the toggle say when
  Task Manager turned it off, or when the Run value starts another copy (Debug vs installed).
  `scripts/install.ps1` publishes to `%LOCALAPPDATA%\Programs\DazeMediaOsd` (outside any checkout).
- Why: a copy in `settings.json` drifts as soon as the user uses Task Manager or Settings → Startup
  apps (user, GOALS). Registering the running exe needs no install-path knowledge in the app; a fixed
  install folder keeps the entry valid across worktrees and rebuilds.
- Why not a Startup-folder shortcut / scheduled task: shortcut needs COM `IShellLink` for the same
  result; a task can't be disabled from Startup apps as plainly and may need elevation for some options.
  Why not leave StartupApproved alone on "on": the Run value would stay disabled and the toggle would
  snap back off.
- Revisit if: packaging moves to MSIX (then `StartupTask` in the manifest).

## D-016 · 2026-10-23 · Repeat drop measured against its own 2 s window, not `DeviceSettle`
Status: accepted
Supersedes: D-014 (only the repeat window; the event line and burst rules stand)
- Decision: a device burst is dropped as a repeat if it starts within `OsdController.RepeatWindow`
  (2 s) of announcing the same device doing the same thing. `DeviceSettle` (500 ms) still times the show.
- Why: daily use (log, 2026-10-23) — Technics EAH-AZ80 disconnect with a default switch 29 ms after the
  headphones announced at once; the hands-free endpoint followed 850 ms later, past 500 ms, and popped
  the same line again. D-014 had measured gaps up to 930 ms but only tested the settled path, where the
  announce itself comes 500 ms late.
- Why a wide window is free: only the same name *and* direction count, and only a one-way burst: one
  that went and came back (a flap merged into one burst) always shows (PR #14 review).
- Revisit if: a real second device with the same "(…)" product within 2 s is hidden.

## D-017 · 2026-10-23 · Device-type glyph from the endpoint form factor; speakers keep the level glyph
Status: accepted
- Decision: `CoreAudioService` reads `PKEY_AudioEndpoint_FormFactor` into `EndpointState.Kind` /
  `DeviceEvent.Kind` (`DeviceKinds.FromFormFactor`: 1 Speakers, 3 Headphones, 4 Microphone, 5–6 Headset,
  9 Display, else Other). Output row: Mute wins; Headphones E7F6 / Headset E95B / Display E7F4 replace
  the volume-level glyph; Speakers and Other keep Volume0–3. Input row: MicOff wins, Headset E95B, else
  Microphone. Event line (D-014): Bluetooth still wins, then the kind's glyph, then speaker/mic.
- Why: the user wants to know which device is active at a glance (GOALS, picked from the PR #8 survey,
  2026-10-23); "Speakers" and a monitor looked the same. The level glyph is itself a speaker, so for
  speakers it is the type glyph with more information; the number and bar already carry the level.
- Why not a second glyph next to the name: one more column in a 4-px grid for the same fact. Why not
  `PKEY_Device_IconPath`: driver-chosen .dll icons, not Fluent glyphs, and they don't theme.
- Read on 26300: laptop speakers 1, jack/Bluetooth headphones 3, mic array 4, Bluetooth hands-free 5
  (both flows), Dell monitor 9, Stereo Mix 10. A failed read is Other (generic glyph).
- Revisit if: a device reports a misleading form factor (e.g. USB headset as Speakers) often enough.

## D-018 · 2026-10-23 · OSD topmost through `OverlappedPresenter.IsAlwaysOnTop`, plus SetWindowPos
Status: accepted
Supersedes: D-008 (only "why not `IsAlwaysOnTop`"; the window design stands)
- Decision: `OsdWindow` sets `presenter.IsAlwaysOnTop = true`; `ShowAt` keeps `SetWindowPos(HWND_TOPMOST)`.
- Why: the user saw no OSD while changing volume over a maximized, focused window (2026-10-23). The
  presenter owns z-order: it drops the `WS_EX_TOPMOST` that `OverlayWindow.Apply` sets and turns every
  `HWND_TOPMOST` request into a non-topmost one (`SetWindowPos` returns true, flag stays off, also from
  another process). So the OSD was never topmost; it only came up above inactive windows, and a
  focused maximized window stayed above it. With `IsAlwaysOnTop` it is topmost from the start.
- Verified on 26300, WinAppSDK 2.5.1: topmost before the first show, above the maximized Claude window
  in a screen capture, foreground unchanged, `WindowFromPoint` still hits the window below.
- Revisit if: a WinAppSDK update changes presenter z-order handling, or a topmost app (e.g. a video
  player in always-on-top) still covers the OSD — then the native flyout's band is the only way up.

## D-019 · 2026-10-23 · Set the default endpoint through undocumented `IPolicyConfig`
Status: accepted
- Decision: `CoreAudioService.SetDefault` calls `IPolicyConfig.SetDefaultEndpoint` (client CLSID
  `870AF99C-…`, IID `F8679F50-…`, slot 11) on its MTA worker for eConsole + eMultimedia; eCommunications
  stays. A failed HRESULT only logs; success comes back as the usual default-device notification, so
  the OSD shows it like any other switch. Tray offers `DeviceChoices.For` (sorted by name; a Bluetooth
  hands-free "Headset (X Hands-Free)" output is hidden next to "Headphones (X)" unless it is the default).
- Why: quick switching is a picked feature (GOALS, 2026-10-23) and Windows has no documented setter:
  `Windows.Media.Devices.MediaDevice` only reads, `AudioRoutingManager` is phone-only. No admin, no
  injection; EarTrumpet and SoundSwitch ship the same interface. Console + multimedia is what Sound
  settings and the quick-settings picker change; communications is the call device users set apart.
- Verified on 26300: tray Input → Stereo Mix set roles 0 and 1, role 2 unchanged, OSD showed the new
  mic 13 ms later; switching back the same way restored it.
- Revisit if: a Windows update moves the vtable (then the HRESULT logs and nothing switches — check
  `scripts/audio-device.ps1` first) or a documented API appears.

## D-020 · 2026-10-23 · Cycle-output hotkey through `RegisterHotKey`, default Win+Alt+A, typed in Settings
Status: accepted; the typed box superseded by D-021, one chord per `GlobalHotkey` by D-022
- Decision: `GlobalHotkey` registers one chord (`MOD_NOREPEAT`) on its own message-only window. A press
  switches to `DeviceCycle.Next` (the `DeviceChoices` list by name, wrapping around). With no other
  output, the OSD shows the current one (`OsdReason.Hotkey`), so the press always gets an answer.
  Default Win+Alt+A (user, 2026-10-23); `cycleOutputHotkey` "" is off. Settings has a text box committed
  on Enter or focus loss: invalid text gets a note and isn't saved, and a chord Windows refuses
  (1409, taken) shows its reason under the card.
- Why: `RegisterHotKey` needs no hook and no admin, adds no input latency, and fails cleanly when the
  shell or another app owns the chord. Win+Ctrl+V is Windows 11's own output picker (taken, 1409 on
  26300). Win+Alt+A was free on 26300.
- Why not a key-capture box: the shell eats many Win+ chords before XAML sees them, so capturing would
  miss exactly the ones people want. Typed text works for every chord.
- Verified on 26300: registered at start; Win+Alt+A with only the speakers active showed the OSD,
  foreground unchanged, still click-through. Win+Ctrl+V showed "already used"; "Alt+Banana" was refused
  and not saved; "ctrl+alt+f11" was stored as "Ctrl+Alt+F11" and registered; empty turned it off.
- Revisit if: users want more shortcuts (input cycle, show OSD): `GlobalHotkey` would then take ids.

## D-021 · 2026-10-23 · Settings records the shortcut from pressed keys; reset and turn-off inside the box
Status: accepted
Supersedes: D-020 (only the typed text box and its "why not a key-capture box")
- Decision: the box is read-only and records the chord pressed while it has focus: Win/Ctrl/Alt
  (+ Shift) + A–Z, 0–9 or F1–F24 (`Hotkey.TryCreate`). While keys are held it shows them ("Win+Alt+"),
  Backspace/Delete turns the shortcut off, Esc shows the stored one again, Tab leaves. Inside the box: a
  reset button (back to Win+Alt+A, shown only when different) and a turn-off ✕. While the box has focus,
  `GlobalHotkey.Pause` lets go of the registration, so pressing the current chord there records it
  instead of switching the output; refusals are still reported (register, then release).
- Why: the user (2026-10-23) could not set a shortcut after clearing it — typing produced letters, not a
  chord — and expects a reset inside the input, as elsewhere in Windows. D-020's objection doesn't hold:
  a chord the shell eats before XAML sees it is also one `RegisterHotKey` refuses, so capturing loses
  nothing that could have worked.
- Verified on 26300 (UIA focus + `keybd_event`): from off, Win+Alt+A recorded and saved; the same chord
  pressed in the box didn't switch the output; Ctrl+Alt+F11 recorded; Shift+A rejected with the note and
  not saved; Reset restored Win+Alt+A and registered it once focus left.

## D-022 · 2026-10-23 · A hotkey per device: one `GlobalHotkey` with an id per chord, rows on a Shortcuts page
Status: accepted; "not active → no switch" superseded for paired Bluetooth devices by D-023
Supersedes: D-020 (only "one chord": `GlobalHotkey` now takes ids, its revisit-if)
- Decision: `deviceHotkeys: [{ id, flow, name, hotkey }]` in `settings.json`; `name` is the last-known
  friendly name (refreshed when Settings opens), so an unplugged device still reads right. `Normalize`
  turns a bad chord into "" (row kept) and drops a chord the cycle shortcut or an earlier row already has.
  `Shortcuts.From` maps settings to ids (1 = cycle, 2 + row index); `GlobalHotkey.Set` registers all,
  `Errors` per id, `Pressed(id)`. A press calls `SetDefault` (eConsole + eMultimedia, D-019); already
  default → OSD (`OsdReason.Hotkey`); not active → no switch, OSD with the event line "<name> · Not
  connected" (`DeviceNotice.Absent`, shown even with "When a device connects" off; user, 2026-10-23).
  Settings: a Shortcuts page holds the cycle card and one row per device (picker through `DeviceChoices`,
  `HotkeyRecorder` extracted from D-021's box, remove), "Add shortcut" last. A chord already used is
  refused in place with a note; any focused recorder pauses every registration.
- Why: the user wants one press per device (GOALS, 2026-10-23). One window and one id space keep a single
  place for "one chord does one thing". The full name, not D-014's product, on the line: it is what the
  row shows and tells "Headphones (Technics…)" from "Headset (Technics…)".
- Why not one `GlobalHotkey` per chord: a thread and window each, and duplicates across instances can't
  be checked. Why not try `SetDefault` anyway when inactive: an unplugged endpoint can't play, and
  the press would get no answer. Why not a toast: D-014's reasons.
- Verified on 26300: Speakers ↔ Technics Headphones and Microphone Array ↔ Stereo Mix by chord (OSD,
  foreground unchanged, eCommunications untouched); again on Technics → OSD only; Stereo Mix disabled →
  "Not connected", no switch; Ctrl+Alt+F9 recorded in a second row and in the cycle box refused with
  the note; remove re-registered the rest; closing Settings with a box focused resumed the shortcuts.
- Revisit if: users want shortcuts for other actions (input cycle, mute mic): add kinds to `Shortcut`.

## D-023 · 2026-10-23 · A device shortcut connects a paired Bluetooth device through `KSPROPERTY_ONESHOT_RECONNECT`
Status: accepted
Supersedes: D-022 (only "not active → no switch", for devices that take the request)
- Decision: a press on an inactive device calls `IAudioService.Connect`: endpoint topology → connector →
  KS filter as `IMMDevice` → `IKsControl`, `KSPROPSETID_BtAudio` / `KSPROPERTY_ONESHOT_RECONNECT` (GET, no
  data) sent to the endpoint and every inactive sibling with the same `PKEY_Device_ContainerId` (not the
  built-in "local machine" container). Only disabled endpoints are refused. `DeviceConnector` then waits
  ≤ 15 s for that endpoint's `DeviceConnected` and calls `SetDefault`. OSD event line: "Connecting…", then
  the usual connect show; "Couldn't connect" on timeout; "Not connected" when no endpoint took the request.
- Why: the user wants the device's shortcut to bring it in (2026-10-23). Documented property (ksmedia.h),
  what Sound control panel's "Connect" sends; no admin, no injection; returns at once.
- Why the siblings: on Intel Smart Sound the offloaded A2DP endpoint accepts it and does nothing (X-005);
  the hands-free one connects the whole device. Why not only unplugged: after a Windows "Disconnect" the
  endpoints sit in not present, and still take the request.
- Why not `Windows.Devices.Bluetooth` or `BluetoothSetServiceState`: no "connect profile" API in the first;
  the second re-installs services and needs admin.
- Verified on 26300 with Technics EAH-AZ80: after a probe disconnect, Win+Ctrl+Alt+T → connected 2.9 s
  later, made default; after a Windows "Disconnect" (2+ min, no auto-reconnect) → 9 s. Old pairings
  (SOUNDPEATS H3, WF-C710N, AZ40M2) have no reachable filter (0x80070002/3) → "Not connected".
- Revisit if: a driver update stops the hands-free endpoint connecting (`spikes/BtProbe list` / `connect`),
  or a connect regularly takes > 15 s.

## D-024 · 2026-10-23 · Version: one `<Version>` in Directory.Build.props, SemVer 0.x, bumped by the PR
Status: accepted; releases: the tag must equal `<Version>` (D-029)
- Decision: `Directory.Build.props` holds `<Version>` (started at 0.1.0) with Product/Company/Copyright; the
  SDK appends `+<commit sha>` to the informational version. `Core.Lifecycle.AppVersion` parses it: About shows
  "0.1.0 (abc1234)", the launcher logs `version 0.1.0+abc1234` and the exe path. A `feat` PR bumps minor, a
  `fix` PR bumps patch, docs/chore don't; 1.0.0 when the user calls v1 done. `AssemblyTitle` "Daze Media
  OSD" is the exe's FileDescription: the name Task Manager and Startup apps show.
- Why: the user asked for versioning (2026-10-23); with Debug and installed copies side by side, the log
  and About must say which build runs. One number, no tooling; the commit pins the exact build.
- Why not Nerdbank.GitVersioning or MinVer: a package plus git-height or tag rules for a local-only build
  with squash merges and no tags or releases yet.
- Revisit if: v2 CI publishes releases — then derive the version from a release tag.

## D-025 · 2026-10-23 · Own mark for every icon: a flyout card with an output row and an input row
Status: accepted
Supersedes: D-009 (only the tray glyph: Segoe "Volume3" → our mark)
- Decision: `scripts/make-icon.ps1` draws one mark with GDI+: a rounded card with a speaker + level bar
  above a microphone + shorter bar, geometry on a 16-unit grid snapped per size. Colour tile (violet → blue,
  amber input level) for `app.ico` (exe `ApplicationIcon`, settings window via `AppWindow.SetIcon`) and
  `app.png` (About, title bar); the same mark outlined in white / black for the tray (`tray-dark/light.ico`).
- Why: the user wants a unique icon (2026-10-23). The old tray glyph was the Windows volume icon itself, so
  ours sat beside the system one looking identical. The mark is what the app adds: output and input.
- Why not an SVG + rasterizer: a new tool on the dev machine; GDI+ is already what the script used.
- Revisit if: the 16/20 px frames read badly on a real 100 %/125 % display (only seen in a 4× preview).

## D-026 · 2026-10-23 · Settings wording: plain Windows voice, "pop-up" for the OSD
Status: accepted
- Decision: every string in the settings window follows the Windows 11 Settings voice: sentence case,
  "you", contractions, short descriptions, no jargon ("default device", "register", "entry", "OSD").
  The OSD is "pop-up" everywhere the user reads (nav, page, descriptions); "Daze Media OSD" stays as the
  name. Notes say what happened and what to do next ("Try another one."); raw exception text goes to the
  log only, and the UI says "see the log". Position items are in screen order (`PositionOrder` maps to the
  stored `OsdPosition`), with the default marked. About has no "Built with" card: the versions that
  matter are in Copy details.
- Why: the user asked for simpler, nicer, less geeky text (2026-10-23); a UX-writer agent rewrote it.
  "Pop-up" rather than "volume pop-up": it also shows devices, media and connect messages.
- Why not keep "OSD" or "flyout": jargon a regular user doesn't know. Why the App folder card stays
  (the writer suggested dropping it): it's how the user tells a Debug copy from the installed one.
- Revisit if: the OSD itself or the tray menu gets user-facing text — use the same terms there.

## D-027 · 2026-10-23 · License: GPL-3.0-or-later, with a Windows App SDK exception
Status: accepted
- Decision: `LICENSE` is the verbatim GPL-3.0 text (from GitHub's license API); the project is
  GPL-3.0-or-later. README carries a section 7 additional permission to convey the program combined with
  the Windows App SDK. Copyright holder: dazewell (matches `<Copyright>` in Directory.Build.props).
- Why: the user chose, in a plain-language questionnaire (2026-10-23), that anyone handing out a changed
  version must publish its source. The exception exists because the self-contained publish bundles the
  Windows App SDK runtime, which is under Microsoft's proprietary terms, not a GPL "System Library".
  "Or later" is the FSF default and lets a future GPL apply without re-licensing.
- Why not MIT / Apache-2.0: let a closed fork ship without its source (what the user rejected). Why not
  MPL-2.0: only changed files stay open. Why not AGPL: no network service to cover. Other deps (CsWin32,
  CommunityToolkit.Mvvm: MIT; xunit: test-only) are GPL-3.0-compatible.
- Revisit if: outside contributions start (consider a DCO sign-off), or a dependency with a
  GPL-incompatible license is added.

## D-028 · 2026-10-23 · Log rotation: rename `app.log` on the first write of a new day, keep 7 days
Status: accepted
- Decision: `app.log` is always today's file. Under the existing cross-process log mutex, the first write
  of a day (per process, `_rotatedDay`) calls `LogRotation.Rotate`: if `app.log` was last written on an
  earlier day it becomes `app-yyyy-MM-dd.log` (that day), then archives ≥ 7 days old are deleted (today
  + 6 archives kept). Whichever process writes first rotates; the other then sees a fresh `app.log`.
- Why: the user wants at most the last 7 days, deleted automatically (2026-10-23). Keeping the name
  `app.log` means `scripts/run.ps1`, the About page's "open log" and every agent's habit keep working.
  The mutex already serializes both processes and every write opens/closes the file, so a rename between
  writes is safe.
- Why not per-day file names only (`app-<date>.log` written directly): everything that opens `app.log`
  would need today's name. Why not trimming one file: rewriting it means parsing multi-line exception
  entries and copying the whole log under the mutex on every trim. Why not launcher-only rotation: the
  app outlives day changes too, and the mutex already makes either process safe.
- Revisit if: someone tails `app.log` without delete sharing across midnight — the rename fails and that
  day's lines stay in `app.log` until the next day's rotation (logged to the debugger only).

## D-029 · 2026-10-24 · Release packaging: Velopack Setup.exe from a tag-triggered GitHub Actions workflow
Status: accepted; trigger amended by D-031 (push to main also releases)
Supersedes: D-003 (only "local build, nothing to install besides the folder"; unpackaged + self-contained stand)
- Decision: `scripts/pack.ps1` publishes (D-003) and runs `vpk pack` with packId `DazeMediaOsdApp`, Start-menu
  shortcut only. `release.yml` runs on a `v<Version>` tag: the tag must equal `<Version>` in
  Directory.Build.props (D-024 stays the one source), then tests, pack, and `vpk upload github --publish`
  (Setup.exe, portable zip, full + delta packages, update feed). `ci.yml` builds and tests every PR and main.
  `Program.Main` runs `VelopackApp` first; every hook restores the native flyout (Velopack force-kills
  launcher and app together, D-006), uninstall also removes our Run value if it points into the install
  folder. The before-update hook (old version, before the kill) sets `ExitSignal` (`Local\DazeMediaOsd.Exit`):
  the app exits like tray Exit, the launcher restores, nothing is left to kill. Unsigned for now. vpk is pinned in `dotnet-tools.json`, equal to the Velopack package.
  `scripts/install.ps1` builds the same package and runs its Setup.exe, so a local build and a release share
  one folder and one Apps entry (user, 2026-10-24: never two copies); it moves the pre-D-029
  `Programs\DazeMediaOsd` copy once (re-points the Run value, deletes the folder).
- Why: the user picked Velopack (2026-10-24) over WiX and Inno: per-user install without admin by default
  (hard rule), and it ships an update feed on GitHub Releases, so an in-app updater is one step away.
  packId isn't `DazeMediaOsd` because Velopack deletes `%LOCALAPPDATA%\<packId>` on uninstall and
  `%LOCALAPPDATA%\DazeMediaOsd` holds settings and logs. The install path `…\current\DazeMediaOsd.exe` is
  stable across updates, so the Run value the app writes (D-015) stays valid.
- Why not WiX MSI: per-user MSI needs care to avoid elevation, and no updates. Why not vpk `--msi`: a
  machine-wide bootstrapper (admin). Why not Inno Setup: no update feed. Why not MSIX: certificate (D-003).
  Why not a desktop shortcut: a tray app is reached from the tray and Start.
- Accepted risk: Setup over an existing install and uninstall kill before any of our code runs (no
  before-hook). If Setup then fails before its after-install hook, the flyout stays minimized until our next
  start, `--restore`, an explorer restart or sign-out, as for any double kill (D-006). `install.ps1` stops
  the app gracefully first, so only a hand-run Setup.exe or uninstall has that window.
- Revisit if: SmartScreen warnings bother users (sign: SignPath Foundation for OSS, or Azure Trusted Signing
  via `--azureTrustedSignFile`), or Store/winget distribution is wanted.

## D-030 · 2026-10-24 · In-app updates: check daily, download quietly, apply at the next launcher start
Status: accepted
- Decision: `Core.Updates.UpdateService` checks GitHub Releases (Velopack `GithubSource`, stable only, no
  token) 1 min after the app starts and every 24 h, downloads a newer release in the background and leaves
  it. The launcher applies a downloaded update before it starts the app (`ApplyPendingAtStart`: updater
  waits for the launcher to exit, applies, starts the exe bare = launcher), skipped when `VELOPACK_RESTART`
  is set so a failed apply can't loop. Velopack's own `SetAutoApplyOnStartup` is off. About has an Updates
  card: status in plain words, "Check for updates" / "Try again", and "Restart now" once one is ready (the
  app hands the update to the updater and exits like tray Exit). Not a Velopack install (Debug) → no checks.
  `DAZEMEDIAOSD_UPDATE_SOURCE` points the client at a local feed folder for testing.
- Why: the user asked for check at startup and daily, download in the background, apply on next start,
  shown in About (2026-10-24). Applying in the launcher means the app is never running mid-swap; the old
  version's before-update hook (D-029) still exits a running app cleanly if one is found.
- Why not Velopack's auto-apply: it runs in every process that calls `VelopackApp.Run()`, including `--app`
  and `--restore`, and restarts with that process's args. Why not apply right after download: a restart
  the user didn't ask for, while they may be using media keys. Why not a toast/tray badge: About is enough
  until the user asks for more.
- Revisit if: a release needs a migration step (use `OnAfterUpdateFastCallback`), GitHub rate limits bite
  (60 unauthenticated requests/h per IP), or the user wants a notification when an update is ready.

## D-031 · 2026-10-24 · Release on merge: a push to main with an untagged `<Version>` publishes it
Status: accepted
- Decision: `release.yml` also runs on pushes to main. A `check` job reads `<Version>` and asks the API for a
  published release `v<Version>` (`releases/tags/`, which ignores drafts): 200 stops, 404 releases, anything
  else fails the run. The `release` job tests, packs, deletes leftover drafts of that version (a failed upload
  leaves one, and vpk refuses an existing release), then `vpk upload --tag v<Version> --targetCommitish
  <sha>`: publishing the draft creates the tag on that commit. A hand-pushed `v<Version>` tag still works
  (must match `<Version>`). `concurrency: release` keeps two quick merges from releasing one version twice.
- Why: the user wants releases without tagging by hand (2026-10-24); D-024 already bumps the version in
  every feat/fix PR, so "untagged version on main" is exactly "something new to ship". Tag at upload, not up
  front: a failed run leaves no tag, so the next push to main retries instead of skipping that version.
  Accepted: a version that never released before a higher one lands is skipped (installs update to the
  newest anyway), and a queued run replaced by a newer push only drops an older version the same way.
- Why not a separate auto-tag workflow: a tag pushed with `GITHUB_TOKEN` doesn't trigger other workflows,
  so the release would never run without a PAT or GitHub App secret. Why not `workflow_call` from an
  auto-tag workflow: two files and inputs for what one `check` job does.
- Revisit if: a release should wait for a human (then a `workflow_dispatch` or draft-only mode), or
  version bumps start landing in PRs that aren't ready to ship.

## D-032 · 2026-10-24 · Opt-in: a track or play/pause change pops the OSD
Status: accepted
Supersedes: D-011 in part (its "a session change never pops" rule, now only the default)
- Decision: setting `showOnMediaChange`, off by default, needs `showMedia`. `OsdRules.ReasonToShow(before,
  after)` returns `MediaPlayback` for a new title/artist or play state in the same player, or a player that
  starts playing; a late cover, a paused session appearing or a session ending only refresh. The controller
  always requests the show; `App.Show` turns it into a refresh while the setting is off.
- Why: the user asked for it (2026-10-24), the trigger D-011 named. Off by default because D-011's reason
  stands: players change tracks on their own. After a media key it simply re-shows with the new track
  ~20–50 ms later, which replaces the refresh.
- Why not pop on any session change: a cover arriving or a browser tab opening paused would pop it.
  Why not two toggles (track / play-pause): the user asked for one option.
- Revisit if: daily use shows pops from videos/notifications in the browser, or play-state flicker between
  tracks pops twice.

## D-033 · 2026-10-24 · Cover art: text first, crossfade to the new cover
Status: accepted
- Decision: `GsmtcMediaService` publishes a new track/play state with the previous cover, then again once the
  new cover is read. `OsdWindow` keeps the shown cover until the new one is decoded, then fades it in (250 ms)
  on a second `Image` on top; no cover → the old one fades out to the placeholder. Decoded at 40 logical px
  (by height). Only while the OSD is already visible and "Animation effects" is on (D-012).
- Why: the user saw the cover lag behind the title (2026-10-24). Before, the title waited for the whole cover
  read, then the cover went to the placeholder icon while a 100–180 KB JPEG decoded at full size: a flash, then
  a pop. Now the text is instant and the cover change reads as intended.
- Why not cache covers by track: a player can reuse or change art per track; reads are once per change anyway.
  Why not hide the old cover until the new one comes: the placeholder flash was the visible part of the lag.
- Revisit if: the log shows `media thumbnail took` > ~300 ms often (then show the placeholder sooner), or the
  old cover under a new title reads as wrong.

## D-034 · 2026-10-24 · Content changes in a visible OSD fade through; volume stays instant
Status: accepted
- Decision: `FadeThrough` (out 100 ms ease-in, swap, in 150 ms ease-out, opacity only) for media title/artist,
  play state, output/input name, input icon, the output icon on mute or device-kind change, and "Muted" ↔
  mic level. Only while the OSD (and that row) was already visible and "Animation effects" is on (D-012). Each
  fader keeps the content last asked for: a repeat is a no-op (one key raises ~2 renders, which must not cut a
  fade), a non-fading change (level step, fresh show) cancels a running fade and applies at once, and a fading
  change during the fade-out replaces the waiting swap. The fade-in has no `From`, so a new change turns smoothly.
- Why: the user liked the cover crossfade (D-033) and asked for the same on every change (2026-10-24).
  Fade-through, not crossfade: text over text is unreadable mid-fade, and it needs no second element per field.
- Why not the volume %, level bar or the level-step glyph: a held key updates every 30–60 ms, so any
  transition lags or restarts (same reason as the fill, X-002). Why not animate a row appearing: it resizes
  the window, which `OsdWindow.Place` sets per frame only for the show/hide slide (D-012).
- Revisit if: fades feel slow during quick skips, or the user wants rows to animate in.
