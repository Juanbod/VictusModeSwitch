[CmdletBinding()]
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\VictusModeSwitch'
$dataDirectory = Join-Path $env:LOCALAPPDATA 'VictusModeSwitch'
$executable = Join-Path $installDirectory 'VictusModeSwitch.exe'
$backgroundBackupPath = Join-Path $dataDirectory 'omen-background-backup.json'
$backgroundKeyPath = 'Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\AD2F1837.OMENCommandCenter_v10z8vjag6ke6'
$elevationResult = Join-Path $dataDirectory 'elevated-remove-result.json'
$elevatedScript = Join-Path $PSScriptRoot 'Remove-Elevated.ps1'
$trayTaskName = 'Victus Mode Switch Tray'
$brokerRegistryPath = 'Software\VictusModeSwitch\Broker'

Stop-ScheduledTask -TaskName $trayTaskName -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $trayTaskName -Confirm:$false -ErrorAction SilentlyContinue
Get-Process -Name 'VictusModeSwitch' -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path -LiteralPath $executable -PathType Leaf) {
    $restore = Start-Process -FilePath $executable -ArgumentList '--set standard' -Wait -PassThru
    if ($restore.ExitCode -ne 0) {
        throw "Could not restore Standard mode; exit code $($restore.ExitCode)."
    }

    $fanRestore = Start-Process -FilePath $executable -ArgumentList '--max-fan off' -Wait -PassThru
    if ($fanRestore.ExitCode -ne 0) {
        throw "Could not restore automatic fan control; exit code $($fanRestore.ExitCode)."
    }
}

New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
Remove-Item -LiteralPath $elevationResult -Force -ErrorAction SilentlyContinue
$arguments = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', ('"{0}"' -f $elevatedScript),
    '-ResultPath', ('"{0}"' -f $elevationResult)
) -join ' '
Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -Wait
if (-not (Test-Path -LiteralPath $elevationResult)) {
    throw 'Elevated removal did not return a result. The UAC prompt may have been cancelled.'
}
$removeResult = Get-Content -LiteralPath $elevationResult -Raw | ConvertFrom-Json
if (-not $removeResult.Success) {
    throw "Elevated removal failed: $($removeResult.Message)"
}

$runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
$runKey.DeleteValue('VictusModeSwitch', $false)
$runKey.Dispose()
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($brokerRegistryPath, $false)

if (Test-Path -LiteralPath $backgroundBackupPath) {
    $backup = Get-Content -LiteralPath $backgroundBackupPath -Raw | ConvertFrom-Json
    $backgroundKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($backgroundKeyPath)
    if ($backup.DisabledExists) {
        $backgroundKey.SetValue('Disabled', [int]$backup.Disabled, [Microsoft.Win32.RegistryValueKind]::DWord)
    } else {
        $backgroundKey.DeleteValue('Disabled', $false)
    }
    if ($backup.DisabledByUserExists) {
        $backgroundKey.SetValue('DisabledByUser', [int]$backup.DisabledByUser, [Microsoft.Win32.RegistryValueKind]::DWord)
    } else {
        $backgroundKey.DeleteValue('DisabledByUser', $false)
    }
    $backgroundKey.Dispose()
    Remove-Item -LiteralPath $backgroundBackupPath -Force
}

if ($RemoveData -and (Test-Path -LiteralPath $dataDirectory)) {
    $localAppData = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
    $resolvedData = [System.IO.Path]::GetFullPath($dataDirectory)
    if (-not $resolvedData.StartsWith(
        $localAppData + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected data path '$resolvedData'."
    }
    Remove-Item -LiteralPath $resolvedData -Recurse -Force
}

Write-Output 'Victus Mode Switch was removed; Standard mode and saved OMEN settings were restored.'
