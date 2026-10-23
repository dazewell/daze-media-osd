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

## X-003 · 2026-10-22 · Defaults as property initializers on an init-only record for System.Text.Json
- Tried: `AppSettings` with `{ get; init; } = default` properties, deserialized via source-gen context.
- Failed because: source-gen sets every init-only property in the object initializer, so a property
  missing from the JSON comes back as `default(T)` (`HideAfterSeconds` 0), not the initializer value.
- Evidence: `SettingsStoreTests.MissingPropertiesKeepDefaults`, .NET 10.0.402.
- Retry only if: never; put defaults on the primary constructor's parameters (STJ honours those).

## X-004 · 2026-10-22 · `dotnet publish` of the unpackaged app without `EnableMsixTooling`
- Tried: `dotnet publish -c Release -r win-x64` with `WindowsPackageType=None`, self-contained (D-003).
- Failed because: publish output lacks `DazeMediaOsd.pri` and the `.xbf` files (bin/ has them), so the
  app crashes at the first XAML load: 0xC000027B in `Microsoft.UI.Xaml.dll`; the launcher restored.
- Evidence: build 26300, WinAppSDK in `Directory.Packages.props`, .NET 10.0.402.
- Retry only if: never; keep `EnableMsixTooling=true` in the App csproj (publish then emits the .pri
  with the XAML in it). After a WinAppSDK upgrade, check the publish folder has `DazeMediaOsd.pri`.

## X-005 · 2026-10-23 · `KSPROPERTY_ONESHOT_RECONNECT`/`DISCONNECT` on the offloaded A2DP endpoint only
- Tried: the BtAudio one-shot properties on "Headphones (Technics EAH-AZ80)" (filter `intcbttoporender_…`,
  Intel Smart Sound BT offload), as the device shortcut's own endpoint.
- Failed because: the filter accepts both (`S_OK`, basic support OK) and does nothing — no state change in
  15 s. The same requests on the "Headset" (hands-free, `intcbttopocapture_…`) endpoint work for the whole device.
- Evidence: build 26300, `spikes/BtProbe`, app log 15:46 (timed out) vs 15:51 (connected), D-023.
- Retry only if: never as the only target; D-023 sends to every endpoint of the container.
