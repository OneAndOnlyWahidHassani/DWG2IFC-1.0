<#
.SYNOPSIS
    Builds release artefacts: standalone installer and release ZIP archive.

.DESCRIPTION
    Produces in dist\:
      DWG2IFC-Setup.exe        installer with the plugin bundle embedded
      DWG2IFC-<version>.zip    the plugin bundle loose, plus LICENSE and README
      SHA256SUMS.txt           checksums of release artefacts

.PARAMETER Version
    Version stamped on the artefacts. Defaults to 1.0.0.

.EXAMPLE
    .\pack.ps1
    .\pack.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string] $Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

# 1. Clean dist
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# 2. Run build
Step "Сборка проекта DWG2IFC $Version..."
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "Сборка завершилась с ошибкой $LASTEXITCODE" }

# 3. Copy Setup to dist
Step "Копирование инсталлятора в dist\"
$setupSrc = Join-Path $root 'DWG2IFC-Setup.exe'
$setupDst = Join-Path $dist 'DWG2IFC-Setup.exe'
Copy-Item $setupSrc $setupDst -Force

# 4. Create ZIP archive
Step "Создание архива DWG2IFC-$Version.zip..."
$staging = Join-Path ([IO.Path]::GetTempPath()) ("DWG2IFC_pack_" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    # Bundle folder
    Copy-Item (Join-Path $root 'bin\Release\bundle') (Join-Path $staging 'DWG2IFC.bundle') -Recurse -Force
    # License & Readme
    Copy-Item (Join-Path $root 'LICENSE')   -Destination $staging -Force
    Copy-Item (Join-Path $root 'README.md') -Destination $staging -Force

    $zipDst = Join-Path $dist "DWG2IFC-$Version.zip"
    Compress-Archive -Path "$staging\*" -DestinationPath $zipDst -Force
} finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}

# 5. Calculate SHA256 checksums
Step "Calculating SHA256 sums..."
$sumsFile = Join-Path $dist 'SHA256SUMS.txt'
$lines = @()
Get-ChildItem -Path $dist -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$hash  $($_.Name)"
}
$lines | Out-File -FilePath $sumsFile -Encoding utf8

Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host "RELEASE PACKAGE DWG2IFC $Version READY!" -ForegroundColor Green
Write-Host "Files in dist:" -ForegroundColor White
Get-ChildItem $dist | ForEach-Object { Write-Host "  - $($_.Name) ($([math]::Round($_.Length/1MB, 2)) MB)" }
Write-Host "========================================================" -ForegroundColor Green
