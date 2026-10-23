# Dead ends

Things that were actually tried and failed, so nobody burns time on them again. Only real attempts go
here (an option rejected on paper belongs in the "Why not" of a decision). Format: `knowledge-log` skill.

## X-001 · 2026-10-22 · Read the native flyout's content via managed UI Automation
- Tried: `System.Windows.Automation` (`AutomationElement.FromHandle(host)`, control and raw view
  walkers) on the visible `XamlExplorerHostIslandWindow`, to tell volume from other flyouts.
- Failed because: only the root `Pane` is exposed; the XAML island content isn't reachable this way.
- Evidence: build 26300, spike branch `spike/q1-flyout-detection`.
- Retry only if: using native UIA3 (`IUIAutomation` via CsWin32) — untested, might see the island.
