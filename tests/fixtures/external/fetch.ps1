#requires -Version 5.1
<#
.SYNOPSIS
Populate tests/fixtures/external/ with workbooks from upstream sources.

.DESCRIPTION
Pulls .twb / .twbx / .tfl fixtures from:
  - aloth/tableau-book-resources    (CC BY 4.0, 12 .twbx + 1 .tfl)
  - tableau/server-client-python    (MIT, 2 fixtures)

These are NOT committed to the tabkit repo (see .gitignore). Re-run this
script after a fresh clone to repopulate the external corpus.

See ATTRIBUTION.md for license and attribution details.
#>

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$bookDir = Join-Path $here 'book'
$sccDir  = Join-Path $here 'server-client'
$null = New-Item -ItemType Directory -Force -Path $bookDir, $sccDir

function Get-GhContents {
    param([Parameter(Mandatory)][string]$Repo, [Parameter(Mandatory)][string]$Path)
    $url = "https://api.github.com/repos/$Repo/contents/$Path"
    Invoke-RestMethod -Uri $url -Headers @{ 'User-Agent' = 'tabkit-fetch' }
}

function Save-RawAsset {
    param(
        [Parameter(Mandatory)][string]$DownloadUrl,
        [Parameter(Mandatory)][string]$Destination
    )
    if ((Test-Path $Destination) -and -not $Force) {
        Write-Host "  skip  $($Destination | Split-Path -Leaf) (exists)"
        return
    }
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $Destination -UseBasicParsing | Out-Null
    $size = (Get-Item $Destination).Length
    Write-Host ("  get   {0,-50} {1,12:N0} bytes" -f ($Destination | Split-Path -Leaf), $size)
}

function Fetch-WorkbooksFromRepo {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][string]$DestDir,
        [string[]]$Extensions = @('.twb', '.twbx', '.tfl')
    )
    Write-Host "[$Repo : $BasePath]"
    $queue = @($BasePath)
    while ($queue.Count -gt 0) {
        $cur = $queue[0]
        $queue = $queue[1..($queue.Count - 1)]
        $items = Get-GhContents -Repo $Repo -Path $cur
        foreach ($item in $items) {
            if ($item.type -eq 'dir') {
                $queue += $item.path
                continue
            }
            $ext = [System.IO.Path]::GetExtension($item.name).ToLowerInvariant()
            if ($Extensions -notcontains $ext) { continue }
            $dest = Join-Path $DestDir $item.name
            Save-RawAsset -DownloadUrl $item.download_url -Destination $dest
        }
    }
}

Fetch-WorkbooksFromRepo `
    -Repo 'aloth/tableau-book-resources' `
    -BasePath '.' `
    -DestDir  $bookDir

Fetch-WorkbooksFromRepo `
    -Repo 'tableau/server-client-python' `
    -BasePath 'test/assets' `
    -DestDir  $sccDir

Write-Host ''
Write-Host 'Done. Corpus populated:'
Get-ChildItem -Path $bookDir, $sccDir -File | Group-Object Directory | ForEach-Object {
    Write-Host "  $($_.Name): $($_.Count) file(s)"
}
