# Tech stack research (2026-10-22)

Snapshot backing D-001. Not maintained — if facts change, add a new decision instead of editing this.

## Requirements that drive the choice

1. OSD must pop instantly on key press and look like Win11 (Fluent, Mica/Acrylic, rounded corners).
2. Settings UI must look modern/native.
3. Deep Windows access: Core Audio COM, WinEvent hooks, WinRT media sessions.
4. Windows-only, local build, agent-written code.

## Options

| Option | Native Win11 look | Interop | Footprint | Maturity (2026-10) | Verdict |
|---|---|---|---|---|---|
| **C# .NET 10 + WinUI 3** | Native (it is the Win11 UI stack) | CsWin32 / CsWinRT, excellent | Moderate; Native AOT supported since WinAppSDK 1.6 | WinAppSDK 2.0 stable 2026-04, 2.4 in 2026-08, semver now | **Chosen** |
| Rust + windows-rs + WinUI (windows-reactor) | Native | Excellent | Smallest | windows-reactor merged 2026-05, v0.0.0, follow-up PRs open | Too early |
| Rust + egui/Slint/iced | Imitation | Excellent | Small | Stable | UI not native |
| Tauri 2 (Rust + WebView2) | Imitation (CSS) | Good via Rust | WebView2 per window | Stable | OSD latency/memory, not native |
| C++/WinRT + WinUI 3 | Native | Excellent | Small | Stable | Slow iteration, no gain over C# |
| WPF (+ WPF-UI / Fluent theme) | Close but not native | Good | Moderate | Stable | Mica/Fluent via 3rd party |
| Microsoft.UI.Reactor (C# declarative WinUI) | Native | Same as C# | Same | 0.1.0-preview | Interesting later; preview |
| Avalonia / Uno | Imitation / native-ish | Good | Moderate | Stable | Cross-platform overhead not needed |

## Facts worth keeping

- .NET 10 is LTS (support to 2028-11). .NET 11 (STS) expected 2026-11 — stay on 10 unless a feature is needed.
- Windows App SDK 2.x uses semver: breaking changes only on major versions → safe to float minor.
- WinUI has no built-in tray icon (Microsoft acknowledged the gap); needs a library or Win32 interop.
- Prior art, all C#: ModernFlyouts (archived 2025), its continuation Flylet (MIT, .NET 10, hides native
  flyout by watching explorer window events, knows Win10 and Win11 window classes), EarTrumpet (audio).
- Local machine at research time: Windows build 26300, only .NET 9 SDK installed (need 10), no
  Visual Studio, no Rust. WinUI 3 C# projects build with the `dotnet` CLI alone.

## Sources

- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0
- https://github.com/microsoft/windows-rs/pull/4479 , https://github.com/microsoft/windows-rs/pull/4782
- https://microsoft.github.io/microsoft-ui-reactor/latest/packaging/
- https://blogs.windows.com/windowsdeveloper/2024/09/04/whats-new-in-windows-app-sdk-1-6/
- https://github.com/ItsMrZxD/Flylet , https://github.com/ShankarBUS/ModernFlyouts
