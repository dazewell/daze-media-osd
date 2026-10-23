# Daily-driver install: publish Release (self-contained, D-003) to a fixed folder outside any checkout and
# start it from there. Autostart is the app's own "Start with Windows" toggle, which registers the copy it
# runs from (D-015): turn it on once from the installed copy and later installs keep it.
# Example: pwsh scripts/install.ps1
param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs/DazeMediaOsd'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $Destination 'DazeMediaOsd.exe'

# Any running copy (Debug or installed) locks files or would block the new one's single-instance mutex.
& (Join-Path $PSScriptRoot 'stop.ps1')

# Publish next to the destination, then swap: a failed publish leaves the installed copy intact.
$staging = "$Destination.new"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish (Join-Path $root 'src/DazeMediaOsd.App') -c Release -r win-x64 -o $staging -v q -nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed ($LASTEXITCODE); installed copy unchanged." }
if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
Move-Item $staging $Destination

Start-Process -FilePath $exe -WorkingDirectory $Destination | Out-Null
$run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name DazeMediaOsd -ErrorAction SilentlyContinue).DazeMediaOsd
Write-Host "Installed and started: $exe"
Write-Host "Start with Windows: $(if ($run) { $run } else { 'off - turn it on in Settings (tray icon)' })"
