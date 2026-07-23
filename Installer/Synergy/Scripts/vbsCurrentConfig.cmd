@echo off
title SynToolkit - Virtualization-based security status
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$status = Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard -ErrorAction SilentlyContinue; if ($null -eq $status) { Write-Host 'VBS status is unavailable on this system.'; exit 1 }; $status | Select-Object VirtualizationBasedSecurityStatus,SecurityServicesConfigured,SecurityServicesRunning | Format-List"
echo.
pause
