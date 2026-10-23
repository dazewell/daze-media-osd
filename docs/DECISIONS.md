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
Status: accepted (pending spike Q1)
- Decision: detect the native volume flyout (WinEvent hook on explorer.exe windows), hide it, show our
  own OSD. Same model as ModernFlyouts/Flylet.
- Why: there is no API to add content to the native flyout. Hiding it per-show is reversible and
  fail-open (our process dies → native flyout simply shows again).
- Why not DLL injection into explorer / Windhawk-style mod: fragile across Windows updates, AV/anti-cheat
  risk, can crash the shell. Why not "companion panel next to the native flyout": two flyouts look
  broken; kept only as a fallback mode if suppression breaks on some build.
- Revisit if: suppression stops working on a Windows update and can't be fixed quickly.

## D-003 · 2026-10-22 · Unpackaged, self-contained, single process, local build
Status: accepted
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
