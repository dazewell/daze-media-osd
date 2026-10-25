# Release package (D-029): self-contained publish (D-003), then Velopack's per-user Setup.exe, portable zip and
# update feed in artifacts/releases. The release workflow runs this; locally it needs `vpk` on PATH
# (dotnet tool install -g vpk --version <VpkVersion>). Example: pwsh scripts/pack.ps1
param([string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/releases'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts/publish'
# Not "DazeMediaOsd": Velopack installs to %LOCALAPPDATA%\<packId> and deletes it on uninstall, and
# %LOCALAPPDATA%\DazeMediaOsd holds settings.json and the logs.
$packId = 'DazeMediaOsdApp'

$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src/DazeMediaOsd.App') -c Release -r win-x64 -o $publish -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }
# Without the .pri the app dies loading XAML (X-004).
if (-not (Test-Path (Join-Path $publish 'DazeMediaOsd.pri'))) { throw 'Publish has no DazeMediaOsd.pri (X-004).' }

vpk pack --packId $packId --packVersion $version --packDir $publish --mainExe DazeMediaOsd.exe `
    --packTitle 'Daze Media OSD' --packAuthors dazewell --icon (Join-Path $root 'src/DazeMediaOsd.App/Assets/app.ico') `
    --shortcuts StartMenuRoot --outputDir $Output
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)." }
Write-Host "Packed $version into $Output"
