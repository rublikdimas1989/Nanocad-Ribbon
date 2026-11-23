@echo off
cd /d "%~dp0"
echo === NC_TestRibbon INSTALL ===
echo Builds the plugin, installs files, pre-writes ribbon UI, then starts nanoCAD once.
echo On build/install failure nanoCAD is NOT started.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-NcTestRibbon.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo INSTALL FAILED ^(exit %ERR%^).
  echo Compilation / install errors are in the log above.
  echo nanoCAD was NOT started.
  pause
  exit /b %ERR%
)
echo INSTALL OK.
pause
exit /b 0
