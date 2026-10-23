# Dev loop: stop the running app safely, build, optionally test, start it again in the background.
# Examples: pwsh scripts/run.ps1            pwsh scripts/run.ps1 -Test            pwsh scripts/run.ps1 -NoBuild
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root 'DazeMediaOsd.slnx'
$exe = Join-Path $root "src/DazeMediaOsd.App/bin/$Configuration/net10.0-windows10.0.26100.0/win-x64/DazeMediaOsd.exe"
$log = Join-Path $env:LOCALAPPDATA 'DazeMediaOsd/logs/app.log'

# The running app locks its exe; stop.ps1 kills only `--app` so the launcher restores the flyout (D-006).
& (Join-Path $PSScriptRoot 'stop.ps1')

if (-not $NoBuild) {
    dotnet build $solution -c $Configuration -v q -nologo
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed ($LASTEXITCODE); app not started." }
}
if ($Test) {
    dotnet test --solution $solution -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { Write-Error "Tests failed ($LASTEXITCODE); app not started." }
}
if (-not (Test-Path $exe)) { Write-Error "$exe not found; run without -NoBuild." }

$logLines = if (Test-Path $log) { @(Get-Content $log).Count } else { 0 }
# Start the launcher (no args), not `dotnet run`: it returns at once and the app outlives this script.
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) | Out-Null

# Wait for the app's start line (or its failure) so a broken start is visible here, not only in the log.
$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 250
    # @() outside the if: assigning an if-expression unwraps a one-line array into a plain string.
    $new = @(if (Test-Path $log) { Get-Content $log | Select-Object -Skip $logLines })
} until (($new -match 'app started|ERROR|launcher: app exited').Count -gt 0 -or (Get-Date) -gt $deadline)
$new | ForEach-Object { Write-Host $_ }
if (($new -match 'app started').Count -eq 0) {
    Write-Error "App did not report a start within 15 s; see $log"
}
Write-Host "Running ($Configuration). Stop: pwsh scripts/stop.ps1 or tray -> Exit. Log: $log"
