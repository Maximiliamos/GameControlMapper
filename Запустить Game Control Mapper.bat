@echo off
setlocal

cd /d "%~dp0"

set "APP_DIR=%~dp0src\GameControlMapper\bin\Release\net8.0-windows"
set "APP_EXE=%APP_DIR%\GameControlMapper.exe"

tasklist /FI "IMAGENAME eq GameControlMapper.exe" /NH 2>nul | find /I "GameControlMapper.exe" >nul
if not errorlevel 1 (
    echo Game Control Mapper is already running.
    exit /b 0
)

if not exist "%APP_EXE%" (
    where dotnet >nul 2>&1
    if errorlevel 1 goto no_dotnet

    echo Release build not found. Building Game Control Mapper...
    dotnet build "GameControlMapper.sln" -c Release
    if errorlevel 1 goto build_failed
)

echo Starting Game Control Mapper...
start "" /D "%APP_DIR%" "%APP_EXE%"
if errorlevel 1 goto launch_failed
exit /b 0

:no_dotnet
echo.
echo .NET SDK 8 was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
pause
exit /b 1

:build_failed
echo.
echo Game Control Mapper build failed. Review the errors above.
pause
exit /b 1

:launch_failed
echo.
echo Failed to start "%APP_EXE%".
pause
exit /b 1
