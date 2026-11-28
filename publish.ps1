<#
.SYNOPSIS
  Build the Windows x64 QuickTranslate installers.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\publish.ps1
  powershell -ExecutionPolicy Bypass -File .\publish.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [string]$OutputDir = '',
    [string]$ISCC
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts\installers' }
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)

$project = Join-Path $root 'src\QuickTranslate\QuickTranslate.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "Project not found: $project" }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $fallback = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $fallback) { $dotnet = $fallback }
    else { throw '.NET 10 SDK not found. Install it or add dotnet to PATH.' }
}

$flavors = if ($SelfContained) { 'self-contained' } else { 'self-contained', 'runtime-required' }
$staging = Join-Path (Split-Path -Parent $project) 'obj\installer'
$buildInstaller = Join-Path $root 'build-installer.ps1'

foreach ($flavor in $flavors) {
    $includeRuntime = $flavor -eq 'self-contained'
    $publishDir = Join-Path $staging $flavor

    $publishArgs = @(
        'publish', $project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', $includeRuntime.ToString().ToLowerInvariant(),
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:IncludeAllContentForSelfExtract=true',
        '-o', $publishDir
    )

    Write-Host "Publishing [$flavor] ..."
    & $dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $exe = Join-Path $publishDir 'QuickTranslate.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published exe not found: $exe" }
    if (Test-Path -LiteralPath (Join-Path $publishDir 'QuickTranslate.Tesseract.dll')) {
        throw 'The published executable must include the Tesseract DLL.'
    }

    & $buildInstaller -Exe $exe -OutputDir $OutputDir -ISCC $ISCC -SelfContained:$includeRuntime
}
