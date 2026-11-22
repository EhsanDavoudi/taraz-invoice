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
set "ISCC=C:\Users\ehsan\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
    set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
if not exist "%ISCC%" (
    set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
)

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
