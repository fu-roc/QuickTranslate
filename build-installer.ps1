<#
.SYNOPSIS
    Build the QuickTranslate Windows installer (an Inno Setup Setup.exe).

.DESCRIPTION
    Packages the single-file executable from publish.ps1 as a machine-wide installer.
    Requires Inno Setup 6.5+ on PATH, in Program Files, or passed through -ISCC.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -Exe .\artifacts\...\QuickTranslate.exe
#>
[CmdletBinding()]
param(
    [string] $Exe,
    [string] $ISCC,
    [string] $Version = '',
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

if (-not $Exe) { throw 'Pass the published QuickTranslate.exe via -Exe.' }
# ISCC resolves source and output paths from the installer definition directory.
$Exe = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Exe)
if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "Source exe not found: $Exe" }

if (-not $Version) {
    $csproj = Join-Path $root 'src\QuickTranslate\QuickTranslate.csproj'
    $Version = ([xml](Get-Content -LiteralPath $csproj -Raw -Encoding UTF8)).Project.PropertyGroup.Version
    if (-not $Version) { throw "No <Version> found in $csproj" }
}
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)

if (-not $ISCC) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $ISCC = $cmd.Source }
}
if (-not $ISCC) {
    $candidates = @(
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { $ISCC = $candidate; break }
    }
}
if (-not $ISCC -or -not (Test-Path -LiteralPath $ISCC)) {
    throw "ISCC.exe not found. Install Inno Setup 6.5+ from https://jrsoftware.org/isdl.php or pass -ISCC <path>."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$iss = Join-Path $root 'packaging\setup.iss'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# Quote only when needed: ISCC parses /Dname=value and /Opath as single tokens.
$exeDefine = if ($Exe -match '\s') { '/DQtExe="' + $Exe + '"' } else { '/DQtExe=' + $Exe }
$outArg    = if ($OutputDir -match '\s') { '/O"' + $OutputDir + '"' } else { '/O' + $OutputDir }
$isccArgs  = @($exeDefine, ('/DQtVersion=' + $Version), ('/DQtStamp=' + $stamp), $outArg, $iss)

Write-Host "ISCC    : $ISCC"
Write-Host "Exe     : $Exe"
Write-Host "Version : $Version"
& $ISCC @isccArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$name = "QuickTranslate-$Version-win-x64-setup-$stamp.exe"
$output = Join-Path $OutputDir $name
if (-not (Test-Path -LiteralPath $output)) { throw "Expected output not found: $output" }
$size = [math]::Round((Get-Item -LiteralPath $output).Length / 1MB, 2)
Write-Host ''
Write-Host "Installer : $output"
Write-Host "Size      : $size MB"
