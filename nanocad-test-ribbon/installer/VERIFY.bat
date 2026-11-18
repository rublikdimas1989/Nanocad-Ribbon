@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Verify-NcTestRibbon.ps1"
echo.
pause
