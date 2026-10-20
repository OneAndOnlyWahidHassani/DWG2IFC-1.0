@echo off
setlocal
cd /d "%~dp0"

echo [1/3] Compiling DWG2IFC Plugin (.NET 8.0)...
dotnet build src\DWG2IFC\DWG2IFC.csproj -c Release --nologo
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Plugin build failed!
    exit /b %ERRORLEVEL%
)

echo [2/3] Packaging Autodesk Autoloader Bundle...
if not exist "bin\Release\bundle" mkdir "bin\Release\bundle"
copy /y "bundle\DWG2IFC.bundle\PackageContents.xml" "bin\Release\bundle\PackageContents.xml" >nul
if not exist "bin\Release\bundle\Contents" mkdir "bin\Release\bundle\Contents"
if exist "bundle\DWG2IFC.bundle\Contents\icon32.png" (
    copy /y "bundle\DWG2IFC.bundle\Contents\icon32.png" "bin\Release\bundle\Contents\icon32.png" >nul
)
if exist "bin\Release\bundle.zip" del /f /q "bin\Release\bundle.zip" >nul
powershell -NoProfile -Command "Compress-Archive -Path 'bin\Release\bundle\*' -DestinationPath 'bin\Release\bundle.zip' -Force"

echo [3/3] Compiling Standalone Setup Executable (C# WPF/CLI)...
dotnet build src\DWG2IFC.Setup\DWG2IFC.Setup.csproj -c Release --nologo
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Setup build failed!
    exit /b %ERRORLEVEL%
)

copy /y "bin\Release\DWG2IFC-Setup.exe" "DWG2IFC-Setup.exe" >nul
copy /y "bin\Release\DWG2IFC-Setup.dll" "DWG2IFC-Setup.dll" >nul
copy /y "bin\Release\DWG2IFC-Setup.runtimeconfig.json" "DWG2IFC-Setup.runtimeconfig.json" >nul

echo.
echo ========================================================
echo BUILD SUCCESSFUL!
echo Output Installer: DWG2IFC-Setup.exe
echo ========================================================
