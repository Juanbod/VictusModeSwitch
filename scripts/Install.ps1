[CmdletBinding()]
param(
    [string]$PublishDirectory,
    [switch]$ConfigureOnly,
    [switch]$PreflightOnly,
    [switch]$PauseOmen,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
function Get-Sha256 {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
    } finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\VictusModeSwitch'
$dataDirectory = Join-Path $env:LOCALAPPDATA 'VictusModeSwitch'
$executable = Join-Path $installDirectory 'VictusModeSwitch.exe'
$elevationResult = Join-Path $dataDirectory 'elevated-setup-result.json'
$elevatedScript = Join-Path $PSScriptRoot 'Configure-Elevated.ps1'
$brokerScript = Join-Path $PSScriptRoot 'BiosBroker.ps1'
$brokerLauncher = Join-Path $PSScriptRoot 'BiosBroker.vbs'
$brokerInstallDirectory = Join-Path $env:ProgramFiles 'VictusModeSwitch\Broker'
$installedBrokerScript = Join-Path $brokerInstallDirectory 'BiosBroker.ps1'
$installedBrokerLauncher = Join-Path $brokerInstallDirectory 'BiosBroker.vbs'
$windowsScriptHost = Join-Path $env:SystemRoot 'System32\wscript.exe'
$expectedTaskArguments = '//B //NoLogo "{0}"' -f $installedBrokerLauncher
$backgroundKeyPath = 'Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\AD2F1837.OMENCommandCenter_v10z8vjag6ke6'
$backgroundBackupPath = Join-Path $dataDirectory 'omen-background-backup.json'
$omenTaskBackupPath = Join-Path $dataDirectory 'omen-task-backup.json'
$biosTaskName = 'Victus Mode Switch BIOS'
$trayTaskName = 'Victus Mode Switch Tray'
$legacyBrokerRegistryPath = 'Software\VictusModeSwitch\Broker'

New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

if (-not $ConfigureOnly -and -not $PreflightOnly) {
    if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
        $PublishDirectory = Join-Path $PSScriptRoot '..\src\VictusModeSwitch\bin\Release\net10.0-windows\win-x64\publish'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'VictusModeSwitch.exe') -PathType Leaf)) {
        throw "Published application was not found in '$PublishDirectory'."
    }

    Stop-ScheduledTask -TaskName $trayTaskName -ErrorAction SilentlyContinue
    Get-Process -Name 'VictusModeSwitch' -ErrorAction SilentlyContinue | Stop-Process -Force
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $PublishDirectory '*') -Destination $installDirectory -Recurse -Force
    $installedScripts = Join-Path $installDirectory 'scripts'
    New-Item -ItemType Directory -Path $installedScripts -Force | Out-Null
    Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $installedScripts 'Install.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Configure-Elevated.ps1') -Destination $installedScripts -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BiosBroker.ps1') -Destination $installedScripts -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BiosBroker.vbs') -Destination $installedScripts -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Remove-Elevated.ps1') -Destination $installedScripts -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $installedScripts -Force
    $elevatedScript = Join-Path $installedScripts 'Configure-Elevated.ps1'
    $brokerScript = Join-Path $installedScripts 'BiosBroker.ps1'
    $brokerLauncher = Join-Path $installedScripts 'BiosBroker.vbs'
}

if (-not $PreflightOnly -and -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Installed executable was not found at '$executable'."
}
if (-not (Test-Path -LiteralPath $brokerScript -PathType Leaf)) {
    throw "BIOS broker script was not found at '$brokerScript'."
}
if (-not (Test-Path -LiteralPath $brokerLauncher -PathType Leaf)) {
    throw "BIOS broker launcher was not found at '$brokerLauncher'."
}

$userId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$needsElevatedSetup = $true
$executableMatches = $false
$argumentsMatch = $false
$workingDirectoryMatches = $false
$runLevelMatches = $false
$principalMatches = $false
$multipleInstancesMatch = $false
$taskShapeMatches = $false
$brokerMatches = $false
$existingTask = Get-ScheduledTask -TaskName $biosTaskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask) {
    $taskActions = @($existingTask.Actions)
    if ($taskActions.Count -eq 1) {
        $taskAction = $taskActions[0]
        $taskExecutable = [Environment]::ExpandEnvironmentVariables([string]$taskAction.Execute)
        $executableMatches = $false
        if (-not [string]::IsNullOrWhiteSpace($taskExecutable)) {
            $executableMatches = [string]::Equals(
                [System.IO.Path]::GetFullPath($taskExecutable),
                [System.IO.Path]::GetFullPath($windowsScriptHost),
                [System.StringComparison]::OrdinalIgnoreCase)
        }
        $argumentsMatch = [string]::Equals(
            ([string]$taskAction.Arguments).Trim(),
            $expectedTaskArguments,
            [System.StringComparison]::OrdinalIgnoreCase)
        $taskWorkingDirectory = [Environment]::ExpandEnvironmentVariables(
            [string]$taskAction.WorkingDirectory)
        $workingDirectoryMatches = -not [string]::IsNullOrWhiteSpace($taskWorkingDirectory) -and
            [string]::Equals(
                [System.IO.Path]::GetFullPath($taskWorkingDirectory),
                [System.IO.Path]::GetFullPath($brokerInstallDirectory),
                [System.StringComparison]::OrdinalIgnoreCase)
    } else {
        $executableMatches = $false
        $argumentsMatch = $false
        $workingDirectoryMatches = $false
    }
    $runLevelMatches = [string]::Equals(
        [string]$existingTask.Principal.RunLevel,
        'Highest',
        [System.StringComparison]::OrdinalIgnoreCase)
    try {
        $taskUserSid = ([System.Security.Principal.NTAccount]::new(
            [string]$existingTask.Principal.UserId)).Translate(
                [System.Security.Principal.SecurityIdentifier]).Value
    } catch {
        $taskUserSid = ''
    }
    $principalMatches = [string]::Equals(
        $taskUserSid,
        $userSid,
        [System.StringComparison]::OrdinalIgnoreCase)
    $multipleInstancesMatch = [string]::Equals(
        [string]$existingTask.Settings.MultipleInstances,
        'Queue',
        [System.StringComparison]::OrdinalIgnoreCase)
    $taskShapeMatches = [bool]$existingTask.Settings.Enabled -and
        @($existingTask.Triggers | Where-Object { $null -ne $_ }).Count -eq 0 -and
        $multipleInstancesMatch
    $brokerMatches = (Test-Path -LiteralPath $installedBrokerScript -PathType Leaf) -and
        (Test-Path -LiteralPath $installedBrokerLauncher -PathType Leaf) -and
        ((Get-Sha256 -Path $brokerScript) -eq
            (Get-Sha256 -Path $installedBrokerScript)) -and
        ((Get-Sha256 -Path $brokerLauncher) -eq
            (Get-Sha256 -Path $installedBrokerLauncher))
    $needsElevatedSetup = -not (
        $executableMatches -and
        $argumentsMatch -and
        $workingDirectoryMatches -and
        $runLevelMatches -and
        $principalMatches -and
        $taskShapeMatches -and
        $brokerMatches)
}
if ($PauseOmen -and -not (Test-Path -LiteralPath $omenTaskBackupPath)) {
    $needsElevatedSetup = $true
}

if ($needsElevatedSetup) {
    Remove-Item -LiteralPath $elevationResult -Force -ErrorAction SilentlyContinue
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $elevatedScript),
        '-BrokerScript', ('"{0}"' -f $brokerScript),
        '-BrokerLauncher', ('"{0}"' -f $brokerLauncher),
        '-ResultPath', ('"{0}"' -f $elevationResult),
        '-UserId', ('"{0}"' -f $userId)
    )
    if ($PauseOmen) {
        $arguments += '-PauseOmen'
    }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList ($arguments -join ' ') -Wait

    if (-not (Test-Path -LiteralPath $elevationResult)) {
        throw 'Elevated setup did not return a result. The UAC prompt may have been cancelled.'
    }
    $setupResult = Get-Content -LiteralPath $elevationResult -Raw | ConvertFrom-Json
    if (-not $setupResult.Success) {
        throw "Elevated setup failed: $($setupResult.Message)"
    }
}

if ($PreflightOnly) {
    Write-Output 'Protected BIOS broker preflight completed.'
    exit 0
}

$legacyBrokerDirectory = Join-Path $installDirectory 'Broker'
if (Test-Path -LiteralPath $legacyBrokerDirectory) {
    Remove-Item -LiteralPath $legacyBrokerDirectory -Recurse -Force
}
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($legacyBrokerRegistryPath, $false)

if ($PauseOmen) {
    $backgroundKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($backgroundKeyPath, $true)
    if ($null -ne $backgroundKey) {
        if (-not (Test-Path -LiteralPath $backgroundBackupPath)) {
            $names = $backgroundKey.GetValueNames()
            [ordered]@{
                DisabledExists = $names -contains 'Disabled'
                Disabled = $backgroundKey.GetValue('Disabled', 0)
                DisabledByUserExists = $names -contains 'DisabledByUser'
                DisabledByUser = $backgroundKey.GetValue('DisabledByUser', 0)
            } | ConvertTo-Json | Set-Content -LiteralPath $backgroundBackupPath -Encoding UTF8
        }
        $backgroundKey.SetValue('Disabled', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $backgroundKey.SetValue('DisabledByUser', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $backgroundKey.Dispose()
    }
    Get-Process -Name 'OmenCommandCenterBackground' -ErrorAction SilentlyContinue | Stop-Process -Force
}

Stop-ScheduledTask -TaskName $trayTaskName -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $trayTaskName -Confirm:$false -ErrorAction SilentlyContinue

$runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
$runKey.SetValue(
    'VictusModeSwitch',
    ('"{0}"' -f $executable),
    [Microsoft.Win32.RegistryValueKind]::String)
$runKey.Dispose()

if (-not $NoStart) {
    Start-Process -FilePath $executable
}

Write-Output "Configured: $executable"
