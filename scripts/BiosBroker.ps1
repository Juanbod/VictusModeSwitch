#Requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$request = $null
$requestId = [Guid]::Empty
$pipe = $null
$reader = $null
$writer = $null

function New-BrokerResultJson {
    param(
        [Parameter(Mandatory)]
        [Guid]$Id,
        [Parameter(Mandatory)]
        [bool]$Success,
        [Parameter(Mandatory)]
        [string]$Message,
        [Parameter(Mandatory)]
        [bool]$MaxFanEnabled,
        [string[]]$AffectedServices = @()
    )

    return [ordered]@{
        Id = $Id
        Success = $Success
        Message = $Message
        MaxFanEnabled = $MaxFanEnabled
        AffectedServices = @($AffectedServices)
    } | ConvertTo-Json -Compress
}

function Get-BoardProduct {
    $searcher = [System.Management.ManagementObjectSearcher]::new(
        'root\cimv2',
        'SELECT Product FROM Win32_BaseBoard')
    try {
        foreach ($board in $searcher.Get()) {
            try {
                return ([string]$board['Product']).Trim()
            } finally {
                $board.Dispose()
            }
        }
    } finally {
        $searcher.Dispose()
    }

    return ''
}

function Invoke-HpBiosCommand {
    param(
        [Parameter(Mandatory)]
        [uint32]$CommandType,
        [byte[]]$Payload = [byte[]]@(),
        [Parameter(Mandatory)]
        [ValidateSet(4, 128)]
        [int]$OutputSize
    )

    $searcher = $null
    $bios = $null
    $dataClass = $null
    $inputData = $null
    $methodInput = $null
    $methodOutput = $null
    $outputData = $null
    try {
        $scope = [System.Management.ManagementScope]::new('\\.\root\wmi')
        $scope.Connect()
        $searcher = [System.Management.ManagementObjectSearcher]::new(
            $scope,
            [System.Management.ObjectQuery]::new('SELECT * FROM hpqBIntM'))
        foreach ($candidate in $searcher.Get()) {
            $bios = $candidate
            break
        }
        if ($null -eq $bios) {
            throw 'HP BIOS WMI interface hpqBIntM was not found.'
        }

        $dataClass = [System.Management.ManagementClass]::new(
            $scope,
            [System.Management.ManagementPath]::new('hpqBDataIn'),
            $null)
        $inputData = $dataClass.CreateInstance()
        if ($null -eq $inputData) {
            throw 'Could not create the HP BIOS input object.'
        }

        $inputData['Sign'] = [byte[]](0x53, 0x45, 0x43, 0x55)
        $inputData['Command'] = [uint32]0x20008
        $inputData['CommandType'] = $CommandType
        $inputData['Size'] = [uint32]$Payload.Length
        if ($Payload.Length -gt 0) {
            $inputData['hpqBData'] = $Payload
        }

        $methodName = "hpqBIOSInt$OutputSize"
        $methodInput = $bios.GetMethodParameters($methodName)
        $methodInput['InData'] = $inputData
        $methodOutput = $bios.InvokeMethod($methodName, $methodInput, $null)
        if ($null -eq $methodOutput) {
            throw "HP BIOS method $methodName did not return a result."
        }
        $outputData = $methodOutput['OutData']
        if ($null -eq $outputData) {
            throw "HP BIOS method $methodName did not return OutData."
        }

        $data = if ($OutputSize -eq 0 -or $null -eq $outputData['Data']) {
            [byte[]]@()
        } else {
            [byte[]]@($outputData['Data'])
        }
        return [pscustomobject]@{
            ReturnCode = [uint32]$outputData['rwReturnCode']
            Data = $data
        }
    } finally {
        foreach ($item in @($outputData, $methodOutput, $methodInput, $inputData, $dataClass, $bios, $searcher)) {
            if ($null -ne $item -and $item -is [System.IDisposable]) {
                $item.Dispose()
            }
        }
    }
}

function Assert-BiosSuccess {
    param(
        [Parameter(Mandatory)]
        [string]$Operation,
        [Parameter(Mandatory)]
        $Result
    )

    if ($Result.ReturnCode -ne 0) {
        throw "HP BIOS operation '$Operation' failed with code $($Result.ReturnCode)."
    }
}

function Assert-SupportedHardware {
    $board = Get-BoardProduct
    if (-not [string]::Equals($board, '8A4F', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsupported system board '$board'; expected 8A4F."
    }

    $design = Invoke-HpBiosCommand -CommandType 0x28 -OutputSize 128
    Assert-BiosSuccess -Operation 'read System Design' -Result $design
    if ($design.Data.Length -lt 4) {
        throw 'The BIOS returned an incomplete System Design block.'
    }
    if ($design.Data[3] -ne 0) {
        throw "Unsupported HP Thermal Policy V$($design.Data[3]); expected V0."
    }
}

function Set-FanMode {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Eco', 'Standard', 'Performance')]
        [string]$Mode
    )

    $biosMode = if ($Mode -eq 'Performance') { [byte]1 } else { [byte]0 }
    $result = Invoke-HpBiosCommand `
        -CommandType 0x1A `
        -Payload ([byte[]](0xFF, $biosMode, 0, 0)) `
        -OutputSize 4
    Assert-BiosSuccess -Operation "set mode $Mode" -Result $result
}

function Set-MaxFan {
    param(
        [Parameter(Mandatory)]
        [bool]$Enabled
    )

    $value = if ($Enabled) { [byte]1 } else { [byte]0 }
    $result = Invoke-HpBiosCommand `
        -CommandType 0x27 `
        -Payload ([byte[]]($value, 0, 0, 0)) `
        -OutputSize 4
    Assert-BiosSuccess -Operation 'set Max Fan' -Result $result
}

function Get-MaxFan {
    $result = Invoke-HpBiosCommand -CommandType 0x26 -OutputSize 4
    Assert-BiosSuccess -Operation 'read Max Fan' -Result $result
    if ($result.Data.Length -lt 1) {
        throw 'The BIOS did not return the Max Fan state.'
    }
    return $result.Data[0] -ne 0
}

function Get-ValidatedHpServices {
    param($RequestedServices)

    $allowed = @(
        'HPAppHelperCap',
        'HPDiagsCap',
        'HPNetworkCap',
        'HPOmenCap',
        'HPSysInfoCap'
    )
    $requested = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($RequestedServices)) {
        $name = ([string]$entry).Trim()
        if ([string]::IsNullOrWhiteSpace($name) -or $allowed -notcontains $name) {
            throw "Unsupported HP service '$name'."
        }
        [void]$requested.Add($name)
    }

    return @($allowed | Where-Object { $requested.Contains($_) })
}

function Set-HpAppServices {
    param(
        [Parameter(Mandatory)]
        [string[]]$Services,
        [Parameter(Mandatory)]
        [bool]$Stop
    )

    $affected = [Collections.Generic.List[string]]::new()
    foreach ($name in $Services) {
        $service = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            continue
        }

        try {
            $service.Refresh()
            if ($Stop) {
                if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
                    $service.Stop()
                    $service.WaitForStatus(
                        [System.ServiceProcess.ServiceControllerStatus]::Stopped,
                        [TimeSpan]::FromSeconds(8))
                    $affected.Add($name)
                }
            } elseif ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
                if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
                    $service.WaitForStatus(
                        [System.ServiceProcess.ServiceControllerStatus]::Stopped,
                        [TimeSpan]::FromSeconds(8))
                }
                $service.Start()
                $service.WaitForStatus(
                    [System.ServiceProcess.ServiceControllerStatus]::Running,
                    [TimeSpan]::FromSeconds(8))
                $affected.Add($name)
            }
        } finally {
            $service.Dispose()
        }
    }

    return $affected.ToArray()
}

function Invoke-BrokerSelfTest {
    Assert-SupportedHardware
    $previousMaxFan = Get-MaxFan
    try {
        Set-FanMode -Mode Performance
        Set-MaxFan -Enabled $false
        if (Get-MaxFan) {
            throw 'The BIOS self-test could not disable Max Fan.'
        }
    } finally {
        Set-FanMode -Mode Standard
        Set-MaxFan -Enabled $previousMaxFan
    }
}

if ($SelfTest) {
    try {
        Invoke-BrokerSelfTest
        exit 0
    } catch {
        exit 1
    }
}

try {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $pipeName = 'VictusModeSwitch.Bios.' + $sid.Replace('-', '.')
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(
        '.',
        $pipeName,
        [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::None)
    $pipe.Connect(5000)
    $utf8 = [Text.UTF8Encoding]::new($false)
    $reader = [IO.StreamReader]::new($pipe, $utf8, $false, 1024, $true)
    $writer = [IO.StreamWriter]::new($pipe, $utf8, 1024, $true)
    $writer.AutoFlush = $true

    $requestJson = $reader.ReadLine()
    if ([string]::IsNullOrWhiteSpace($requestJson)) {
        throw 'The BIOS broker request is empty.'
    }

    $request = $requestJson | ConvertFrom-Json
    $requestId = [Guid]::Parse([string]$request.Id)
    $createdAt = [DateTimeOffset]::Parse(
        [string]$request.CreatedAt,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
    $now = [DateTimeOffset]::UtcNow
    if ($createdAt -lt $now.AddMinutes(-1) -or $createdAt -gt $now.AddSeconds(15)) {
        throw 'The BIOS broker request timestamp is outside the allowed window.'
    }

    $operation = if ($request.PSObject.Properties.Name -contains 'Operation') {
        ([string]$request.Operation).Trim()
    } else {
        'ApplyHardware'
    }
    $actualMaxFan = $false
    $affectedServices = @()
    switch ($operation) {
        'ApplyHardware' {
            $mode = ([string]$request.Mode).Trim()
            if (@('Eco', 'Standard', 'Performance') -notcontains $mode) {
                throw "Unsupported BIOS mode '$mode'."
            }
            if ($request.MaxFanEnabled -isnot [bool]) {
                throw 'The Max Fan request value is invalid.'
            }
            $maxFanEnabled = [bool]$request.MaxFanEnabled

            Assert-SupportedHardware
            Set-FanMode -Mode $mode
            Set-MaxFan -Enabled $maxFanEnabled
            $actualMaxFan = Get-MaxFan
            if ($actualMaxFan -ne $maxFanEnabled) {
                throw 'The BIOS did not confirm the requested Max Fan state.'
            }
        }
        'StopHpServices' {
            $services = @(Get-ValidatedHpServices -RequestedServices $request.Services)
            $affectedServices = @(Set-HpAppServices -Services $services -Stop $true)
        }
        'StartHpServices' {
            $services = @(Get-ValidatedHpServices -RequestedServices $request.Services)
            $affectedServices = @(Set-HpAppServices -Services $services -Stop $false)
        }
        default {
            throw "Unsupported broker operation '$operation'."
        }
    }

    $resultJson = New-BrokerResultJson `
        -Id $requestId `
        -Success $true `
        -Message 'OK' `
        -MaxFanEnabled $actualMaxFan `
        -AffectedServices $affectedServices
    $writer.WriteLine($resultJson)
    $exitCode = 0
} catch {
    $message = $_.Exception.Message
    try {
        if ($null -ne $writer) {
            $resultJson = New-BrokerResultJson `
                -Id $requestId `
                -Success $false `
                -Message $message `
                -MaxFanEnabled $false
            $writer.WriteLine($resultJson)
        }
    } catch {
    }
    $exitCode = 1
} finally {
    foreach ($item in @($writer, $reader, $pipe)) {
        if ($null -ne $item) {
            $item.Dispose()
        }
    }
}

exit $exitCode
