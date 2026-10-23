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
Status: accepted
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
Status: accepted
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
