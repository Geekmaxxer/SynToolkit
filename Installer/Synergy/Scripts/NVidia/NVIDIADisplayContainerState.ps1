param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Enable', 'Disable')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
$serviceName = 'NVDisplay.ContainerLocalSystem'
$serviceRegistryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$snapshotKey = 'HKLM:\SOFTWARE\SynToolkit\Services\NvidiaDisplayContainer'
$snapshotStartupName = 'PreviousStartupType'
$snapshotRunningName = 'PreviousWasRunning'

function Get-ContainerState {
    $service = Get-Service -Name $serviceName -ErrorAction Stop
    $startupType = (Get-ItemProperty -LiteralPath $serviceRegistryKey -Name Start -ErrorAction Stop).Start
    if ([int]$startupType -notin 0, 1, 2, 3, 4) {
        throw "Unsupported NVIDIA Display Container startup value '$startupType'."
    }

    [pscustomobject]@{
        StartupType = [int]$startupType
        WasRunning = [bool]($service.Status -eq 'Running')
    }
}

function Set-ContainerStartupType([int]$startupType) {
    $startToken = switch ($startupType) {
        0 { 'boot' }
        1 { 'system' }
        2 { 'auto' }
        3 { 'demand' }
        4 { 'disabled' }
        default { throw "Unsupported NVIDIA Display Container startup value '$startupType'." }
    }

    & sc.exe config $serviceName start= $startToken | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Windows could not set the NVIDIA Display Container startup type (sc.exe exit code $LASTEXITCODE)."
    }
}

function Set-ContainerState($state) {
    $service = Get-Service -Name $serviceName -ErrorAction Stop
    if ($state.WasRunning) {
        $startupBeforeStart = if ($state.StartupType -eq 4) { 3 } else { $state.StartupType }
        Set-ContainerStartupType $startupBeforeStart
        if ($service.Status -ne 'Running') {
            Start-Service -Name $serviceName -ErrorAction Stop
            $service = Get-Service -Name $serviceName -ErrorAction Stop
            $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        }
        if ($startupBeforeStart -ne $state.StartupType) {
            Set-ContainerStartupType $state.StartupType
        }
    }
    else {
        if ($service.Status -ne 'Stopped') {
            Stop-Service -Name $serviceName -ErrorAction Stop
            $service = Get-Service -Name $serviceName -ErrorAction Stop
            $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
        Set-ContainerStartupType $state.StartupType
    }
}

function Assert-ContainerState($expected) {
    $actual = Get-ContainerState
    if ($actual.StartupType -ne $expected.StartupType -or
        $actual.WasRunning -ne $expected.WasRunning) {
        throw 'The NVIDIA Display Container service did not retain its requested startup/running state.'
    }
}

function Read-Snapshot {
    $values = Get-ItemProperty -LiteralPath $snapshotKey -ErrorAction SilentlyContinue
    if ($null -eq $values) {
        return $null
    }

    $startupType = $values.$snapshotStartupName
    $wasRunning = $values.$snapshotRunningName
    if ($null -eq $startupType -or $null -eq $wasRunning -or
        [int]$startupType -notin 0, 1, 2, 3, 4 -or
        [int]$wasRunning -notin 0, 1) {
        return $null
    }

    [pscustomobject]@{
        StartupType = [int]$startupType
        WasRunning = [bool]([int]$wasRunning -eq 1)
    }
}

function Save-SnapshotIfMissing($state) {
    if ($null -ne (Read-Snapshot)) {
        return
    }

    New-Item -Path $snapshotKey -Force | Out-Null
    New-ItemProperty -LiteralPath $snapshotKey -Name $snapshotStartupName -Value $state.StartupType -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $snapshotKey -Name $snapshotRunningName -Value $(if ($state.WasRunning) { 1 } else { 0 }) -PropertyType DWord -Force | Out-Null
}

function Clear-Snapshot {
    Remove-ItemProperty -LiteralPath $snapshotKey -Name $snapshotStartupName -ErrorAction SilentlyContinue
    Remove-ItemProperty -LiteralPath $snapshotKey -Name $snapshotRunningName -ErrorAction SilentlyContinue
}

try {
    $originalState = Get-ContainerState
    if ($Action -eq 'Disable') {
        Save-SnapshotIfMissing $originalState
        $targetState = [pscustomobject]@{ StartupType = 4; WasRunning = $false }
        try {
            Set-ContainerState $targetState
            Assert-ContainerState $targetState
        }
        catch {
            try {
                Set-ContainerState $originalState
                Assert-ContainerState $originalState
            }
            catch {
                Write-Warning "NVIDIA Display Container rollback failed: $($_.Exception.Message)"
            }
            throw
        }
    }
    else {
        $targetState = Read-Snapshot
        if ($null -eq $targetState) {
            $targetState = [pscustomobject]@{ StartupType = 2; WasRunning = $true }
        }

        try {
            Set-ContainerState $targetState
            Assert-ContainerState $targetState
        }
        catch {
            try {
                Set-ContainerState $originalState
                Assert-ContainerState $originalState
            }
            catch {
                Write-Warning "NVIDIA Display Container rollback failed: $($_.Exception.Message)"
            }
            throw
        }

        try {
            Clear-Snapshot
        }
        catch {
            Write-Warning "The restored NVIDIA Display Container snapshot could not be cleared: $($_.Exception.Message)"
        }
    }

    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
