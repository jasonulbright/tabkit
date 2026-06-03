#requires -Version 5.1
<#
.SYNOPSIS
  Build the tabkit Windows installer (NSIS, per-user, framework-dependent).

.DESCRIPTION
  1. Publishes src/Tabkit.App as a framework-dependent win-x64 build into .\payload
     (requires the .NET 10 Desktop Runtime on the target machine — the installer
     checks for it). Native assets for ParquetSharp / Tableau HyperAPI are pulled
     in via the win-x64 RID.
  2. Measures the published footprint.
  3. Compiles tabkit.nsi with makensis, producing Tabkit-Setup-<version>.exe here.

.PARAMETER Version
  Version stamped on the setup .exe, registry, and output filename. Defaults to
  the ThisAssembly.Version constant in src/Tabkit.Core/Output/JsonOutput.cs.

.EXAMPLE
  pwsh ./installer/build.ps1
  pwsh ./installer/build.ps1 -Version 0.3.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [string]$Rid = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $here
$appProj = Join-Path $repo 'src\Tabkit.App\Tabkit.App.csproj'
$payload = Join-Path $here 'payload'

# --- Resolve version from the single source of truth if not supplied ---
if (-not $Version) {
    $jsonOut = Join-Path $repo 'src\Tabkit.Core\Output\JsonOutput.cs'
    $m = Select-String -Path $jsonOut -Pattern 'Version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"' |
         Select-Object -First 1
    if (-not $m) { throw "Could not infer version from $jsonOut — pass -Version explicitly." }
    $Version = $m.Matches[0].Groups[1].Value
}
Write-Host "==> tabkit installer build  (version $Version, $Configuration, $Rid)" -ForegroundColor Cyan

# --- Locate makensis ---
$makensis = (Get-Command makensis.exe -ErrorAction SilentlyContinue).Source
if (-not $makensis) {
    $candidate = Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'
    if (Test-Path $candidate) { $makensis = $candidate }
}
if (-not $makensis) { throw "makensis.exe not found. Install NSIS (https://nsis.sourceforge.io) or add it to PATH." }

# --- 1. Publish (framework-dependent) ---
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
Write-Host "==> dotnet publish -> $payload" -ForegroundColor Cyan
dotnet publish $appProj `
    -c $Configuration `
    -r $Rid `
    --self-contained false `
    -p:PublishSingleFile=false `
    -o $payload
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$exe = Join-Path $payload 'tabkit-app.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe not found after publish." }

# --- 2. Measure footprint (KB) ---
$sizeKb = [int][math]::Ceiling(
    ((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum) / 1KB)
Write-Host "==> payload size: $sizeKb KB" -ForegroundColor Cyan

# --- 3. Compile installer (run from $here so relative paths in the .nsi resolve) ---
Push-Location $here
try {
    & $makensis "/DVERSION=$Version" "/DAPP_SIZE_KB=$sizeKb" 'tabkit.nsi'
    if ($LASTEXITCODE -ne 0) { throw "makensis failed (exit $LASTEXITCODE)." }
}
finally { Pop-Location }

$setup = Join-Path $here "Tabkit-Setup-$Version.exe"
$setupKb = [int][math]::Ceiling((Get-Item $setup).Length / 1KB)
Write-Host ""
Write-Host "==> Done: $setup  ($setupKb KB)" -ForegroundColor Green
