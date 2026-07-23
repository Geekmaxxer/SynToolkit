@echo off
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
	echo This resets the Windows TCP/IP and Winsock configuration and flushes DNS.
	echo Static IP or custom DNS settings may need to be reapplied, and network
	echo access may be interrupted until Windows is restarted.
	echo No network device or driver will be removed.
	"%SystemRoot%\System32\choice.exe" /c YN /n /m "Continue? [Y/N] "
	if errorlevel 2 exit /b 0
)

set /a SYNTOOLKIT_FAILURES=0

echo Flushing the DNS resolver cache...
"%SystemRoot%\System32\ipconfig.exe" /flushdns >nul 2>&1
if errorlevel 1 (
	echo Failed to flush the DNS resolver cache.
	set /a SYNTOOLKIT_FAILURES+=1
)

echo Resetting the Winsock catalog...
"%SystemRoot%\System32\netsh.exe" winsock reset >nul 2>&1
if errorlevel 1 (
	echo Failed to reset the Winsock catalog.
	set /a SYNTOOLKIT_FAILURES+=1
)

echo Resetting IPv4 configuration...
"%SystemRoot%\System32\netsh.exe" interface ipv4 reset >nul 2>&1
if errorlevel 1 (
	echo Failed to reset IPv4 configuration.
	set /a SYNTOOLKIT_FAILURES+=1
)

echo Resetting IPv6 configuration...
"%SystemRoot%\System32\netsh.exe" interface ipv6 reset >nul 2>&1
if errorlevel 1 (
	echo Failed to reset IPv6 configuration.
	set /a SYNTOOLKIT_FAILURES+=1
)

if not "%SYNTOOLKIT_FAILURES%"=="0" (
	echo Network reset completed with %SYNTOOLKIT_FAILURES% failed step^(s^).
	exit /b 20
)

echo Network settings were reset without removing any network devices.
echo Restart Windows to complete the reset.
if defined SYNTOOLKIT_SILENT exit /b 0
pause
exit /b 0

:Elevate
if defined SYNTOOLKIT_SILENT exit /b 5
set "___args="%~f0""
echo Administrator privileges are required.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -Command "$p=Start-Process -Verb RunAs -FilePath $env:ComSpec -ArgumentList '/d','/c',$env:___args -Wait -PassThru -ErrorAction Stop; exit $p.ExitCode"
set "SYNTOOLKIT_ELEVATED_EXIT=%errorlevel%"
if not "%SYNTOOLKIT_ELEVATED_EXIT%"=="0" echo The elevated repair did not complete ^(exit code %SYNTOOLKIT_ELEVATED_EXIT%^).
exit /b %SYNTOOLKIT_ELEVATED_EXIT%
