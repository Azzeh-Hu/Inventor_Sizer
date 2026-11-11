@echo off
rem Starts Inventor Sizer (Excel version) with the PowerShell that ships with Windows (no installs needed).
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0InventorSizer_excel.ps1"
if errorlevel 1 pause
