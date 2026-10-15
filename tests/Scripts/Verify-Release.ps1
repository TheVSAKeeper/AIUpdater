#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixture = Join-Path $projectRoot "artifacts\release-check\$([guid]::NewGuid().ToString('N'))"
$fixtureBuild = Join-Path $fixture 'build'
New-Item -ItemType Directory -Path $fixtureBuild -Force | Out-Null
foreach ($name in @('release-info.ps1', 'bump-version.ps1'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot "build\$name") -Destination (Join-Path $fixtureBuild $name)
}
$propsPath = Join-Path $fixture 'Directory.Build.props'
$changelogPath = Join-Path $fixture 'CHANGELOG.md'
$infoScript = Join-Path $fixtureBuild 'release-info.ps1'
$bumpScript = Join-Path $fixtureBuild 'bump-version.ps1'

function Set-Fixture([string]$Version, [string]$Notes)
{
    [IO.File]::WriteAllText($propsPath, "<Project><PropertyGroup><Version>$Version</Version></PropertyGroup></Project>`r`n", [Text.UTF8Encoding]::new($true))
    [IO.File]::WriteAllText($changelogPath, $Notes, [Text.UTF8Encoding]::new($true))
}

function Assert-Rejected([scriptblock]$Action, [string]$Message)
{
    $before = (Get-FileHash -LiteralPath $propsPath).Hash
    $rejected = $false
    try { & $Action | Out-Null }
    catch
    {
        if ($_.Exception.Message -notlike $Message) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Непригодная версия или заметки были приняты' }
    if ((Get-FileHash -LiteralPath $propsPath).Hash -ne $before) { throw 'Отказ изменил номер версии' }
}

$notes = "# Изменения`r`n`r`n## 3`r`n`r`n- Третья версия`r`n`r`n## 2.1`r`n`r`n- Исправление`r`n`r`n## 2`r`n`r`n- Вторая версия`r`n`r`n## 1`r`n`r`n- Первая версия`r`n"
Set-Fixture '1' $notes
$release = & $infoScript
if ($release.Version -ne '1' -or $release.Tag -ne 'v1' -or $release.Notes -ne '- Первая версия')
{
    throw 'Номер, тег и раздел заметок не совпадают'
}
& $bumpScript | Out-Null
if ((& $infoScript).Version -ne '2') { throw 'Основная версия не повышена до 2' }
& $bumpScript -Version patch | Out-Null
if ((& $infoScript).Version -ne '2.1') { throw 'Не создано исправление 2.1' }
& $bumpScript | Out-Null
if ((& $infoScript).Version -ne '3') { throw 'Основная версия не сбросила номер исправления' }
Assert-Rejected { & $bumpScript -Version 3 } '*должна быть больше*'
Assert-Rejected { & $bumpScript -Version 2 } '*должна быть больше*'
Assert-Rejected { & $bumpScript -Version 4 } '*непустой раздел*'
foreach ($invalid in @('', '0', '01', '1.0', '1.01', '1.2.3', 'v1', '1-beta', '65535', '1.65535', '99999999999999999'))
{
    Assert-Rejected { & $infoScript -Version $invalid } '*'
}
Set-Fixture '1.1' "## 1.2`n`n- Второе исправление`n`n## 1.1`n`n- Первое исправление`n"
& $bumpScript -Version patch | Out-Null
if ((& $infoScript).Tag -ne 'v1.2') { throw 'Номер исправления не повышен' }
Set-Fixture '1' "## 2`n`n`n## 1`n`n- Первая версия`n"
Assert-Rejected { & $bumpScript } '*непустой раздел*'
Set-Fixture '1' "## 1`n`n- Один`n`n## 1`n`n- Два`n"
Assert-Rejected { & $infoScript } '*непустой раздел*'
Set-Fixture '1' $notes
& $bumpScript -Version 3 | Out-Null
if ((& $infoScript).Version -ne '3') { throw 'Явный номер не принят' }
Write-Output 'PASS: vN and vN.P, next/patch/explicit increments, rollback and repeat rejection, required unique notes, invalid numbers, no changes on rejection'
