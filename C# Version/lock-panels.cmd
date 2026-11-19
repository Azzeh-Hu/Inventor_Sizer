@echo off
rem Locks the panel files (FP / TP / RP masters, standards\FP|TP|RP.json, MODELS\) in panels.lock.
rem   lock-panels.cmd          lock them as they are now (also: accept a change made on purpose)
rem   lock-panels.cmd /unlock  remove the lock to change panel files with the app (Set up master ...)
rem Commit panels.lock together with the panel files.
if not exist "%~dp0InventorSizer.exe" (
  echo InventorSizer.exe is not built yet. Run build.cmd first.
  pause
  exit /b 1
)
set "ARG=/lock"
if /i "%~1"=="/unlock" set "ARG=/unlock"
start "" /wait "%~dp0InventorSizer.exe" %ARG%
