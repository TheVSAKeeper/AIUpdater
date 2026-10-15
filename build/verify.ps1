#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'release-info.ps1') | Out-Null
& dotnet build (Join-Path $projectRoot 'AIUpdater.slnx') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Ошибка сборки решения' }
foreach ($scriptName in @('Verify-Updater.ps1', 'Verify-PackageChecks.ps1'))
{
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $projectRoot "tests\Scripts\$scriptName")
    if ($LASTEXITCODE -ne 0) { throw "Не пройдена проверка $scriptName" }
}
& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $projectRoot 'tests\Scripts\Verify-Release.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Не пройдена проверка релизной системы' }
& dotnet run --no-build --project (Join-Path $projectRoot 'tests\AIUpdater.InterfaceChecks') -c $Configuration -- (Join-Path $projectRoot 'artifacts\interface-check')
if ($LASTEXITCODE -ne 0) { throw 'Не пройдена проверка интерфейса' }
