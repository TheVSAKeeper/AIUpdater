#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishRoot = Join-Path $projectRoot 'publish'
$publishDirectory = Join-Path $publishRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$desktopDirectory = [Environment]::GetFolderPath('DesktopDirectory')
$logDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AIUpdater\Logs'
$backupDirectory = Join-Path $projectRoot 'artifacts\desktop-previous'

# Publish to a fresh directory so an open application can keep running.
& dotnet publish (Join-Path $projectRoot 'src\AIUpdater\AIUpdater.csproj') -c Release --self-contained false -o $publishDirectory
if ($LASTEXITCODE -ne 0)
{
    throw "Ошибка публикации: $LASTEXITCODE"
}
$executable = Join-Path $publishDirectory 'AIUpdater.exe'
if (-not (Test-Path -LiteralPath $executable))
{
    throw "Приложение не найдено: $executable"
}
foreach ($scriptName in @('Update-Codex.ps1', 'Check-Updates.ps1', 'Manage-Components.ps1'))
{
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $scriptName)))
    {
        throw "В публикации отсутствует скрипт: $scriptName"
    }
}

$shortcutShell = New-Object -ComObject WScript.Shell
$shortcut = $shortcutShell.CreateShortcut((Join-Path $desktopDirectory 'AIUpdater.lnk'))
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $publishDirectory
$shortcut.IconLocation = "$executable,0"
$shortcut.Description = 'Обновление инструментов OpenAI, Anthropic и Google'
$shortcut.Save()

$updateScript = Join-Path $PSScriptRoot 'publish-local.ps1'
$command = @"
@echo off
chcp 65001 >nul
pwsh -NoProfile -ExecutionPolicy Bypass -File "$updateScript"
pause
"@
[IO.File]::WriteAllText((Join-Path $desktopDirectory 'Обновить AIUpdater.cmd'), $command + "`r`n",[Text.UTF8Encoding]::new($false))

# Preserve old desktop files while moving them out of the desktop.
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
foreach ($logName in @('Update-Codex-last.log', 'Update-Codex-OpenAI-last.log', 'Update-Codex-Anthropic-last.log', 'Update-Codex-Google-last.log'))
{
    $source = Join-Path $desktopDirectory $logName
    if (-not (Test-Path -LiteralPath $source))
    {
        continue
    }
    $destination = Join-Path $logDirectory $logName
    if (Test-Path -LiteralPath $destination)
    {
        $destination = Join-Path $logDirectory ("{0}-{1}.log" -f [IO.Path]::GetFileNameWithoutExtension($logName),[guid]::NewGuid().ToString('N'))
    }
    Move-Item -LiteralPath $source -Destination $destination
}

foreach ($oldName in @('AIUpdater', 'AIUpdater.exe.lnk', 'Update-Codex.ps1'))
{
    $source = Join-Path $desktopDirectory $oldName
    if (-not (Test-Path -LiteralPath $source))
    {
        continue
    }
    $resolvedSource = [IO.Path]::GetFullPath($source)
    if ([IO.Path]::GetDirectoryName($resolvedSource) -ne [IO.Path]::GetFullPath($desktopDirectory))
    {
        throw "Неожиданный путь: $resolvedSource"
    }
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $destination = Join-Path $backupDirectory $oldName
    if (Test-Path -LiteralPath $destination)
    {
        $destination += '-' + [guid]::NewGuid().ToString('N')
    }
    Move-Item -LiteralPath $resolvedSource -Destination $destination
}

Write-Host "AIUpdater: $executable"
Write-Host 'На рабочем столе созданы AIUpdater.lnk и Обновить AIUpdater.cmd'
