[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BrokerScript,
    [Parameter(Mandatory)]
    [string]$BrokerLauncher,
    [Parameter(Mandatory)]
    [string]$ResultPath,
    [Parameter(Mandatory)]
    [string]$UserId,
    [switch]$PauseOmen
)

$ErrorActionPreference = 'Stop'
$taskName = 'Victus Mode Switch BIOS'
$dataDirectory = [System.IO.Path]::GetDirectoryName($ResultPath)
$omenTaskBackupPath = Join-Path $dataDirectory 'omen-task-backup.json'
$brokerInstallDirectory = Join-Path $env:ProgramFiles 'VictusModeSwitch\Broker'
$installedBrokerScript = Join-Path $brokerInstallDirectory 'BiosBroker.ps1'
$installedBrokerLauncher = Join-Path $brokerInstallDirectory 'BiosBroker.vbs'
$stagedBrokerScript = Join-Path $brokerInstallDirectory 'BiosBroker.staged.ps1'
$stagedBrokerLauncher = Join-Path $brokerInstallDirectory 'BiosBroker.staged.vbs'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$windowsScriptHost = Join-Path $env:SystemRoot 'System32\wscript.exe'

try {
    if (-not (Test-Path -LiteralPath $BrokerScript -PathType Leaf)) {
        throw "BIOS broker script was not found at '$BrokerScript'."
    }
    if (-not (Test-Path -LiteralPath $BrokerLauncher -PathType Leaf)) {
        throw "BIOS broker launcher was not found at '$BrokerLauncher'."
    }

    New-Item -ItemType Directory -Path $brokerInstallDirectory -Force | Out-Null
    Copy-Item -LiteralPath $BrokerScript -Destination $stagedBrokerScript -Force
    Copy-Item -LiteralPath $BrokerLauncher -Destination $stagedBrokerLauncher -Force

    $selfTestArguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -SelfTest' -f $stagedBrokerScript
    $test = Start-Process `
        -FilePath $windowsPowerShell `
        -ArgumentList $selfTestArguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($test.ExitCode -ne 0) {
        throw "Elevated BIOS self-test failed with exit code $($test.ExitCode)."
    }

    Unregister-ScheduledTask -TaskName 'Victus Mode Switch' -Confirm:$false -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $stagedBrokerScript -Destination $installedBrokerScript -Force
    Move-Item -LiteralPath $stagedBrokerLauncher -Destination $installedBrokerLauncher -Force

    $actionArguments = '//B //NoLogo "{0}"' -f $installedBrokerLauncher
    $action = New-ScheduledTaskAction `
        -Execute $windowsScriptHost `
        -Argument $actionArguments `
        -WorkingDirectory $brokerInstallDirectory
    $principal = New-ScheduledTaskPrincipal -UserId $UserId -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -MultipleInstances IgnoreNew `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1)

    Register-ScheduledTask `
        -TaskName $taskName `
        -Action $action `
        -Principal $principal `
        -Settings $settings `
        -Description 'Lightweight HP Victus Eco/Standard/Performance switcher.' `
        -Force | Out-Null

    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree(
        'Software\VictusModeSwitch\Broker',
        $false)

    if ($PauseOmen) {
        $omenTasks = @(Get-ScheduledTask | Where-Object {
            $_.TaskName -like 'OmenInstallMonitor*' -or $_.TaskName -like 'OmenOverlay*'
        })
        if (-not (Test-Path -LiteralPath $omenTaskBackupPath)) {
            $backup = @($omenTasks | ForEach-Object {
                [ordered]@{
                    TaskName = $_.TaskName
                    TaskPath = $_.TaskPath
                    Enabled = [bool]$_.Settings.Enabled
                    Running = $_.State -eq 'Running'
                }
            })
            ConvertTo-Json -InputObject $backup -Depth 3 |
                Set-Content -LiteralPath $omenTaskBackupPath -Encoding UTF8
        }
        foreach ($omenTask in $omenTasks) {
            Stop-ScheduledTask -InputObject $omenTask -ErrorAction SilentlyContinue
            Disable-ScheduledTask -InputObject $omenTask | Out-Null
        }
        Get-Process -Name 'OmenInstallMonitor','OverlayHelper' -ErrorAction SilentlyContinue |
            Stop-Process -Force
    }

    $message = if ($PauseOmen) {
        'Protected BIOS broker registered and OMEN helper tasks paused.'
    } else {
        'Protected BIOS broker registered.'
    }
    [ordered]@{ Success = $true; Message = $message } |
        ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
} catch {
    Remove-Item -LiteralPath $stagedBrokerScript -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stagedBrokerLauncher -Force -ErrorAction SilentlyContinue
    [ordered]@{ Success = $false; Message = $_.Exception.Message } |
        ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    exit 1
}
