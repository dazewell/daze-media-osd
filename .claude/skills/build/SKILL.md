---
name: build
description: Build, run, test and publish the daze-media-osd WinUI 3 / .NET 10 app on the local Windows machine, including prerequisite checks and fixes for common build failures. Use whenever you need to compile, run the app to see a change, run tests, produce a publish folder, set up the dev environment, or when a dotnet/MSBuild/XAML/Windows App SDK error appears — even if the user just says "try it" or "does it work".
---

# Build

v1 builds locally with the `dotnet` CLI only — no Visual Studio required (D-003). CI arrives in v2.

> **Status:** the solution doesn't exist yet. The first coding session creates it following the layout
> in `docs/ARCHITECTURE.md`, then updates the commands below to match reality and deletes this note.

## Prerequisites

Run `pwsh .claude/skills/build/scripts/check-prereqs.ps1`. It checks the .NET 10 SDK, Windows build and CPU
architecture and prints how to fix what's missing. Install the SDK with:

```powershell
winget install Microsoft.DotNet.SDK.10
```

Installing software changes the user's machine — ask before running it.

## Commands (from repo root, PowerShell)

```powershell
# The tray app keeps running after you test it and locks its own exe → stop it before every build.
Get-Process DazeMediaOsd -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet build DazeMediaOsd.slnx -c Debug
dotnet test  DazeMediaOsd.slnx -c Debug --no-build
dotnet run   --project src/DazeMediaOsd.App -c Debug          # start the app to see changes
dotnet publish src/DazeMediaOsd.App -c Release -r win-x64 -o artifacts/publish
```

`dotnet run` blocks until the app exits — start it in the background (or a terminal tab) when you need
to keep working, and stop it with the `Stop-Process` line above.

## Verifying UI changes

Builds passing doesn't mean the OSD works. After a UI or interop change: run the app, press a volume key
(or change the default device in Sound settings), and confirm the OSD shows what you changed. If you
can't verify visually, say so explicitly instead of claiming it works.

## Troubleshooting

Grow this table when you hit something non-obvious (and record a dead end if a fix attempt failed).

| Symptom | Cause / fix |
|---|---|
| `MSB3027` / file is locked by `DazeMediaOsd` | Running instance; stop it (command above). |
| `NETSDK1045` / SDK 10 not supported | Only an older SDK installed; install .NET 10 SDK. `global.json` pins it. |
| App starts then exits silently (unpackaged) | Windows App SDK runtime not found: ensure `WindowsAppSDKSelfContained=true` and `WindowsPackageType=None`. |
