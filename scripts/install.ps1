# Daily-driver install: publish Release (self-contained, D-003) to a fixed folder outside any checkout and
# start it from there. Autostart is the app's own "Start with Windows" toggle, which registers the copy it
# runs from (D-015): turn it on once from the installed copy and later installs keep it.
# Example: pwsh scripts/install.ps1
param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs/DazeMediaOsd'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $Destination 'DazeMediaOsd.exe'

# Publish to a staging folder while the old copy keeps running: a failed publish changes nothing.
$staging = "$Destination.new"
$backup = "$Destination.old"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
# A stale backup must go before the app is stopped: failing here leaves it running.
if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
dotnet publish (Join-Path $root 'src/DazeMediaOsd.App') -c Release -r win-x64 -o $staging -v q -nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed ($LASTEXITCODE); installed copy unchanged and still running." }

# Any running copy (Debug or installed) locks files or would block the new one's single-instance mutex.
& (Join-Path $PSScriptRoot 'stop.ps1')

# Swap with rollback: the old copy is only deleted once the new one is in place.
try {
    if (Test-Path $Destination) { Move-Item $Destination $backup }
    Move-Item $staging $Destination
} catch {
    if (-not (Test-Path $Destination) -and (Test-Path $backup)) { Move-Item $backup $Destination }
    if (Test-Path $exe) { Start-Process -FilePath $exe -WorkingDirectory $Destination | Out-Null }
    throw "Install failed, previous copy restored and restarted: $_"
}
# Leftover backup is harmless (the next run removes it): never let it keep the new copy from starting.
if (Test-Path $backup) {
    Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $backup) { Write-Warning "Couldn't remove $backup; the next install retries." }
}

Start-Process -FilePath $exe -WorkingDirectory $Destination | Out-Null
$run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name DazeMediaOsd -ErrorAction SilentlyContinue).DazeMediaOsd
Write-Host "Installed and started: $exe"
Write-Host "Start with Windows: $(if ($run) { $run } else { 'off - turn it on in Settings (tray icon)' })"
