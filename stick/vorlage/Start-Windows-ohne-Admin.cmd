@echo off
rem Wie Start-Windows.cmd, aber ohne Administratorrechte (einige Pruefungen werden dann uebersprungen).
chcp 65001 >nul
"%~dp0bin\win-x64\Diagnostiker.exe"
echo.
pause
