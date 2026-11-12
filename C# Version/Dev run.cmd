@echo off
rem Starts Inventor Sizer as a dev run: also shows "Open master", "Set up master..." and
rem "Learn hardware holes". Users start InventorSizer.exe itself.
if not exist "%~dp0InventorSizer.exe" (
  echo InventorSizer.exe is not built yet. Run build.cmd first.
  pause
  exit /b 1
)
start "" "%~dp0InventorSizer.exe" /dev
