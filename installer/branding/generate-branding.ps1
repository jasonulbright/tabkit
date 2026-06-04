#requires -Version 5.1
<#
.SYNOPSIS
  Regenerate tabkit's branding assets from the vector master (icon.svg).

.DESCRIPTION
  icon.svg is the single source of truth for the 'tk' mark. This script rasterizes
  it with ImageMagick and composes the installer bitmaps, producing:

    icon-256.png            256x256 PNG  — rasterized mark (preview / docs)
    app.ico                 multi-res ICO (16,24,32,48,64,128,256) — app + installer icon
    installer-header.bmp    150x57  BMP3 — NSIS MUI page header (white bg)
    installer-sidebar.bmp   164x314 BMP3 — NSIS MUI welcome/finish panel (dark bg)

  Re-run after editing icon.svg, then rebuild the installer (installer/build.ps1).
  Requires ImageMagick 7+ (`magick`) on PATH.

  Palette (from Tabkit.Core/Output/HtmlOutput.cs):
    brand #46A0F2 -> brand-dim #1B57AD, dark bg #1b1e24 -> #0e1014, muted #9A9CA1.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $here
try {
    $magick = (Get-Command magick.exe -ErrorAction SilentlyContinue).Source
    if (-not $magick) { throw "ImageMagick (magick.exe) not found on PATH. Install from https://imagemagick.org" }

    $fontBold = 'C:/Windows/Fonts/segoeuib.ttf'
    $fontReg  = 'C:/Windows/Fonts/segoeui.ttf'
    foreach ($f in @($fontBold, $fontReg)) {
        if (-not (Test-Path $f)) { throw "Required font not found: $f" }
    }

    Write-Host "==> rasterize mark from icon.svg" -ForegroundColor Cyan
    & $magick -background none -density 192 icon.svg -resize 256x256 icon-256.png

    Write-Host "==> app.ico (multi-resolution)" -ForegroundColor Cyan
    & $magick icon-256.png -define icon:auto-resize=256,128,64,48,32,24,16 app.ico

    Write-Host "==> installer-header.bmp (150x57)" -ForegroundColor Cyan
    & $magick icon-256.png -resize 44x44 logo-44.png
    & $magick -size 150x57 xc:white `
        logo-44.png -gravity west -geometry +8+0 -composite `
        -font $fontBold -pointsize 22 -fill '#16181C' -gravity west -annotate +60+0 'tabkit' `
        BMP3:installer-header.bmp

    Write-Host "==> installer-sidebar.bmp (164x314)" -ForegroundColor Cyan
    & $magick -size 164x314 gradient:'#1b1e24'-'#0e1014' sidebar-bg.png
    & $magick icon-256.png -resize 96x96 logo-96.png
    & $magick sidebar-bg.png `
        logo-96.png -gravity north -geometry +0+44 -composite `
        -font $fontBold -pointsize 26 -fill white    -gravity north -annotate +0+150 'tabkit' `
        -font $fontReg  -pointsize 12 -fill '#9A9CA1' -gravity north -annotate +0+188 'static analysis' `
        -font $fontReg  -pointsize 12 -fill '#9A9CA1' -gravity north -annotate +0+206 'for Tableau workbooks' `
        BMP3:installer-sidebar.bmp

    Remove-Item logo-44.png, logo-96.png, sidebar-bg.png -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "==> done. Assets:" -ForegroundColor Green
    & $magick identify icon-256.png app.ico installer-header.bmp installer-sidebar.bmp
}
finally { Pop-Location }
