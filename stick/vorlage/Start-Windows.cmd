@echo off
rem Startet den Diagnostiker unter Windows mit Administratorrechten (wird per UAC erfragt).
chcp 65001 >nul
net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~dpnx0' -Verb RunAs"
    exit /b
)
"%~dp0bin\win-x64\Diagnostiker.exe"
echo.
pause
