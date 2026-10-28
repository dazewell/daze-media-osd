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

## X-006 · 2026-10-23 · `TitleBar.IconSource` with an `ms-appx:///` image in XAML
- Tried: `<TitleBar.IconSource><ImageIconSource ImageSource="ms-appx:///Assets/app.png"/>` in the settings
  window (unpackaged).
- Failed because: the whole window renders blank white (tree built, UIA sees it; no exception logged).
  `ms-appx:///Assets/app.png` in an `Image` on a page renders fine.
- Evidence: build 26300, WinAppSDK 2.5.1.
- Retry only if: never in XAML; the window sets `AppTitleBar.IconSource` from code with a file path.

## X-007 · 2026-10-24 · Leaving `SystemBackdrop.OnDefaultSystemBackdropConfigurationChanged` to the base
- Tried: a custom `SystemBackdrop` (`AlwaysActiveAcrylicBackdrop`, D-012) that doesn't override it.
- Failed because: on a system light/dark switch from Settings the base throws `ArgumentException`
  ("target", E_INVALIDARG) inside a XAML event → fail-fast 0xC000027B, app gone (launcher restored).
  A registry flip + `WM_SETTINGCHANGE` "ImmersiveColorSet" doesn't trigger it; the Settings combo does.
- Evidence: build 26300, WinAppSDK `Microsoft.UI.Xaml.dll` 3.2.3, user crash 2026-10-24 17:02 and
  reproduced by UIA on the Settings "Choose your mode" combo; app log stack from `UnhandledException`.
- Retry only if: never; the backdrop overrides it as a no-op and follows `ActualThemeChanged`.

## X-008 · 2026-10-26 · Handling a `gh api` 404 in a `shell: pwsh` step without resetting `$LASTEXITCODE`
- Tried: release.yml `check` step treated 404 as "not released yet" (`release=true`) and fell through.
- Failed because: Actions runs `pwsh -command ". '{0}'"`, which exits with the last native command's
  `$LASTEXITCODE`; the 404's 1 survived, so the step failed after printing the right answer and v0.4.0 wasn't released.
- Evidence: run 40163907848 on `main` 4a5f64b, 2026-10-25.
- Retry only if: never; any handled non-zero native exit sets `$global:LASTEXITCODE = 0`.

## X-009 · 2026-10-28 · Taskbar hit test by `WindowFromPoint`; probing the wheel with `SendInput`
- Tried: `WindowFromPoint` + `GetAncestor(GA_ROOT)` to see if the cursor is on `Shell_TrayWnd`; UIA `ElementFromPoint`;
  injecting wheel notches with `SendInput` over each taskbar button to look for a native reaction (Q7).
- Failed because: over the taskbar `WindowFromPoint` returns a top-level, non-cloaked `Windows.UI.Core.CoreWindow`
  laid over it, never the taskbar; UIA stops at the `Shell_TrayWnd` pane. `SendInput` failed with error 5 (UIPI)
  for most notches, so "no volume change" from injected notches proves nothing.
- Evidence: 26300, `spikes/TaskbarWheelProbe auto` (4 of 57 notches sent; all 4 seen by the raw-input sink).
- Retry only if: never for the hit test (use the rects, Q6); `SendInput` only from a context without UIPI limits.
