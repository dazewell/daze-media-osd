# Similar apps: feature ideas beyond the plan (2026-10-22)

Snapshot, not maintained. Question: which features of comparable apps could daze-media-osd add beyond
GOALS (v1 scope, Later)? Sources: READMEs, release notes, issue trackers, reviews; links inline. The user
decides what joins the scope; nothing here is a decision.

Already planned (not repeated below): media info + media keys, animations, autostart, settings, CI,
brightness / airplane / keyboard-layout flyouts, per-app volume, quick device switching.

## Method and gaps

- Primary: GitHub READMEs and issues of ModernFlyouts, Flylet, EarTrumpet, Background Music;
  FluentFlyout release notes. Secondary (reviews/aggregators): 3RVX, TbVolScroll, Windhawk mods,
  SoundSource, GNOME.
- Not covered: KDE Plasma OSD (search returned nothing), Volume2 details, Windhawk volume-OSD mods (none
  found; Windhawk mods are scroll-to-volume only). Issue "most wanted" is judged by comment counts; the
  tracker view did not expose reaction counts.
- ModernFlyouts is archived (2025-11-15); Flylet continues it. Both replace the native flyout the same
  way we do (hidden only while the app runs).

## What users ask for most (ModernFlyouts tracker)

- Flyout also on volume changes not from media keys (#4, 51 comments) — we already cover this (D-007).
- Per-app volume (#164, 17; "one of the most requested changes from Store feedback"), tray volume
  flyout replacement (#103), battery flyout (#352, #903 Bluetooth battery), camera/mic notification
  flyouts (#841, #225), Bluetooth connected indicator (#456), desktop-PC brightness (#102), hide
  timeline when unsupported (#392), light/dark/system theme (#75), multi-monitor choice (#672).
- Bugs that hurt trust: flyout doesn't close (#464), not replacing the new Win11 flyouts (#920),
  broke on 24H2 (#1349). Lesson: suppression robustness matters more than extras (our D-006 launcher).

## Feature table

| App | Feature | Description | Source |
|---|---|---|---|
| ModernFlyouts | Lock-keys flyout | Caps/Num/Scroll Lock, Insert/Overtype state | [README](https://github.com/ModernFlyouts-Community/ModernFlyouts) |
| ModernFlyouts | Draggable, remembered position; pin/hide top bar | Drag OSD, position saved | README |
| ModernFlyouts | Per-module disable, background opacity, timeout | Settings per flyout | README |
| ModernFlyouts | Media shuffle/repeat/stop/timeline | Extra GSMTC controls, player dependent | README |
| ModernFlyouts | Battery flyout (requested) | Battery / Bluetooth battery | [#352](https://github.com/ModernFlyouts-Community/ModernFlyouts/issues/352), #903 |
| ModernFlyouts | Mic mute / camera notifications (requested) | Flyout on mic/camera toggle | #841, #225 |
| ModernFlyouts | Per-app volume (requested) | Reuse EarTrumpet idea | [#164](https://github.com/ModernFlyouts-Community/ModernFlyouts/issues/164) |
| Flylet | Accent colour from album art | Media card tinted by artwork | [README](https://github.com/ItsMrZxD/Flylet) |
| Flylet | Media card styles | Classic / Minimal / Compact pill / Album background | README |
| Flylet | Custom background image/GIF, blur, darken | Cosmetic theming | README |
| Flylet | Live-estimated timeline | Position advances between GSMTC updates | README |
| FluentFlyout | Taskbar widget with now-playing | Media info on the taskbar, session switcher on right-click | [v2.16.0](https://newreleases.io/project/github/unchihugo/FluentFlyout/release/v2.16.0) |
| FluentFlyout | "Up Next" flyout | Shows the next track | [site](https://fluentflyout.com/faq/) |
| FluentFlyout | Hotkey to show volume flyout (Ctrl+Alt+V) | Pop the OSD on demand | v2.16.0 |
| FluentFlyout | OEM/Bluetooth device keys trigger flyouts | Keys not seen as keyboard input | v2.16.0 |
| EarTrumpet | Per-app volume + peak meters | Mixer in tray flyout | [README](https://github.com/File-New-Project/EarTrumpet) |
| EarTrumpet | Per-app device routing | Send app to another output | README |
| EarTrumpet | Default device switch from tray menu | Click to change default | README |
| EarTrumpet | Configurable hotkeys | Open flyout/mixer/settings | README |
| 3RVX | Skins, volume step, multi-monitor | Skinnable OSD, custom hotkeys | [ghacks](https://www.ghacks.net/?p=5779) (unmaintained since 2017) |
| TbVolScroll | Wheel over taskbar changes volume | 5 % per notch, Alt = 1 %, finer below 10 % | [ghacks](https://www.ghacks.net/2020/09/01/control-the-volume-with-the-mouse-scroll-wheel-when-the-cursor-is-over-the-taskbar/) |
| Windhawk mods | Taskbar scroll system / per-app volume | Ctrl+click mutes app, tooltip level | [Neowin](https://www.neowin.net/news/this-genius-mod-makes-volume-control-in-windows-11-so-much-better/), [Ramen](https://ramensoftware.com/tag/windhawk/page/2) |
| Background Music (macOS) | Auto-pause music when other audio plays | Pause player, resume after | [README](https://github.com/kyleneideck/BackgroundMusic) |
| Background Music | Volume boost above normal max | Per-app gain | README |
| SoundSource (macOS) | Menu-bar icon shows level/mute; I/O meters | Live level in the bar | [Six Colors](https://sixcolors.com/post/2020/08/soundsource-5-adds-new-features-revamped-interface/) |
| SoundSource | Global mic / app mute, EQ, effects | Quick mutes | [MacTech](https://www.mactech.com/2020/08/06/soundsource-5-for-macos-gets-updated-look-much-more/amp/) |
| GNOME 43+ | Device submenu on the volume slider | Pick output/input without Settings | [OMG Linux](https://www.omglinux.com/gnome-43-makes-it-much-easier-to-switch-audio-device/) |

## Shortlist (fits Windows 11, tray, no injection, no admin, fail-open, no focus)

Effort: S ≤ ½ day, M ≈ 1–2 days, L > 2 days. Ordered by value for our niche (device info + OSD).

| # | Feature | User value | Effort | API | Decision conflict |
|---|---|---|---|---|---|
| 1 | Pop OSD on demand via global hotkey | See device/volume without changing it; FluentFlyout has it | S | `RegisterHotKey` (no hook, no admin) | None; extends D-007 trigger list |
| 2 | Volume step and fine step (Shift/Alt) for our own volume keys | 1 % steps, requested by 3RVX/TbVolScroll users; but we only observe keys today | M | Core Audio `IAudioEndpointVolume.SetMasterVolumeLevelScalar`; needs swallowing native key | No D-xxx forbids it, but D-007 assumes Windows applies the key and we only observe it; owning the key is a new decision |
| 3 | Taskbar scroll → volume (and OSD) | Popular (TbVolScroll, Windhawk) | M | Low-level mouse hook or UIA hit-test of taskbar; hook is an anti-cheat/latency worry (cf. D-005 reasoning) | Not D-002 (no injection) but hook caveat |
| 4 | Mic mute / camera in-use indicator in the OSD | Requested twice (#225, #841) | S–M | Core Audio capture mute (done); camera: `ConsentStore` registry webcam key or `DeviceWatcher` | Extends D-007 mic-mute rule |
| 5 | Lock-keys flyout (Caps/Num/Scroll) | Shipped by every flyout app | S | `GetKeyState` + raw input (already have `RawInputKeySource`) | Overlaps "keyboard-layout flyouts" in Later |
| 6 | Battery / Bluetooth device battery | Requested #352, #903 | M | `Windows.Devices.Enumeration` `System.Devices.Aep.*BatteryLevel`; `GetSystemPowerStatus` for the laptop | None |
| 7 | Accent colour from album art, media card styles | Visual polish, Flylet's signature | S–M | `BitmapDecoder` + dominant colour; CsWinRT | Builds on the planned media row |
| 8 | Seekable media timeline, shuffle/repeat | Standard in every competitor | M | GSMTC `GetTimelineProperties`, `TryChangePlaybackPositionAsync`; OSD is click-through, so display only unless interactive mode | Click-through is D-008; display-only is safe |
| 9 | Drag-to-position + remembered position, multi-monitor choice | Common requests (#672, ModernFlyouts) | M | `SetWindowPos`, `MonitorFromPoint`; drag needs removing `WS_EX_TRANSPARENT` | D-008 click-through: needs a modifier or edit mode |
| 10 | Output-device change toast with device icon and type (headphones/speakers/HDMI) | Matches our core idea | S | `PKEY_DeviceInterface_FriendlyName`, `PKEY_Device_IconPath`, `PKEY_AudioEndpoint_FormFactor` | None |
| 11 | Bluetooth connected/disconnected indicator | Requested #456 | S | Audio endpoint arrival/removal (already notified) + `DeviceWatcher` | None |
| 12 | Pause media on device change (headphones unplugged) | Common expectation, not in the apps above but a natural fit | S | GSMTC `TryPauseAsync` when default endpoint removed | Extends D-007 |
| 13 | Auto-pause music when other audio plays (Background Music) | Nice, but heuristic | L | `IAudioSessionManager2` session events + GSMTC | Scope creep; low priority |
| 14 | Peak meter in OSD or per-app mini-mixer | Differentiator once per-app lands | L | `IAudioMeterInformation`, `IAudioSessionManager2` | Already in Later (per-app) |
| 15 | Light/dark/system theme and accent tint option | Asked #75 | S | `UISettings`, WinUI `RequestedTheme` | None (GOALS: theme follows system) |

Suggested first picks (small, high fit): 10, 1, 5, 4, 11, 12; then 7 and 8 together with the media PR.

## Doesn't fit

- Keyboard backlight / OEM Fn flyouts: keys send OEM hardware signals, not observable (ModernFlyouts README).
- Replacing/modifying the native flyout or the taskbar volume icon flyout via Windhawk-style mods: DLL
  injection into explorer (D-002).
- Per-app device routing (EarTrumpet): uses undocumented `IAudioPolicyConfig`; fragile across builds, and
  not an OSD feature.
- System-wide EQ / effects / volume boost beyond 100 % (SoundSource): needs a virtual audio driver (admin,
  driver signing).
- Recording system audio (Background Music): out of scope, needs a virtual device.
- Skinning engines (3RVX): large surface for little value; Flylet-style presets cover the need.
- Drawing over exclusive-fullscreen games: GOALS non-goal.
- Windows 10 support, Microsoft Store / MSIX distribution: D-004, D-003 (v2 revisit).
- Rich notifications/"Up Next" queue (FluentFlyout): GSMTC doesn't expose queues.
- Taskbar widget with now-playing (FluentFlyout): needs anchoring into the taskbar; fragile.
