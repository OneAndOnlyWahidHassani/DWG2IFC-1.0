# One-click build script for DWG2IFC
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "[1/3] Compiling DWG2IFC Plugin (.NET 8.0)..." -ForegroundColor Cyan
dotnet build src\DWG2IFC\DWG2IFC.csproj -c Release --nologo

Write-Host "[2/3] Packaging Autodesk Autoloader Bundle..." -ForegroundColor Cyan
Copy-Item "bundle\DWG2IFC.bundle\PackageContents.xml" "bin\Release\bundle\PackageContents.xml" -Force
if (Test-Path "bundle\DWG2IFC.bundle\Contents\icon32.png") {
    Copy-Item "bundle\DWG2IFC.bundle\Contents\icon32.png" "bin\Release\bundle\Contents\icon32.png" -Force
}
if (Test-Path "bin\Release\bundle.zip") {
    Remove-Item "bin\Release\bundle.zip" -Force
}
Compress-Archive -Path "bin\Release\bundle\*" -DestinationPath "bin\Release\bundle.zip" -Force

Write-Host "[3/3] Compiling Standalone Setup Executable (C# WPF/CLI)..." -ForegroundColor Cyan
dotnet build src\DWG2IFC.Setup\DWG2IFC.Setup.csproj -c Release --nologo

Copy-Item "bin\Release\DWG2IFC-Setup.exe" "DWG2IFC-Setup.exe" -Force
Copy-Item "bin\Release\DWG2IFC-Setup.dll" "DWG2IFC-Setup.dll" -Force
Copy-Item "bin\Release\DWG2IFC-Setup.runtimeconfig.json" "DWG2IFC-Setup.runtimeconfig.json" -Force

# Deploy bundle to user and system ApplicationPlugins directories
$appDataPlugin = "$env:APPDATA\Autodesk\ApplicationPlugins\DWG2IFC.bundle"
$progDataPlugin = "$env:ProgramData\Autodesk\ApplicationPlugins\DWG2IFC.bundle"

foreach ($dest in @($appDataPlugin, $progDataPlugin)) {
    try {
        if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
        Copy-Item -Path "bin\Release\bundle\*" -Destination $dest -Recurse -Force
        Write-Host "Deployed bundle to: $dest" -ForegroundColor DarkGreen
    } catch {
        Write-Host "Could not deploy to ${dest}: $_" -ForegroundColor Yellow
    }
}

# Clean legacy bundle paths
foreach ($legacy in @("$env:APPDATA\Autodesk\ApplicationPlugins\Plant3D_IfcExporter.bundle", "$env:ProgramData\Autodesk\ApplicationPlugins\Plant3D_IfcExporter.bundle")) {
    if (Test-Path $legacy) { try { Remove-Item -Recurse -Force $legacy } catch { } }
}

# Create / refresh Desktop shortcut
try {
    $ws = New-Object -ComObject WScript.Shell
    $desktopPath = [Environment]::GetFolderPath('Desktop')
    $sc = $ws.CreateShortcut("$desktopPath\DWG2IFC.lnk")
    $sc.TargetPath = "$PSScriptRoot\DWG2IFC-Setup.exe"
    $sc.WorkingDirectory = "$PSScriptRoot"
    $sc.IconLocation = "$PSScriptRoot\app.ico,0"
    $sc.Description = 'DWG2IFC - AutoCAD and Plant 3D IFC Exporter (baidurovlabs.ru)'
    $sc.Save()
    Write-Host "Created Desktop shortcut: $desktopPath\DWG2IFC.lnk" -ForegroundColor DarkGreen
} catch {
    Write-Host "Shortcut creation note: $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host "BUILD SUCCESSFUL!" -ForegroundColor Green
Write-Host "Installer executable ready: DWG2IFC-Setup.exe" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
