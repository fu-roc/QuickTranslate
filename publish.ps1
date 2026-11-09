<#
.SYNOPSIS
  Build the portable QuickTranslate zip (single-file exe + README).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\publish.ps1
  powershell -ExecutionPolicy Bypass -File .\publish.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [string]$OutputDir = (Join-Path $PSScriptRoot 'artifacts')
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\QuickTranslate\QuickTranslate.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "Project not found: $project" }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $fallback = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $fallback) { $dotnet = $fallback }
    else { throw '.NET 10 SDK not found. Install it or add dotnet to PATH.' }
}

$flavor = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
# Include the time so every run writes a brand-new artifact: no overwrite, no delete.
# Overwriting a same-day zip can fail when Defender or a file manager holds it open.
$name = "QuickTranslate-$(Get-Date -Format 'yyyyMMdd-HHmmss')-win-x64-$flavor"
$staging = Join-Path $OutputDir $name
$publishDir = Join-Path $staging 'QuickTranslate'

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
# Reuse the staging folder on re-runs; publish overwrites files in place.
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:IncludeAllContentForSelfExtract=true',
    '-p:DebugType=none',
    '-o', $publishDir
)

Write-Host "Publishing [$flavor] ..."
& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $publishDir

$zip = Join-Path $OutputDir "$name.zip"
# Each run uses a fresh timestamped name, so no -Force / no delete is needed
# (Compress-Archive -Force deletes the old file first, which some managed
# environments block with a safe-delete guard, and an in-use zip cannot be
# overwritten at all).
# Pass the files, not the folder: Windows PowerShell 5.1 writes backslash-separated
# entry names when archiving a directory, which breaks some unzip tools.
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal

$mb = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
Write-Host "OK: $zip ($mb MB)"
Write-Host "Needs .NET 10 Desktop Runtime on target: $(-not $SelfContained)"
