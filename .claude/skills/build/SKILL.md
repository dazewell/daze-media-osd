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

Checking the native flyout while the app runs: `dotnet run --project spikes/FlyoutProbe -- watch`
presses Mute twice (net zero) and logs the host. Minimized = tiny rect, no SHOW/HIDE ~2.5 s apart.

## Verifying UI changes

Builds passing doesn't mean the OSD works. After a UI or interop change: run the app, press a volume key
(or change the default device in Sound settings), and confirm the OSD shows what you changed. If you
can't verify visually, say so explicitly instead of claiming it works.

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
