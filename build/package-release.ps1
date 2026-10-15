#requires -Version 7.0
[CmdletBinding()]
param([string]$Output)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$release = & (Join-Path $PSScriptRoot 'release-info.ps1')
if (-not $Output)
{
    $Output = "artifacts\release\$($release.Tag)-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
}
$outputPath = [IO.Path]::GetFullPath($Output, $projectRoot)
if (-not $outputPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
{
    throw 'Каталог сборки должен находиться внутри репозитория'
}
if (Test-Path -LiteralPath $outputPath)
{
    throw "Каталог уже существует: $outputPath"
}
$dist = Join-Path $outputPath 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
foreach ($portable in @($false, $true))
{
    $edition = if ($portable) { 'portable' } else { 'framework-dependent' }
    $directory = Join-Path $outputPath $edition
    $arguments = @(
        'publish', (Join-Path $projectRoot 'src\AIUpdater\AIUpdater.csproj'),
        '-c', 'Release', '-r', 'win-x64', '-o', $directory,
        '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false', '--nologo'
    )
    if ($portable)
    {
        $arguments += @('--self-contained', '-p:IncludeNativeLibrariesForSelfExtract=true')
    }
    else
    {
        $arguments += '--no-self-contained'
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Ошибка сборки $edition" }
    foreach ($name in @('AIUpdater.exe', 'Update-Codex.ps1', 'Check-Updates.ps1', 'Manage-Components.ps1'))
    {
        if (-not (Test-Path -LiteralPath (Join-Path $directory $name)))
        {
            throw "В сборке $edition отсутствует $name"
        }
    }
    $productVersion = (Get-Item -LiteralPath (Join-Path $directory 'AIUpdater.exe')).VersionInfo.ProductVersion
    if ($productVersion -ne $release.Version)
    {
        throw "Версия exe '$productVersion' отличается от $($release.Version)"
    }
    $suffix = if ($portable) { '-portable' } else { '' }
    $archiveName = "AIUpdater-$($release.Tag)-win-x64$suffix.zip"
    Compress-Archive -Path (Join-Path $directory '*') -DestinationPath (Join-Path $dist $archiveName)
}
$sums = foreach ($file in Get-ChildItem -LiteralPath $dist -Filter '*.zip' | Sort-Object Name)
{
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
$notes = @"
# AIUpdater $($release.Tag)

## Скачать

| Архив | Требования |
|-------|------------|
| ``AIUpdater-$($release.Tag)-win-x64.zip`` | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| ``AIUpdater-$($release.Tag)-win-x64-portable.zip`` | Рантайм включён в архив |

Распакуй архив целиком и запусти ``AIUpdater.exe``. Три PowerShell-скрипта должны оставаться рядом.
Оба варианта хранят результаты и журналы в ``%LOCALAPPDATA%\AIUpdater\Logs``.
Контрольные суммы архивов находятся в ``SHA256SUMS.txt``.

## Изменения

$($release.Notes)
"@
[IO.File]::WriteAllText((Join-Path $outputPath 'release-notes.md'), $notes + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Архивы $($release.Tag): $dist"
