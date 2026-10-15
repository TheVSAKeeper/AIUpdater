#requires -Version 7.0
[CmdletBinding()]
param([string]$Version)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$properties = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)
$versionNodes = $properties.SelectNodes('/Project/PropertyGroup/Version')
if ($versionNodes.Count -ne 1)
{
    throw 'Directory.Build.props должен содержать один Version'
}
if (-not $PSBoundParameters.ContainsKey('Version'))
{
    $Version = $versionNodes[0].InnerText
}
if ($Version -cnotmatch '^[1-9][0-9]*(\.[1-9][0-9]*)?$')
{
    throw "Непригодная версия '$Version': ожидается N или N.P, например 1 или 1.1"
}
foreach ($part in $Version.Split('.'))
{
    $number = 0
    if (-not [int]::TryParse($part, [ref]$number) -or $number -gt 65534)
    {
        throw 'Часть версии должна быть от 1 до 65534'
    }
}
$changelog = Get-Content -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Raw
$sections = [regex]::Matches($changelog, "(?ms)^## $([regex]::Escape($Version))\r?\n(?<notes>.*?)(?=^## |\z)")
if ($sections.Count -ne 1 -or [string]::IsNullOrWhiteSpace($sections[0].Groups['notes'].Value))
{
    throw "CHANGELOG.md: нужен один непустой раздел '## $Version'"
}
[pscustomobject]@{
    Version = $Version
    Tag = "v$Version"
    Notes = $sections[0].Groups['notes'].Value.Trim()
}
