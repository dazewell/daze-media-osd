# Daily-driver install from this checkout: the same Velopack package a GitHub Release ships (D-029), installed
# with its Setup.exe, so a local build and a release share one folder (%LOCALAPPDATA%\DazeMediaOsdApp), one
# Apps entry and one uninstall. Autostart is the app's own "Start with Windows" toggle (D-015).
# Example: pwsh scripts/install.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packages = Join-Path $root 'artifacts/install'
$installDir = Join-Path $env:LOCALAPPDATA 'DazeMediaOsdApp'
$exe = Join-Path $installDir 'current/DazeMediaOsd.exe'
# Where install.ps1 put the app before D-029.
$oldDir = Join-Path $env:LOCALAPPDATA 'Programs/DazeMediaOsd'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

# Pack while the installed copy keeps running: a failed build changes nothing. A fresh folder each time, so
# vpk never sees an earlier local package of the same version.
if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }
& (Join-Path $PSScriptRoot 'pack.ps1') -Output $packages
$setup = Get-ChildItem $packages -Filter '*-Setup.exe' | Select-Object -First 1
if (-not $setup) { throw "No Setup.exe in $packages." }

# Stop gracefully so the launcher restores the native flyout (D-006). Setup would force-kill both processes
# instead, and only its install hook would restore the flyout.
& (Join-Path $PSScriptRoot 'stop.ps1')

# Over an existing install, Setup keeps the old folder until the new one is in place (rollback).
$process = Start-Process $setup.FullName -ArgumentList '--silent' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Setup failed ($($process.ExitCode)); see $env:LOCALAPPDATA\velopack\velopack.log." }

# One-time move from the pre-D-029 folder: keep "Start with Windows" (and a Task Manager "disabled", which
# lives in StartupApproved and is left alone) by re-pointing our Run value, then delete the old copy.
$run = (Get-ItemProperty $runKey -Name DazeMediaOsd -ErrorAction SilentlyContinue).DazeMediaOsd
if ($run -and $run.Trim('"').StartsWith($oldDir, [StringComparison]::OrdinalIgnoreCase)) {
    Set-ItemProperty $runKey -Name DazeMediaOsd -Value "`"$exe`""
    Write-Host "Start with Windows moved from $oldDir to the new install."
}
foreach ($dir in $oldDir, "$oldDir.new", "$oldDir.old") {
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $dir) { Write-Warning "Couldn't remove $dir; delete it by hand." } else { Write-Host "Removed old copy $dir" }
    }
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) | Out-Null
$run = (Get-ItemProperty $runKey -Name DazeMediaOsd -ErrorAction SilentlyContinue).DazeMediaOsd
Write-Host "Installed and started: $exe"
Write-Host "Start with Windows: $(if ($run) { $run } else { 'off - turn it on in Settings (tray icon)' })"
