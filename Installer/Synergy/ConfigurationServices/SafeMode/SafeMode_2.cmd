@echo off
setlocal EnableExtensions
set "SYNTOOLKIT_SILENT="
if /i "%~1"=="/silent" set "SYNTOOLKIT_SILENT=1"

fltmc >nul 2>&1 || (
	if defined SYNTOOLKIT_SILENT exit /b 5
	set "___args="%~f0" %*"
	echo Administrator privileges are required.
	powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$p=Start-Process -Verb RunAs -FilePath $env:ComSpec -ArgumentList '/d','/c',$env:___args -Wait -PassThru -ErrorAction Stop; exit $p.ExitCode" || exit /b 1
	exit /b 0
)

bcdedit /set {current} safeboot network >nul || exit /b 1
bcdedit /set {current} safebootalternateshell no >nul || exit /b 1

echo Finished, please reboot your device for changes to apply.
if defined SYNTOOLKIT_SILENT exit /b 0
pause
exit /b 0
