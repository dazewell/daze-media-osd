# daze-media-osd

A Windows 11 replacement for the volume/media on-screen display that shows extra information — starting
with the current output and input audio device — plus a Fluent-styled settings app.

Status: early — tray app with its own OSD (output + input device, volume, mute) and a settings window;
media info and autostart are next. See [docs/GOALS.md](docs/GOALS.md) for scope and progress.

Build and start (stops a running instance first): `pwsh scripts/run.ps1` (`-Test` also runs the tests).

- Stack: C# / .NET 10, WinUI 3 (Windows App SDK 2.x), unpackaged — why: [docs/DECISIONS.md](docs/DECISIONS.md)
- Design: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Building: [.claude/skills/build/SKILL.md](.claude/skills/build/SKILL.md)

This repository is developed by AI agents; [CLAUDE.md](CLAUDE.md) is their entry point.
