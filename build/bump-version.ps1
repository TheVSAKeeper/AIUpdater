#requires -Version 7.0
[CmdletBinding()]
param([string]$Version = 'next')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$current = & (Join-Path $PSScriptRoot 'release-info.ps1')
$parts = $current.Version.Split('.')
$major = [int]$parts[0]
$patch = if ($parts.Count -eq 2) { [int]$parts[1] } else { 0 }
$next = switch ($Version)
{
    'next' { [string]($major + 1) }
    'patch' { "$major.$($patch + 1)" }
    default { $Version }
}
$release = & (Join-Path $PSScriptRoot 'release-info.ps1') -Version $next
$nextParts = $release.Version.Split('.')
$nextMajor = [int]$nextParts[0]
$nextPatch = if ($nextParts.Count -eq 2) { [int]$nextParts[1] } else { 0 }
if ($nextMajor -lt $major -or ($nextMajor -eq $major -and $nextPatch -le $patch))
{
    throw "Новая версия должна быть больше $($current.Version)"
}
$propsPath = Join-Path $projectRoot 'Directory.Build.props'
$content = [IO.File]::ReadAllText($propsPath)
$content = [regex]::Replace($content, '<Version>[^<]*</Version>', "<Version>$($release.Version)</Version>")
[IO.File]::WriteAllText($propsPath, $content, [Text.UTF8Encoding]::new($true))
Write-Output "$($current.Tag) -> $($release.Tag)"
