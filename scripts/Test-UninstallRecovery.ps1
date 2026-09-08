[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$removeScript = Join-Path $PSScriptRoot 'Remove-Elevated.ps1'
$temporaryRoot = Join-Path $env:TEMP ("VictusModeSwitch-UninstallTest-{0}" -f [Guid]::NewGuid())
$resultPath = Join-Path $temporaryRoot 'elevated-remove-result.json'
$backupPath = Join-Path $temporaryRoot 'omen-task-backup.json'
$originalProgramFiles = $env:ProgramFiles
$global:VictusModeSwitchTestEnabledTasks = @()
$global:VictusModeSwitchTestDisabledTasks = @()
$global:VictusModeSwitchTestStartedTasks = @()

function Stop-ScheduledTask {
    [CmdletBinding()]
    param([string]$TaskName, [string]$TaskPath, $InputObject)
}

function Unregister-ScheduledTask {
    [CmdletBinding()]
    param([string]$TaskName, [string]$TaskPath, [switch]$Confirm, $InputObject)
}

function Get-Process {
    [CmdletBinding()]
    param([string[]]$Name)
}

function Stop-Process {
    [CmdletBinding()]
    param([switch]$Force, $InputObject)
}

function Get-ScheduledTask {
    [CmdletBinding()]
    param([string]$TaskName, [string]$TaskPath)

    [pscustomobject]@{
        TaskName = $TaskName
        TaskPath = $TaskPath
    }
}

function Enable-ScheduledTask {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TaskName, [string]$TaskPath)

    $global:VictusModeSwitchTestEnabledTasks += "$TaskPath$TaskName"
}

function Disable-ScheduledTask {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TaskName, [string]$TaskPath)

    $global:VictusModeSwitchTestDisabledTasks += "$TaskPath$TaskName"
}

function Start-ScheduledTask {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TaskName, [string]$TaskPath)

    $global:VictusModeSwitchTestStartedTasks += "$TaskPath$TaskName"
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    $env:ProgramFiles = Join-Path $temporaryRoot 'ProgramFiles'
    @(
        [ordered]@{ TaskName = 'OmenEnabledRunning'; TaskPath = '\'; Enabled = $true; Running = $true }
        [ordered]@{ TaskName = 'OmenEnabledStopped'; TaskPath = '\'; Enabled = $true; Running = $false }
        [ordered]@{ TaskName = 'OmenDisabled'; TaskPath = '\'; Enabled = $false; Running = $false }
    ) | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $backupPath -Encoding UTF8

    & $removeScript -ResultPath $resultPath
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (-not $result.Success) {
        throw "Removal simulation failed: $($result.Message)"
    }
    if ($global:VictusModeSwitchTestEnabledTasks.Count -ne 2) {
        throw "Expected 2 enabled OMEN tasks, got $($global:VictusModeSwitchTestEnabledTasks.Count)."
    }
    if ($global:VictusModeSwitchTestDisabledTasks.Count -ne 1) {
        throw "Expected 1 disabled OMEN task, got $($global:VictusModeSwitchTestDisabledTasks.Count)."
    }
    if ($global:VictusModeSwitchTestStartedTasks.Count -ne 1) {
        throw "Expected 1 restarted OMEN task, got $($global:VictusModeSwitchTestStartedTasks.Count)."
    }
    if (Test-Path -LiteralPath $backupPath) {
        throw 'The recovery file was not consumed after a successful simulation.'
    }

    Write-Output 'Windows PowerShell uninstall-recovery simulation passed.'
} finally {
    $env:ProgramFiles = $originalProgramFiles
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Variable -Name 'VictusModeSwitchTestEnabledTasks' -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable -Name 'VictusModeSwitchTestDisabledTasks' -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable -Name 'VictusModeSwitchTestStartedTasks' -Scope Global -ErrorAction SilentlyContinue
}
