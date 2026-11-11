@echo off
rem Starts Inventor Sizer with the PowerShell that ships with Windows (no installs needed).
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0InventorSizer.ps1"
if errorlevel 1 pause
