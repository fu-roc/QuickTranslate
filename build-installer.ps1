<#
.SYNOPSIS
    Build the QuickTranslate Windows installer (an Inno Setup Setup.exe).

.DESCRIPTION
    Takes the single-file exe produced by publish.ps1 and wraps it in a real
    machine-wide installer: wizard UI (Simplified Chinese), Program Files as
    the install folder, all-users shortcuts and an HKLM uninstall entry.
    The generated Setup.exe requires elevation (UAC) to run.
    Requires Inno Setup 6.5+ (ISCC.exe), found via -ISCC, PATH, a normal install,
    or a portable copy under %LOCALAPPDATA%\Temp\QT-Packaging\IS.

    The installer intentionally does NOT register any autostart entry: an
    unsigned program writing HKCU\...\Run triggers Defender's
    Behavior:Win32/Persistence.A!ml detection. Use the app's Settings page.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
    powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -Exe .\artifacts\...\QuickTranslate.exe -Version 1.0.4
#>
[CmdletBinding()]
param(
    [string] $Exe,
    [string] $ISCC,
    # 留空时从 csproj 的 <Version> 读取，避免程序集版本与安装包版本各说各话
    [string] $Version = '',
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root 'artifacts'

if (-not $Version) {
    $csproj = Join-Path $root 'src\QuickTranslate\QuickTranslate.csproj'
    $versionMatch = Select-String -LiteralPath $csproj -Pattern '<Version>([^<]+)</Version>' |
        Select-Object -First 1
    if ($versionMatch) { $Version = $versionMatch.Matches[0].Groups[1].Value }
    else { $Version = '1.0.4' }
    Write-Host "Version : $Version (from csproj)"
}
if (-not $OutputDir) { $OutputDir = $artifacts }
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

if (-not $Exe) {
    $found = Get-ChildItem -LiteralPath $artifacts -Recurse -Filter 'QuickTranslate.exe' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $found) { throw "No published QuickTranslate.exe under $artifacts. Run publish.ps1 first." }
    $Exe = $found.FullName
}
if (-not (Test-Path -LiteralPath $Exe)) { throw "Source exe not found: $Exe" }

if (-not $ISCC) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $ISCC = $cmd.Source }
}
if (-not $ISCC) {
    $candidates = @(
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        (Join-Path $env:LOCALAPPDATA 'Temp\QT-Packaging\IS\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { $ISCC = $candidate; break }
    }
}
if (-not $ISCC -or -not (Test-Path -LiteralPath $ISCC)) {
    throw "ISCC.exe not found. Install Inno Setup 6.5+ from https://jrsoftware.org/isdl.php or pass -ISCC <path>."
}

$iss = Join-Path $root 'packaging\setup.iss'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# Quote only when needed: ISCC parses /Dname=value and /Opath as single tokens.
$exeDefine = if ($Exe -match '\s') { '/DQtExe="' + $Exe + '"' } else { '/DQtExe=' + $Exe }
$outArg    = if ($OutputDir -match '\s') { '/O"' + $OutputDir + '"' } else { '/O' + $OutputDir }
$isccArgs  = @($exeDefine, ('/DQtVersion=' + $Version), ('/DQtStamp=' + $stamp), $outArg, $iss)

Write-Host "ISCC    : $ISCC"
Write-Host "Exe     : $Exe"
& $ISCC @isccArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$name = "QuickTranslate-$Version-win-x64-setup-$stamp.exe"
$output = Join-Path $OutputDir $name
if (-not (Test-Path -LiteralPath $output)) { throw "Expected output not found: $output" }
$size = [math]::Round((Get-Item -LiteralPath $output).Length / 1MB, 2)
Write-Host ''
Write-Host "Installer : $output"
Write-Host "Size      : $size MB"
