@echo off
REM Install NC_TestRibbon + NcBootstrap, then start nanoCAD twice
REM (first pass warms ribbon cache, second pass leaves nanoCAD open).
REM Path to nCad.exe is resolved by Install-NcTestRibbon.ps1 (ASCII-only).
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-NcTestRibbon.ps1"
pause
