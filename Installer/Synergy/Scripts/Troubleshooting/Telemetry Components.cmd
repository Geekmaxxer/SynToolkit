@echo off
title SynToolkit - Telemetry component status
echo Connected User Experiences and Telemetry service:
sc.exe query DiagTrack
echo.
echo Windows Error Reporting service:
sc.exe query WerSvc
echo.
echo Compatibility telemetry scheduled tasks:
schtasks.exe /query /tn "\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser" 2>nul
echo.
pause
exit /b 0
