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
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)

$project = Join-Path $root 'src\QuickTranslate\QuickTranslate.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "Project not found: $project" }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $fallback = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $fallback) { $dotnet = $fallback }
    else { throw '.NET 10 SDK not found. Install it or add dotnet to PATH.' }
}

$flavor = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
$name = "QuickTranslate-$(Get-Date -Format 'yyyyMMdd-HHmmss')-win-x64-$flavor"
$staging = Join-Path $OutputDir $name
$publishDir = Join-Path $staging 'QuickTranslate'

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:IncludeAllContentForSelfExtract=true',
    '-p:DebugType=portable',
    '-o', $publishDir
)

Write-Host "Publishing [$flavor] ..."
& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $publishDir 'QuickTranslate.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published exe not found: $exe" }
$readme = Join-Path $publishDir 'README.md'
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $readme

$pdb = Join-Path $publishDir 'QuickTranslate.pdb'
if (Test-Path -LiteralPath $pdb -PathType Leaf) {
    Copy-Item -LiteralPath $pdb -Destination (Join-Path $OutputDir "$name.pdb")
}

$zip = Join-Path $OutputDir "$name.zip"
# Pass the files, not the folder: Windows PowerShell 5.1 writes backslash-separated
# entry names when archiving a directory, which breaks some unzip tools.
Compress-Archive -LiteralPath $exe,$readme -DestinationPath $zip -CompressionLevel Optimal

$mb = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
Write-Host "OK: $zip ($mb MB)"
Write-Host "Needs .NET 10 Desktop Runtime on target: $(-not $SelfContained)"
