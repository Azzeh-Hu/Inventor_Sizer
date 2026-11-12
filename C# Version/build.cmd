@echo off
rem Builds InventorSizer.exe. Double-click, or run from cmd / PowerShell.
rem Uses the .NET SDK installed for your user (%LOCALAPPDATA%\Microsoft\dotnet) when there is one.
setlocal
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
cd /d "%~dp0"
tasklist /FI "IMAGENAME eq InventorSizer.exe" | find /I "InventorSizer.exe" >nul
if not errorlevel 1 (
  echo Inventor Sizer is running. Close it first, then run this again.
  pause
  exit /b 1
)
dotnet build src\InventorSizer.csproj -c Release -nologo -v minimal
if errorlevel 1 (
  echo.
  echo BUILD FAILED - the exe was not replaced.
  pause
  exit /b 1
)
echo.
echo Built InventorSizer.exe - the window title shows the build time when you start it.
pause
