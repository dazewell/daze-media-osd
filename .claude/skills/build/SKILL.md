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

`dotnet run` blocks until the launcher exits — start it in the background (or a terminal tab) when you
need to keep working, and stop it with `scripts/stop.ps1`. The Debug exe is
`src/DazeMediaOsd.App/bin/Debug/net10.0-windows10.0.26100.0/win-x64/DazeMediaOsd.exe`.

Seeing what the app did: `%LOCALAPPDATA%\DazeMediaOsd\logs\app.log` (both processes, pid per line).

Driving it without touching hardware:
- `pwsh scripts/press-key.ps1 175,174` — Volume Up then Down (173 = Mute); raw input sees these.
- `pwsh scripts/audio-device.ps1` lists render endpoints; `-SetDefault "<id>"` switches the default
  output. Switch back to the original afterwards — it's the user's machine.
- `dotnet run --project spikes/FlyoutProbe -- watch` presses Mute twice (net zero) and logs the native
  flyout host. Minimized = tiny rect, no SHOW/HIDE ~2.5 s apart.

## Verifying UI changes

Builds passing doesn't mean the OSD works. After a UI or interop change: run the app, press a volume key
(or change the default device in Sound settings), and confirm the OSD shows what you changed. If you
can't verify visually, say so explicitly instead of claiming it works.

- The OSD is the `--app` process's `WinUIDesktopWin32WindowClass` window titled `DazeMediaOsd`
  (`FindWindow` by title alone misses it — enumerate by pid + class). It hides 2.5 s after a show.
- Screen captures from PowerShell need `SetProcessDpiAwarenessContext(-4)` first, else they get the
  scaled-down top-left part of the screen.
- Tray: the icon window is class `DazeMediaOsd.Tray` (uID 1). `Shell_NotifyIconGetRect` gives its
  screen rect (or the overflow chevron's while it sits in the closed overflow — click that first);
  right-click there for the menu (Settings, separator, Exit): Down Down Enter picks Exit. A clean stop
  for testing: tray → Exit. Left-click opens the settings window (also `WinUIDesktopWin32WindowClass`,
  title `Daze Media OSD`).
- Settings live in `%LOCALAPPDATA%\DazeMediaOsd\settings.json`; delete it after a test that changed
  settings — it's the user's machine. Every change shows a preview OSD (log `show SettingsPreview`).
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
| App starts then exits silently (unpackaged) | Windows App SDK runtime not found: ensure `WindowsAppSDKSelfContained=true` and `WindowsPackageType=None`. |
