@echo off
setlocal
cd /d "%~dp0"

fltmc >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator rights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)

taskkill /im NetWatch.exe /f >nul 2>&1

echo ==========================================
echo  Building NetWatch (Release, single exe)
echo ==========================================
dotnet publish "%~dp0NetWatch.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0publish"
if errorlevel 1 (
    echo.
    echo Build failed. Make sure NetWatch is closed and the .NET 9 SDK is installed.
    pause
    exit /b 1
)

echo.
echo Launching NetWatch...
start "" "%~dp0publish\NetWatch.exe"

endlocal
exit /b 0
