<#
.SYNOPSIS
    Builds DWG2IFC and deploys it into the AutoCAD / Plant 3D ApplicationPlugins folders.

.DESCRIPTION
    Supports AutoCAD 2025, AutoCAD 2026, AutoCAD Plant 3D 2025, and AutoCAD Plant 3D 2026 (.NET 8.0).
    Deploys the autoloader bundle to %APPDATA%\Autodesk\ApplicationPlugins\DWG2IFC.bundle.

.PARAMETER SkipBuild
    Deploy the existing bundle without rebuilding.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "==> Проверка окружения AutoCAD / Plant 3D" -ForegroundColor Cyan

$running = Get-Process acad, accoreconsole -ErrorAction SilentlyContinue
if ($running) {
    Write-Warning "AutoCAD или AcCoreConsole запущен ($($running.Count) процессов). Закройте их перед установкой."
    $ans = Read-Host "Завершить процессы автоматически? (y/n)"
    if ($ans -eq 'y') {
        $running | Stop-Process -Force
    } else {
        exit 1
    }
}

if (-not $SkipBuild) {
    Write-Host "==> Сборка DWG2IFC (.NET 8.0)..." -ForegroundColor Cyan
    & dotnet build (Join-Path $root 'src\DWG2IFC\DWG2IFC.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Сборка плагина завершилась с ошибкой $LASTEXITCODE" }

    # Packaging bundle
    $bundleDir = Join-Path $root 'bin\Release\bundle'
    if (-not (Test-Path $bundleDir)) { New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null }
    Copy-Item (Join-Path $root 'bundle\DWG2IFC.bundle\PackageContents.xml') (Join-Path $bundleDir 'PackageContents.xml') -Force
    if (Test-Path (Join-Path $root 'bundle\DWG2IFC.bundle\Contents\icon32.png')) {
        $contentsDir = Join-Path $bundleDir 'Contents'
        if (-not (Test-Path $contentsDir)) { New-Item -ItemType Directory -Path $contentsDir -Force | Out-Null }
        Copy-Item (Join-Path $root 'bundle\DWG2IFC.bundle\Contents\icon32.png') (Join-Path $contentsDir 'icon32.png') -Force
    }
}

# Target directories
$destUser = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\DWG2IFC.bundle'
$destSys  = Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins\DWG2IFC.bundle'
$bundleSource = Join-Path $root 'bin\Release\bundle'

foreach ($dest in @($destUser, $destSys)) {
    try {
        if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
        Copy-Item -Path "$bundleSource\*" -Destination $dest -Recurse -Force
        Write-Host "Развёрнуто в: $dest" -ForegroundColor Green
    } catch {
        Write-Host "Пропущено $dest (нет прав записи): $_" -ForegroundColor Yellow
    }
}

# Clean legacy bundle
foreach ($legacy in @("$env:APPDATA\Autodesk\ApplicationPlugins\Plant3D_IfcExporter.bundle", "$env:ProgramData\Autodesk\ApplicationPlugins\Plant3D_IfcExporter.bundle")) {
    if (Test-Path $legacy) { try { Remove-Item -Recurse -Force $legacy } catch { } }
}

Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host "УСТАНОВКА DWG2IFC ЗАВЕРШЕНА!" -ForegroundColor Green
Write-Host "Запустите AutoCAD или Plant 3D 2025/2026." -ForegroundColor White
Write-Host "Вкладка на ленте: DWG2IFC" -ForegroundColor White
Write-Host "Команды: DWG2IFC, IFCEXPORT, PLANTTOIFC, IFCRIBBON" -ForegroundColor White
Write-Host "========================================================" -ForegroundColor Green
