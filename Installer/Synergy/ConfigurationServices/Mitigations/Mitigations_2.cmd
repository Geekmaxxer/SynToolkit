@echo off
setlocal EnableExtensions
set "SYNTOOLKIT_SILENT="
if /i "%~1"=="/silent" set "SYNTOOLKIT_SILENT=1"

fltmc >nul 2>&1 || (
	if defined SYNTOOLKIT_SILENT exit /b 5
	set "___args="%~f0" %*"
	echo Administrator privileges are required.
	powershell.exe -NoProfile -Command "Start-Process -Verb RunAs -FilePath 'cmd.exe' -ArgumentList '/d','/c',$env:___args" >nul 2>&1
	exit /b %errorlevel%
)

rem Use the supported Exploit Protection interface.
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference='Stop'; Set-ProcessMitigation -System -Reset" >nul || exit /b 1

echo Finished, please reboot your device for changes to apply.
if defined SYNTOOLKIT_SILENT exit /b 0
pause
exit /b 0
