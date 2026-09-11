[CmdletBinding()]
param(
    [string]$Version = '2.4.2'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsDirectory = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
if ([System.IO.Path]::GetDirectoryName($artifactsDirectory) -ne $projectRoot) {
    throw "Unexpected artifacts path '$artifactsDirectory'."
}
if (Test-Path -LiteralPath $artifactsDirectory) {
    Remove-Item -LiteralPath $artifactsDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $artifactsDirectory 'publish') -Force | Out-Null

Push-Location $projectRoot
try {
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $windowsPowerShell `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File .\scripts\Test-UninstallRecovery.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell uninstall tests failed.' }

    dotnet test .\VictusModeSwitch.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    dotnet publish .\src\VictusModeSwitch\VictusModeSwitch.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=false `
        -p:DebugType=embedded `
        -o .\artifacts\publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    $iscc = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($iscc)) {
        throw 'Inno Setup 6 was not found.'
    }

    & $iscc "/DAppVersion=$Version" .\installer\VictusModeSwitch.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

    $installer = Join-Path $artifactsDirectory 'VictusModeSwitch-Setup.exe'
    $checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$checksum  VictusModeSwitch-Setup.exe" |
        Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
    Write-Output "Built $installer"
    Write-Output "SHA256 $checksum"
} finally {
    Pop-Location
}
