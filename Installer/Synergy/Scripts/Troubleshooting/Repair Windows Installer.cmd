@echo off
rem SynToolkit Windows Installer registration and service repair
setlocal EnableExtensions

set "SYNTOOLKIT_SILENT="
if /i "%~1"=="/silent" set "SYNTOOLKIT_SILENT=1"
if not "%~1"=="" if not defined SYNTOOLKIT_SILENT (
	echo Unsupported argument: %~1
	exit /b 2
)

"%SystemRoot%\System32\fltmc.exe" >nul 2>&1
if errorlevel 1 goto :Elevate

if not defined SYNTOOLKIT_SILENT (
	echo This repair re-registers Windows Installer and restores its service to
	echo the standard Manual startup mode. It does not change folder permissions
	echo or remove temporary files. Close any installers before continuing.
	"%SystemRoot%\System32\choice.exe" /c YN /n /m "Continue? [Y/N] "
	if errorlevel 2 exit /b 0
)

echo Checking the Windows Installer service...
"%SystemRoot%\System32\sc.exe" query msiserver >nul 2>&1
if errorlevel 1 (
	echo The Windows Installer service is unavailable.
	exit /b 10
)

"%SystemRoot%\System32\sc.exe" config msiserver start= demand >nul 2>&1
if errorlevel 1 (
	echo Failed to restore the Windows Installer service startup mode.
	exit /b 11
)

echo Re-registering native Windows Installer...
call :RegisterInstaller "%SystemRoot%\System32\msiexec.exe"
if errorlevel 1 exit /b 12

if exist "%SystemRoot%\SysWOW64\msiexec.exe" (
	echo Re-registering 32-bit Windows Installer...
	call :RegisterInstaller "%SystemRoot%\SysWOW64\msiexec.exe"
	if errorlevel 1 exit /b 13
)

echo Starting the Windows Installer service...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -Command "$service=Get-Service -Name 'msiserver' -ErrorAction Stop; if ($service.Status -ne 'Running') { Start-Service -Name 'msiserver' -ErrorAction Stop; $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(15)) }" >nul 2>&1
if errorlevel 1 (
	echo Windows Installer was registered, but its service could not be started.
	exit /b 14
)

echo Windows Installer registration and service repair completed.
if defined SYNTOOLKIT_SILENT exit /b 0
pause
exit /b 0

:RegisterInstaller
if not exist "%~1" (
	echo Required Windows Installer executable not found: %~1
	exit /b 1
)

"%~1" /unregister >nul 2>&1
if errorlevel 1 (
	echo Failed to unregister: %~1
	exit /b 1
)

"%~1" /regserver >nul 2>&1
if errorlevel 1 (
	echo Failed to register: %~1
	exit /b 1
)
exit /b 0

:Elevate
if defined SYNTOOLKIT_SILENT exit /b 5
set "___args="%~f0""
echo Administrator privileges are required.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -Command "$p=Start-Process -Verb RunAs -FilePath $env:ComSpec -ArgumentList '/d','/c',$env:___args -Wait -PassThru -ErrorAction Stop; exit $p.ExitCode"
set "SYNTOOLKIT_ELEVATED_EXIT=%errorlevel%"
if not "%SYNTOOLKIT_ELEVATED_EXIT%"=="0" echo The elevated repair did not complete ^(exit code %SYNTOOLKIT_ELEVATED_EXIT%^).
exit /b %SYNTOOLKIT_ELEVATED_EXIT%
