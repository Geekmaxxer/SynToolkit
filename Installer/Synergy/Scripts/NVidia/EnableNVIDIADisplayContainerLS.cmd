@echo off
setlocal
fltmc.exe >nul 2>&1 || exit /b 1
sc.exe query NVDisplay.ContainerLocalSystem >nul 2>&1 || exit /b 2
set "stateScript=%~dp0NVIDIADisplayContainerState.ps1"
if not exist "%stateScript%" exit /b 3
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%stateScript%" -Action Enable
exit /b %errorlevel%
