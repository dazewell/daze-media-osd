# Gives a published release's downloads their public names (D-047): Surtitle-<version>-Setup.exe and
# Surtitle-<version>-Portable.zip instead of Velopack's <packId>-win-*. vpk names its files after the packId
# and has no option for it; the packId can't change (D-044). The update feed (*.nupkg, releases.win.json, RELEASES)
# keeps Velopack's names: installed copies look those up.
# The release workflow runs this right after publishing, so a failure here leaves a complete release with the
# old names, never a missing one. By hand, for an existing release:
#   gh release download v<v> -p '*-win-*' -D <dir>; pwsh scripts/name-release-downloads.ps1 -Version <v> -Dir <dir>
param([Parameter(Mandatory)][string]$Version,
    [string]$Dir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/releases'),
    [string]$Repo, [switch]$KeepOld)
$ErrorActionPreference = 'Stop'
$repoArgs = if ($Repo) { '-R', $Repo } else { @() }
$names = [ordered]@{
    'DazeMediaOsdApp-win-Setup.exe'    = "Surtitle-$Version-Setup.exe"
    'DazeMediaOsdApp-win-Portable.zip' = "Surtitle-$Version-Portable.zip"
}
$staging = Join-Path ([IO.Path]::GetTempPath()) "surtitle-release-$Version"
New-Item -ItemType Directory -Force $staging | Out-Null
foreach ($old in $names.Keys) {
    $source = Join-Path $Dir $old
    if (-not (Test-Path $source)) { throw "No $old in $Dir." }
    $new = Join-Path $staging $names[$old]
    Copy-Item $source $new -Force
    gh release upload "v$Version" $new --clobber @repoArgs
    if ($LASTEXITCODE -ne 0) { throw "Couldn't upload $($names[$old]) to v$Version." }
    Write-Host "Uploaded $($names[$old])"
}
# Only once both new files are up, so the release never lacks an installer.
if (-not $KeepOld) {
    foreach ($old in $names.Keys) {
        gh release delete-asset "v$Version" $old --yes @repoArgs
        if ($LASTEXITCODE -ne 0) { throw "Couldn't remove $old from v$Version." }
        Write-Host "Removed $old"
    }
}
Remove-Item $staging -Recurse -Force
