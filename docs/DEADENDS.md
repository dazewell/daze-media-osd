# Dead ends

Things that were actually tried and failed, so nobody burns time on them again. Only real attempts go
here (an option rejected on paper belongs in the "Why not" of a decision). Format: `knowledge-log` skill.

## X-001 · 2026-10-22 · Read the native flyout's content via managed UI Automation
- Tried: `System.Windows.Automation` (`AutomationElement.FromHandle(host)`, control and raw view
  walkers) on the visible `XamlExplorerHostIslandWindow`, to tell volume from other flyouts.
- Failed because: only the root `Pane` is exposed; the XAML island content isn't reachable this way.
- Evidence: build 26300, spike branch `spike/q1-flyout-detection`.
- Retry only if: using native UIA3 (`IUIAutomation` via CsWin32) — untested, might see the island.

## X-002 · 2026-10-22 · WinUI `ProgressBar` as the OSD volume bar
- Tried: determinate `ProgressBar` (Max 100), `Value` set on every show; `Opacity` 0.4 for mute.
- Failed because: its value transition restarts on each update while a key is held (bar jumps), and at
  0–2 % the indicator template draws a stray dot at the left edge (user, 2026-10-22).
- Evidence: build 26300, WinAppSDK 2.5.1, PR #4 first commit.
- Retry only if: never for a fast-updating level; use the track + fill in `OsdWindow.xaml`.
