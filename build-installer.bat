@echo off
setlocal
echo ========================================================
echo   Taraz Invoice - One-Click Release Installer Builder
echo ========================================================
echo.

echo [1/3] Stopping any running TarazInvoice instances...
powershell -NoProfile -Command "Stop-Process -Name 'TarazInvoice' -Force -ErrorAction SilentlyContinue"

echo [2/3] Publishing Release build (win-x64 self-contained)...
dotnet publish "%~dp0TarazInvoice.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0publish\app"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] dotnet publish failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [3/3] Compiling Setup Installer with Inno Setup...
set "ISCC="

if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if "%ISCC%"=="" if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if "%ISCC%"=="" if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if "%ISCC%"=="" if exist "%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe"
if "%ISCC%"=="" if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if "%ISCC%"=="" if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"

if "%ISCC%"=="" (
    for /f "delims=" %%I in ('where iscc.exe 2^>nul') do set "ISCC=%%I"
)

if "%ISCC%"=="" (
    echo.
    echo [ERROR] Inno Setup compiler ISCC.exe not found!
    echo Please install Inno Setup 6 by running:
    echo   winget install JRSoftware.InnoSetup
    echo or download from: https://jrsoftware.org/isdl.php
    pause
    exit /b 1
)

echo Using Inno Setup: "%ISCC%"
"%ISCC%" "%~dp0installer.iss"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Installer compilation failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo   SUCCESS! Installer ready in:
echo   %~dp0publish
echo ========================================================
explorer.exe "%~dp0publish"
exit /b 0
