[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'
$taskName = 'Victus Mode Switch BIOS'
$dataDirectory = [System.IO.Path]::GetDirectoryName($ResultPath)
$omenTaskBackupPath = Join-Path $dataDirectory 'omen-task-backup.json'
$brokerInstallDirectory = Join-Path $env:ProgramFiles 'VictusModeSwitch\Broker'

try {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName 'Victus Mode Switch' -Confirm:$false -ErrorAction SilentlyContinue
    Get-Process -Name 'VictusModeSwitch' -ErrorAction SilentlyContinue | Stop-Process -Force

    if (Test-Path -LiteralPath $omenTaskBackupPath) {
        $omenTaskBackup = @(Get-Content -LiteralPath $omenTaskBackupPath -Raw | ConvertFrom-Json)
        foreach ($entry in $omenTaskBackup) {
            $omenTask = Get-ScheduledTask -TaskName $entry.TaskName -TaskPath $entry.TaskPath -ErrorAction SilentlyContinue
            if ($null -eq $omenTask) {
                continue
            }

            if ($entry.Enabled) {
                Enable-ScheduledTask -InputObject $omenTask | Out-Null
                if ($entry.Running) {
                    Start-ScheduledTask -InputObject $omenTask
                }
            } else {
                Disable-ScheduledTask -InputObject $omenTask | Out-Null
            }
        }
        Remove-Item -LiteralPath $omenTaskBackupPath -Force
    }

    $expectedBrokerDirectory = [System.IO.Path]::GetFullPath(
        (Join-Path ([System.IO.Path]::GetFullPath($env:ProgramFiles)) 'VictusModeSwitch\Broker'))
    $resolvedBrokerDirectory = [System.IO.Path]::GetFullPath($brokerInstallDirectory)
    if (-not [string]::Equals(
        $resolvedBrokerDirectory,
        $expectedBrokerDirectory,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected broker path '$resolvedBrokerDirectory'."
    }
    if (Test-Path -LiteralPath $resolvedBrokerDirectory) {
        Remove-Item -LiteralPath $resolvedBrokerDirectory -Recurse -Force
        Remove-Item -LiteralPath ([System.IO.Path]::GetDirectoryName($resolvedBrokerDirectory)) `
            -Force -ErrorAction SilentlyContinue
    }

    [ordered]@{ Success = $true; Message = 'Protected BIOS broker was removed.' } |
        ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
} catch {
    [ordered]@{ Success = $false; Message = $_.Exception.Message } |
        ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    exit 1
}
