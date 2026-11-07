#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Head,
    [string]$RepoUrl = 'https://github.com/TheVSAKeeper/AIUpdater',
    [string]$RepoSlug = 'TheVSAKeeper/AIUpdater'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$infoArgs = @{}
if ($PSBoundParameters.ContainsKey('Version'))
{
    $infoArgs['Version'] = $Version
}
$release = & (Join-Path $PSScriptRoot 'release-info.ps1') @infoArgs

$templatePath = Join-Path $projectRoot '.github\release-body.md'
if (-not (Test-Path -LiteralPath $templatePath))
{
    throw "Не найден шаблон описания релиза: $templatePath"
}
$header = (Get-Content -LiteralPath $templatePath -Raw).Trim()
$header = $header.Replace('__VERSION__', $release.Version).Replace('__REPO__', $RepoUrl).Replace('__SLUG__', $RepoSlug)

$changelog = Get-Content -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Raw
$headings = @([regex]::Matches($changelog, '(?m)^## (?<ver>[1-9][0-9]*(\.[1-9][0-9]*)?)\r?$') | ForEach-Object { $_.Groups['ver'].Value })
$currentIndex = [Array]::IndexOf($headings, $release.Version)
$prevVersion = if ($currentIndex -ge 0 -and ($currentIndex + 1) -lt $headings.Count) { $headings[$currentIndex + 1] } else { '' }

function Resolve-VersionCommit([string]$TargetVersion, [string]$FromRef = 'HEAD')
{
    $tagSha = @(git -C $projectRoot rev-parse -q --verify "refs/tags/v$TargetVersion^{commit}" 2>$null)
    if ($LASTEXITCODE -eq 0 -and $tagSha.Count -gt 0 -and $tagSha[0])
    {
        return $tagSha[0].Trim()
    }

    $candidates = @(git -C $projectRoot log $FromRef --format='%H' -- Directory.Build.props 2>$null)
    foreach ($sha in $candidates)
    {
        if (-not $sha) { continue }
        $propsXml = @(git -C $projectRoot show "$($sha.Trim()):Directory.Build.props" 2>$null) -join "`n"
        if ($propsXml -match "<Version>$([regex]::Escape($TargetVersion))</Version>")
        {
            return $sha.Trim()
        }
    }

    return ''
}

$lines = @(
    $header,
    '',
    '## Изменения',
    '',
    $release.Notes
)

if (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))
{
    $targetRef = $Head
    if (-not $targetRef)
    {
        $resolvedTarget = Resolve-VersionCommit $release.Version 'HEAD'
        $targetRef = if ($resolvedTarget) { $resolvedTarget } else { 'HEAD' }
    }

    $prevSha = if ($prevVersion) { Resolve-VersionCommit $prevVersion $targetRef } else { '' }
    $range = if ($prevSha) { "$prevSha..$targetRef" } else { $targetRef }
    $commits = @(git -C $projectRoot log --no-merges --pretty='- %s' $range 2>$null | Where-Object { $_ -and $_ -notmatch '^- (Версия|Повышена версия до) [0-9]' })
    if ($commits.Count -gt 0)
    {
        $lines += @(
            '',
            '## Технические изменения',
            '',
            '<details>',
            "<summary>Коммиты ($($commits.Count))</summary>",
            ''
        )
        $lines += $commits
        $lines += @(
            '',
            '</details>'
        )
    }

    $compareLine = if ($prevVersion)
    {
        "**Полный список:** $RepoUrl/compare/v$prevVersion...$($release.Tag)"
    }
    else
    {
        "**Полный список:** $RepoUrl/commits/$($release.Tag)"
    }
    $lines += @(
        '',
        $compareLine
    )
}

($lines -join "`n").TrimEnd()
