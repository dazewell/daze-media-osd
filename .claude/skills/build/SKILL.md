---
name: build
description: Build, run, test and publish the daze-media-osd WinUI 3 / .NET 10 app on the local Windows machine, including prerequisite checks and fixes for common build failures. Use whenever you need to compile, run the app to see a change, run tests, produce a publish folder, set up the dev environment, or when a dotnet/MSBuild/XAML/Windows App SDK error appears — even if the user just says "try it" or "does it work".
---

# Build

v1 builds locally with the `dotnet` CLI only — no Visual Studio required (D-003). CI arrives in v2.

## Prerequisites

Run `pwsh .claude/skills/build/scripts/check-prereqs.ps1`. It checks the .NET 10 SDK, Windows build and CPU
architecture and prints how to fix what's missing. Install the SDK with:

```powershell
winget install Microsoft.DotNet.SDK.10
```

Installing software changes the user's machine — ask before running it.

## Commands (from repo root, PowerShell)

Everyday loop — stop, build, start in the background, show the app's start line (non-zero exit if any
step fails):

```powershell
pwsh scripts/run.ps1                 # -Test runs the tests too; -NoBuild just restarts; -Configuration Release
```

The individual steps:

```powershell
# The tray app keeps running after you test it and locks its own exe → stop it before every build.
# Never Stop-Process both DazeMediaOsd.exe processes (launcher + `--app`, D-006): the native flyout
# would stay minimized. The script kills `--app`; the launcher then restores the flyout and exits.
pwsh scripts/stop.ps1

dotnet build DazeMediaOsd.slnx -c Debug
dotnet test --solution DazeMediaOsd.slnx -c Debug --no-build   # Microsoft.Testing.Platform syntax
dotnet run --project src/DazeMediaOsd.App -c Debug             # starts the launcher, which starts --app
dotnet publish src/DazeMediaOsd.App -c Release -r win-x64 -o artifacts/publish   # self-contained, ~230 MB
```

Daily-driver install: `pwsh scripts/install.ps1` stops any running copy, publishes to
`%LOCALAPPDATA%\Programs\DazeMediaOsd` and starts it there. The user's autostart (D-015) points at that
exe: after testing a Debug build, reinstall or start the installed copy again
(`& "$env:LOCALAPPDATA\Programs\DazeMediaOsd\DazeMediaOsd.exe"`) so the user isn't left without the app.
Toggling "Start with Windows" from a Debug build re-points the user's Run value at the Debug exe: put it
back from the installed copy afterwards.

`dotnet run` blocks until the launcher exits — start it in the background (or a terminal tab) when you
need to keep working, and stop it with `scripts/stop.ps1`. The Debug exe is
`src/DazeMediaOsd.App/bin/Debug/net10.0-windows10.0.26100.0/win-x64/DazeMediaOsd.exe`.

Seeing what the app did: `%LOCALAPPDATA%\DazeMediaOsd\logs\app.log` (both processes, pid per line).
Read it (and `settings.json`) from PowerShell: the agent's Bash tool may run sandboxed with a stale copy
of `%LOCALAPPDATA%` — it showed a log 25 minutes old while the real one was current (2026-10-22).

Driving it without touching hardware:
- `pwsh scripts/press-key.ps1 175,174` — Volume Up then Down (173 = Mute; 179 Play/Pause, 176 Next,
  177 Previous); raw input sees these. Called from another script, use `& scripts/press-key.ps1 175,174`:
  `pwsh -File … 175,174` passes "175,174" as one string.
- Real media playback without touching the user's media: serve a page that plays a quiet `<audio>` and
  sets `navigator.mediaSession.metadata` (title, artist, canvas artwork) and next/previous handlers from
  the scratchpad (`python -I -m http.server 8765 --bind 127.0.0.1`), open it in the browser pane, click
  Play. It becomes a GSMTC session (log `media: …`); media keys drive it. Close the tab afterwards.
- `pwsh scripts/audio-device.ps1` lists render endpoints; `-SetDefault "<id>"` switches the default
  output. Switch back to the original afterwards — it's the user's machine. `-All` lists every endpoint
  with its state (1 active, 2 disabled); `-Enable "<id>"` / `-Disable "<id>"` is a real device
  connect/disconnect — use a disabled, non-default one (e.g. Stereo Mix) and disable it again after.
- `dotnet run --project spikes/FlyoutProbe -- watch` presses Mute twice (net zero) and logs the native
  flyout host. Minimized = tiny rect, no SHOW/HIDE ~2.5 s apart.

## Verifying UI changes

Builds passing doesn't mean the OSD works. After a UI or interop change: run the app, press a volume key
(or change the default device in Sound settings), and confirm the OSD shows what you changed. If you
can't verify visually, say so explicitly instead of claiming it works.

- The OSD is the `--app` process's `WinUIDesktopWin32WindowClass` window titled `DazeMediaOsd`
  (`FindWindow` by title alone misses it — enumerate by pid + class). It hides 2.5 s after a show.
- Screen captures from PowerShell need `SetProcessDpiAwarenessContext(-4)` first, else they get the
  scaled-down top-left part of the screen. If `CopyFromScreen` shows what's *under* the OSD (another
  topmost window, e.g. the agent's own app, sits above it — seen 2026-10-22), capture the window itself:
  `PrintWindow(hwnd, hdc, 2)` (PW_RENDERFULLCONTENT) renders its XAML content, without the acrylic.
  `pwsh -File` + `Add-Type` takes ~1–2 s to start: press keys / toggle devices from inside the capture
  script, or the OSD has hidden by the time it captures.
- Tray: the icon window is class `DazeMediaOsd.Tray` (uID 1). `Shell_NotifyIconGetRect` gives its
  screen rect (or the overflow chevron's while it sits in the closed overflow — click that first);
  right-click there for the menu (Output ▸, Input ▸, separator, Settings, separator, Exit). Posting
  `WM_APP+1` with lParam `0x7B | (1 << 16)` (WM_CONTEXTMENU, icon 1) and wParam `x | y << 16` opens it
  from a script, but then its `SetForegroundWindow` fails (foreground lock) and keys go elsewhere:
  pick items with the mouse (`SetCursorPos` + `mouse_event`, physical pixels after the DPI call). A clean stop
  for testing: tray → Exit. Left-click opens the settings window (also `WinUIDesktopWin32WindowClass`,
  title `Daze Media OSD`).
- Settings live in `%LOCALAPPDATA%\DazeMediaOsd\settings.json` and hold the user's real settings: copy
  it before a test that changes settings and restore it byte-for-byte afterwards (compare
  `Get-FileHash`), with the app stopped. Never delete it. Every change shows a preview OSD (log
  `show SettingsPreview`). UI Automation drives the window: post `WM_APP+1` with lParam
  `0x400 | (1 << 16)` (NIN_SELECT, icon 1) to the `DazeMediaOsd.Tray` window to open it (title
  `Daze Media OSD`); toggles are found by their `AutomationProperties.Name` (e.g. "Show media").
- Also check: foreground window unchanged by a show (`GetForegroundWindow`), and `WindowFromPoint`
  over the OSD returns the window below (click-through).

## Troubleshooting

Grow this table when you hit something non-obvious (and record a dead end if a fix attempt failed).

| Symptom | Cause / fix |
|---|---|
| `MSB3027` / file is locked by `DazeMediaOsd` | Running instance; stop it (command above). |
| `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform` | `global.json` must keep `"test": { "runner": "Microsoft.Testing.Platform" }`; use `dotnet test --solution …`. |
| `SYSLIB1051` on a `[LibraryImport]` with `HWND` | CsWin32 structs aren't marshallable by the source generator; declare `nint` (HWND converts implicitly). |
| `NETSDK1045` / SDK 10 not supported | Only an older SDK installed; install .NET 10 SDK. `global.json` pins it. |
| No volume OSD at all (neither ours nor native) | Host left minimized after both processes were killed: run `DazeMediaOsd.exe --restore` (or `dotnet run --project spikes/FlyoutProbe -- restore`), or restart explorer. |
| Native flyout shows again / ours doesn't appear after a Windows update | Host class or band changed: `dotnet run --project spikes/FlyoutProbe -- watch`, compare with ARCHITECTURE → Native flyout and D-006. |
| Published app crashes at start, 0xC000027B in `Microsoft.UI.Xaml.dll` | Publish folder lacks `DazeMediaOsd.pri`: keep `EnableMsixTooling=true` in the App csproj (X-004). |
| App starts then exits silently (unpackaged) | Windows App SDK runtime not found: ensure `WindowsAppSDKSelfContained=true` and `WindowsPackageType=None`. |
