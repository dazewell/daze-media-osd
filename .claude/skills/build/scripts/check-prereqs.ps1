# Checks the local machine can build daze-media-osd. Read-only: prints problems and fixes, installs nothing.
$ok = $true

$sdks = @(dotnet --list-sdks 2>$null)
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    Write-Host "MISSING  .NET 10 SDK (found: $($sdks -join ', '))" -ForegroundColor Red
    Write-Host "         fix: winget install Microsoft.DotNet.SDK.10"
    $ok = $false
} else {
    Write-Host "OK       .NET 10 SDK"
}

$build = [System.Environment]::OSVersion.Version.Build
if ($build -lt 26100) {
    Write-Host "UNSUPPORTED  Windows build $build; minimum is 26100 (Win11 24H2, D-004)" -ForegroundColor Red
    $ok = $false
} else {
    Write-Host "OK       Windows build $build"
}

$arch = $env:PROCESSOR_ARCHITECTURE
Write-Host "INFO     CPU architecture $arch (publish RID: $(if ($arch -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }))"

if ($ok) { exit 0 } else { exit 1 }
