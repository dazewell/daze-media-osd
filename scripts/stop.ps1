# Stops DazeMediaOsd safely (D-006): kill only the `--app` process, then the launcher restores the
# native volume flyout and exits by itself. Killing both at once would leave the native flyout minimized.
$apps = Get-CimInstance Win32_Process -Filter "Name='DazeMediaOsd.exe'"
$apps | Where-Object CommandLine -like '*--app*' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

foreach ($entry in $apps | Where-Object { $_.CommandLine -notlike '*--app*' -and $_.CommandLine -notlike '*--restore*' }) {
    $launcher = Get-Process -Id $entry.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $launcher -or $launcher.WaitForExit(5000)) { continue }
    # The launcher is the only restorer: restore explicitly before force-killing it.
    Write-Warning "Launcher $($launcher.Id) did not exit; restoring the native flyout, then killing it."
    & $entry.ExecutablePath --restore | Out-Null
    $launcher.Kill()
}
