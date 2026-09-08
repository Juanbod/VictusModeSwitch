#Requires -Version 5.1

[CmdletBinding()]
param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$inspectionErrors = [System.Collections.Generic.List[string]]::new()

function Invoke-InspectionStep {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    try {
        return & $Action
    } catch {
        $inspectionErrors.Add("$Name`: $($_.Exception.Message)")
        return $null
    }
}

function Get-CimClassSummary {
    param(
        [Parameter(Mandatory)]
        [string]$ClassName
    )

    $class = Get-CimClass -Namespace 'root/wmi' -ClassName $ClassName
    return [ordered]@{
        Name = $class.CimClassName
        Properties = @(
            $class.CimClassProperties |
                Sort-Object Name |
                ForEach-Object {
                    [ordered]@{
                        Name = $_.Name
                        Type = [string]$_.CimType
                        Flags = [string]$_.Flags
                    }
                }
        )
        Methods = @(
            $class.CimClassMethods |
                Sort-Object Name |
                ForEach-Object {
                    [ordered]@{
                        Name = $_.Name
                        Parameters = @(
                            $_.Parameters |
                                Sort-Object Name |
                                ForEach-Object {
                                    [ordered]@{
                                        Name = $_.Name
                                        Type = [string]$_.CimType
                                        ReferenceClass = $_.ReferenceClassName
                                        Qualifiers = @($_.Qualifiers | ForEach-Object { $_.Name })
                                    }
                                }
                        )
                    }
                }
        )
    }
}

function Get-ExecutablePath {
    param([string]$CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        return $null
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($CommandLine.Trim())
    if ($expanded.StartsWith('"')) {
        $closingQuote = $expanded.IndexOf('"', 1)
        if ($closingQuote -gt 1) {
            return $expanded.Substring(1, $closingQuote - 1)
        }
    }

    return ($expanded -split '\s+', 2)[0]
}

function Get-FileVersionSummary {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $file = Get-Item -LiteralPath $Path
    return [ordered]@{
        Path = $file.FullName
        FileVersion = $file.VersionInfo.FileVersion
        ProductVersion = $file.VersionInfo.ProductVersion
    }
}

$computer = Invoke-InspectionStep -Name 'Computer system' -Action {
    Get-CimInstance -ClassName Win32_ComputerSystem | Select-Object -First 1
}
$baseBoard = Invoke-InspectionStep -Name 'Baseboard' -Action {
    Get-CimInstance -ClassName Win32_BaseBoard | Select-Object -First 1
}
$bios = Invoke-InspectionStep -Name 'BIOS version' -Action {
    Get-CimInstance -ClassName Win32_BIOS | Select-Object -First 1
}

$wmiClasses = @()
foreach ($className in @(
    'hpqBIntM',
    'hpqBDataIn',
    'hpqBDataOut0',
    'hpqBDataOut4',
    'hpqBDataOut128',
    'hpqBDataOut1024',
    'hpqBDataOut4096',
    'hpqBEvnt'
)) {
    $summary = Invoke-InspectionStep -Name "WMI class $className" -Action {
        Get-CimClassSummary -ClassName $className
    }
    if ($null -ne $summary) {
        $wmiClasses += $summary
    }
}

$omenPackage = Invoke-InspectionStep -Name 'OMEN Gaming Hub package' -Action {
    Get-AppxPackage -Name 'AD2F1837.OMENCommandCenter' | Select-Object -First 1
}
$omenService = Invoke-InspectionStep -Name 'HP Omen HSA service' -Action {
    Get-CimInstance -ClassName Win32_Service -Filter "Name = 'HPOmenCap'" | Select-Object -First 1
}
$omenServicePath = if ($null -ne $omenService) {
    Get-ExecutablePath -CommandLine ([string]$omenService.PathName)
} else {
    $null
}
$omenServiceFile = Invoke-InspectionStep -Name 'HP Omen HSA executable version' -Action {
    Get-FileVersionSummary -Path $omenServicePath
}

$systemDesignPath = 'HKCU:\Software\HP\OMEN Ally\Settings'
$systemDesignData = Invoke-InspectionStep -Name 'OMEN SystemDesignData cache' -Action {
    if (Test-Path -LiteralPath $systemDesignPath) {
        Get-ItemPropertyValue -LiteralPath $systemDesignPath -Name 'SystemDesignData' -ErrorAction SilentlyContinue
    }
}

$systemDesign = [ordered]@{
    Source = 'OMEN Gaming Hub HKCU cache; no BIOS method was invoked'
    RegistryPath = 'HKCU\Software\HP\OMEN Ally\Settings'
    Present = $systemDesignData -is [byte[]]
    Length = 0
    Hex = $null
    Decoded = $null
}
if ($systemDesignData -is [byte[]]) {
    $systemDesign.Length = $systemDesignData.Length
    $systemDesign.Hex = [BitConverter]::ToString($systemDesignData)
    if ($systemDesignData.Length -ge 9) {
        $capabilities = $systemDesignData[4]
        $systemDesign.Decoded = [ordered]@{
            ShippingAdapterPowerRatingWatts = [int]($systemDesignData[0] -bor ($systemDesignData[1] -shl 8))
            ThermalPolicyVersion = [int]$systemDesignData[3]
            SoftwareFanControl = ($capabilities -band 0x01) -ne 0
            ExtremeMode = ($capabilities -band 0x02) -ne 0
            ExtremeModeUnlocked = ($capabilities -band 0x04) -ne 0
            BiosOverclockingFlag = $systemDesignData[6] -ne 0
            GraphicsSwitchingFlag = $systemDesignData[7] -ne 0
            DefaultConcurrentTdp = [int]$systemDesignData[8]
            Interpretation = 'Observed in OMEN Gaming Hub 1101.2605.2.0; layout can vary by platform'
        }
    }
}

$report = [ordered]@{
    SchemaVersion = 1
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Safety = 'Read-only inventory. This script does not invoke hpqBIOSInt methods or write firmware settings.'
    Hardware = [ordered]@{
        Manufacturer = if ($null -ne $computer) { [string]$computer.Manufacturer } else { $null }
        Model = if ($null -ne $computer) { [string]$computer.Model } else { $null }
        SystemSku = if ($null -ne $computer) { [string]$computer.SystemSKUNumber } else { $null }
        BaseBoardManufacturer = if ($null -ne $baseBoard) { [string]$baseBoard.Manufacturer } else { $null }
        BaseBoardProduct = if ($null -ne $baseBoard) { [string]$baseBoard.Product } else { $null }
        BaseBoardVersion = if ($null -ne $baseBoard) { [string]$baseBoard.Version } else { $null }
        BiosVersion = if ($null -ne $bios) { [string]$bios.SMBIOSBIOSVersion } else { $null }
        BiosReleaseDate = if ($null -ne $bios -and $null -ne $bios.ReleaseDate) {
            ([DateTime]$bios.ReleaseDate).ToString('yyyy-MM-dd')
        } else {
            $null
        }
    }
    HpWmi = [ordered]@{
        Namespace = 'root\wmi'
        BiosGuid = '5FB7F034-2C63-45E9-BE91-3D44E2C707E4'
        EventGuid = '95F24279-4D7B-4334-9387-ACCDC67EF61C'
        Classes = $wmiClasses
    }
    Omen = [ordered]@{
        Package = if ($null -ne $omenPackage) {
            [ordered]@{
                Name = [string]$omenPackage.Name
                Version = [string]$omenPackage.Version
                Architecture = [string]$omenPackage.Architecture
                InstallLocation = [string]$omenPackage.InstallLocation
            }
        } else {
            $null
        }
        HsaService = if ($null -ne $omenService) {
            [ordered]@{
                Name = [string]$omenService.Name
                DisplayName = [string]$omenService.DisplayName
                State = [string]$omenService.State
                StartMode = [string]$omenService.StartMode
                Executable = $omenServiceFile
            }
        } else {
            $null
        }
        SystemDesignData = $systemDesign
    }
    Errors = @($inspectionErrors)
}

$json = $report | ConvertTo-Json -Depth 10
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $fullOutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) {
        [IO.Path]::GetFullPath($OutputPath)
    } else {
        [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
    }
    $parent = Split-Path -Parent $fullOutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllText($fullOutputPath, $json, [Text.UTF8Encoding]::new($false))
}

$json
