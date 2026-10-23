# Stops DazeMediaOsd safely (D-006): kill only the `--app` process, then the launcher restores the
# native volume flyout and exits by itself. Killing both at once would leave the native flyout minimized.
$apps = Get-CimInstance Win32_Process -Filter "Name='DazeMediaOsd.exe'"
$apps | Where-Object CommandLine -like '*--app*' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

$launchers = $apps | Where-Object CommandLine -notlike '*--app*' | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
$launchers | ForEach-Object { if (-not $_.WaitForExit(5000)) { Write-Warning "Launcher $($_.Id) did not exit; killing it."; $_.Kill() } }
