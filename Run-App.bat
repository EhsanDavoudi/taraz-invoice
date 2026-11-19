@echo off
chcp 65001 > nul
echo ========================================================
echo   سامانه جامع صدور و مدیریت پیش‌فاکتور «تـراز»
echo ========================================================
echo در حال اجرای نرم‌افزار دسکتاپ تـراز...
if exist "%~dp0bin\Release\net9.0-windows\TarazInvoice.exe" (
    start "" "%~dp0bin\Release\net9.0-windows\TarazInvoice.exe"
) else (
    start "" "%~dp0bin\Debug\net9.0-windows\TarazInvoice.exe"
)
exit
