param([switch]$Apply)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
$prepared = Join-Path $repo '.kc\scratch\development-environment-normalization'
$artifacts = Join-Path $repo '.kc\artifacts\development-environment-normalization'
$legacyNvm = 'C:\Users\admin\AppData\Local\nvm'
$newNvm = 'C:\Users\admin\AppData\Local\Author Software\nvm'
$nativePowerShell = 'C:\Program Files\PowerShell\7\pwsh.exe'
$nativeCodex = 'C:\Users\admin\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe'
$installer = Join-Path $prepared 'nvm-2.0.1-x64-setup.exe'
$checkpointPath = Join-Path $artifacts 'deferred-checkpoint.json'
$remaining = @(
    'claude-sync и Python 3.13: прежние операции отклонены автоматической политикой; скрипт их не повторяет.',
    'PowerShell 7.5.5: устаревшая ARP-запись оставлена после отказа политики; Visual Studio Community требует восстановления состояния Installer.',
    '.NET SDK и runtimes: сохранить версии и global.json; SDK 10 обслуживается отдельной карточкой обновлятора.'
)

function Invoke-Native
{
    param([string]$File, [string[]]$Arguments, [int[]]$AllowedExitCodes = @(0))
    $output = @(& $File @Arguments 2>&1)
    $code = $LASTEXITCODE
    $text = ($output | ForEach-Object { "$_" }) -join [Environment]::NewLine
    Add-Content -LiteralPath $script:log -Value ("`n> $File $($Arguments -join ' ')`n$text`nExitCode=$code") -Encoding UTF8
    Write-Host $text
    if ($code -notin $AllowedExitCodes) { throw "$File завершился с кодом $code. Журнал: $script:log" }
    return $text
}

function Update-ProcessPath
{
    $env:PATH = [Environment]::ExpandEnvironmentVariables(
        [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
        [Environment]::GetEnvironmentVariable('PATH', 'User'))
}

function Get-BlockingProcesses
{
    param([switch]$IncludePowerShell)
    Get-Process -Name node,nvm,pwsh,WindowsTerminal -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -ne 'pwsh' -or ($IncludePowerShell -and (-not $_.Path -or
        $_.Path.StartsWith('C:\Users\admin\.dotnet\tools\', [StringComparison]::OrdinalIgnoreCase)))
    } | Select-Object Id,ProcessName,Path
}

function Assert-NoBlockingProcesses
{
    param([switch]$IncludePowerShell)
    $blocked = @(Get-BlockingProcesses -IncludePowerShell:$IncludePowerShell)
    if ($blocked.Count)
    {
        $blocked | Format-Table -AutoSize | Out-Host
        throw 'Закройте перечисленные процессы и повторите запуск. Скрипт не завершает процессы.'
    }
}

if ($env:USERPROFILE -ne 'C:\Users\admin') { throw 'Сценарий подготовлен только для профиля C:\Users\admin.' }
if (-not (Test-Path -LiteralPath $nativePowerShell)) { throw 'Отсутствует штатный PowerShell 7.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($Apply -and $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
{
    throw 'Запустите без повышения прав: установщик NVM рассчитан на обычного пользователя.'
}
foreach ($command in @('dotnet','winget')) { $null = Get-Command $command -ErrorAction Stop }
$nvm = if (Test-Path -LiteralPath "$newNvm\nvm.exe") { "$newNvm\nvm.exe" } else { "$legacyNvm\nvm.exe" }
if (-not (Test-Path -LiteralPath $nvm)) { throw 'Не найден ожидаемый NVM v1 или v2.' }
if ($nvm -eq "$legacyNvm\nvm.exe")
{
    if ((Get-FileHash -LiteralPath $installer).Hash -ne '2BA6AB6290FDC7D1E608E76C97577CA6E1DF772B2D59AB8EBC01E10788DE8E57')
    { throw 'Не совпала контрольная сумма подготовленного установщика NVM.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Author Software Inc*')
    { throw 'Подпись установщика NVM недействительна.' }
}
Write-Host 'План: удалить global-tool PowerShell; мигрировать NVM; обновить Node в текущей major; обновить Terminal через msstore.'
$remaining | ForEach-Object { Write-Host $_ }
Assert-NoBlockingProcesses
if (-not $Apply)
{
    Write-Host 'Проверка готовности пройдена. Для выполнения добавьте -Apply. Изменений нет.'
    return
}

$run = Join-Path $artifacts ('deferred-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$null = New-Item -ItemType Directory -Path $run
$script:log = Join-Path $run 'commands.log'
$result = [ordered]@{ Started = Get-Date -Format o; Status = 'Running'; Completed = @(); Remaining = $remaining; Error = $null }
$pins = @('C:\Sources\TheVSAKeeper\SpaceSnoop\global.json', (Join-Path $repo 'global.json'))
$pinHashes = @{}
try
{
    foreach ($pin in $pins)
    {
        if (-not (Test-Path -LiteralPath $pin)) { throw "Не найден закреплённый global.json: $pin" }
        $pinHashes[$pin] = (Get-FileHash -LiteralPath $pin).Hash
    }
    foreach ($scope in @('User','Machine'))
    {
        $hive = if ($scope -eq 'User') { [Microsoft.Win32.Registry]::CurrentUser } else { [Microsoft.Win32.Registry]::LocalMachine }
        $keyPath = if ($scope -eq 'User') { 'Environment' } else { 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment' }
        $key = $hive.OpenSubKey($keyPath)
        try
        {
            [pscustomobject]@{ Scope = $scope; Kind = "$($key.GetValueKind('Path'))"; Value = $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run "path-$scope-before.json") -Encoding UTF8
        }
        finally { $key.Dispose() }
    }
    $checkpoint = if (Test-Path -LiteralPath $checkpointPath) { Get-Content -LiteralPath $checkpointPath -Raw | ConvertFrom-Json } else { $null }
    $inventory = Invoke-Native $nvm @('list')
    if ($checkpoint -and -not $checkpoint.Completed)
    {
        $active = [version]$checkpoint.Active
        $savedVersions = @($checkpoint.Versions)
    }
    else
    {
        $current = Invoke-Native $nvm @('current')
        if ($current.Trim() -notmatch '\Av?(\d+\.\d+\.\d+)\z') { throw 'Не удалось определить текущую Node.' }
        $active = [version]$Matches[1]
        $savedVersions = @([regex]::Matches($inventory, '\b\d+\.\d+\.\d+\b') | ForEach-Object Value | Select-Object -Unique)
        [pscustomobject]@{ Active = "$active"; Versions = $savedVersions; Completed = $false } |
            ConvertTo-Json | Set-Content -LiteralPath $checkpointPath -Encoding UTF8
    }
    $inventory | Set-Content -LiteralPath (Join-Path $run 'nvm-before.txt') -Encoding UTF8
    foreach ($file in @('settings.txt','config.json'))
    {
        $source = Join-Path (Split-Path -Parent $nvm) $file
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $run }
    }
    $tools = Invoke-Native 'dotnet' @('tool','list','--global')
    $powerShellDeferred = $false
    if ($tools -match '(?im)^powershell\s')
    {
        if ($tools -notmatch '(?im)^powershell\s+7\.6\.3\s')
        { throw 'Версия PowerShell global tool изменилась после аудита. Проверьте её и подготовьте новую резервную копию.' }
        $rollback = Join-Path $artifacts 'powershell-tool-rollback\powershell.7.6.3.nupkg'
        if (-not (Test-Path -LiteralPath $rollback) -or (Get-Item -LiteralPath $rollback).Length -ne 50150002)
        { throw 'Не найдена подготовленная резервная копия global-tool PowerShell.' }
        $powerShellBlockers = @(Get-BlockingProcesses -IncludePowerShell | Where-Object ProcessName -eq 'pwsh')
        if ($powerShellBlockers.Count)
        {
            $powerShellDeferred = $true
            $powerShellBlockers | Format-Table -AutoSize | Out-Host
            $message = 'PowerShell global tool используется или путь процесса недоступен. Удаление отложено; NVM/Node/Terminal выполняются независимо.'
            Write-Host $message
            $result.Remaining += $message
        }
        else
        {
            Assert-NoBlockingProcesses -IncludePowerShell
            $null = Invoke-Native 'dotnet' @('tool','uninstall','--global','PowerShell')
        }
    }
    if (-not $powerShellDeferred) { $result.Completed += 'PowerShell global tool removed or already absent' }
    if ($nvm -eq "$legacyNvm\nvm.exe")
    {
        Assert-NoBlockingProcesses
        $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/OFFICIALNODE=ignore',"/LOG=`"$run\nvm-install.log`"") -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Установщик NVM: $($process.ExitCode)" }
        $nvm = "$newNvm\nvm.exe"
        if (-not (Test-Path -LiteralPath $nvm)) { throw 'После установки не найден NVM v2.' }
    }
    $env:NVM_HOME = $newNvm
    Update-ProcessPath
    $null = Invoke-Native $nvm @('config','set','auto_install=false')
    $null = Invoke-Native $nvm @('use',"$active",'--no-install')
    $after = Invoke-Native $nvm @('list')
    foreach ($saved in $savedVersions)
    {
        if ($after -notmatch ('(?<![\d.])' + [regex]::Escape($saved) + '(?![\d.])'))
        { throw "При миграции не найдена сохранённая Node $saved. Остановлено до обновления Node." }
    }
    $result.Completed += 'NVM migrated; existing Node versions retained; auto_install disabled'
    $releases = Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json' -TimeoutSec 30
    $latest = @($releases | Where-Object { $_.version -match "\Av$($active.Major)\.[0-9]+\.[0-9]+\z" } |
        ForEach-Object { [version]$_.version.Substring(1) } | Sort-Object -Descending | Select-Object -First 1)
    if (-not $latest.Count) { throw "В официальном индексе нет Node $($active.Major)." }
    $target = if ($latest[0] -gt $active) { $latest[0] } else { $active }
    if ($target -gt $active)
    {
        Assert-NoBlockingProcesses
        $null = Invoke-Native $nvm @('install',"$target")
        $null = Invoke-Native $nvm @('use',"$target",'--no-install')
    }
    Update-ProcessPath
    $nodeVersion = Invoke-Native 'node' @('--version')
    if ($nodeVersion.Trim() -ne "v$target") { throw "Ожидалась Node $target, получено $nodeVersion" }
    $null = Invoke-Native 'npm' @('--version')
    $result.Completed += "Node $target and npm verified"
    Assert-NoBlockingProcesses
    $null = Invoke-Native 'winget' @('upgrade','--id','9N0DX20HK701','-e','--source','msstore','--accept-source-agreements','--accept-package-agreements','--disable-interactivity') @(0,-1978335189)
    $terminal = @(Get-AppxPackage -Name Microsoft.WindowsTerminal | Select-Object Name,Version,InstallLocation)
    if (-not $terminal.Count) { throw 'После winget не найдена регистрация Windows Terminal.' }
    $terminal | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'terminal-after.json') -Encoding UTF8
    $result.Completed += 'Terminal Store update command succeeded or reported no applicable upgrade'
    $toolsAfter = Invoke-Native 'dotnet' @('tool','list','--global')
    if (-not $powerShellDeferred -and $toolsAfter -match '(?im)^powershell\s') { throw 'PowerShell global tool остался установлен.' }
    $null = Invoke-Native $nativePowerShell @('-NoProfile','-Command','$PSVersionTable.PSVersion.ToString()')
    $userKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    try
    {
        $raw = $userKey.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $kind = $userKey.GetValueKind('Path')
        [pscustomobject]@{ Value = $raw; Kind = "$kind" } | ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $run 'path-User-after-installers.json') -Encoding UTF8
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $candidates = @($raw -split ';')
        if (Test-Path -LiteralPath $nativeCodex) { $candidates = @((Split-Path -Parent $nativeCodex)) + $candidates }
        $entries = @($candidates | Where-Object {
            $_ -eq '' -or $seen.Add([Environment]::ExpandEnvironmentVariables($_.Trim().Trim('"')).TrimEnd('\'))
        })
        $deduplicated = $entries -join ';'
        if ($deduplicated -cne $raw) { $userKey.SetValue('Path', $deduplicated, $kind) }
        if ($userKey.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -cne $deduplicated)
        { throw 'Не удалось проверить User PATH после удаления повторов.' }
    }
    finally { $userKey.Dispose() }
    Update-ProcessPath
    if (Test-Path -LiteralPath $nativeCodex)
    {
        if ((Get-Command codex -ErrorAction Stop).Source -ne $nativeCodex)
        { throw 'Команда codex перехвачена другой установкой. Проверьте PATH и профиль оболочки.' }
        $null = Invoke-Native 'codex' @('--version')
        $result.Completed += 'Native Codex takes precedence over NVM shims and launches successfully'
    }
    [pscustomobject]@{ Active = "$target"; Versions = $savedVersions; Completed = $true } |
        ConvertTo-Json | Set-Content -LiteralPath $checkpointPath -Encoding UTF8
    $result.Completed += 'User PATH duplicates removed; registry type preserved'
    $result.Status = 'CompletedWithRemainingWork'
}
catch
{
    $result.Status = 'Failed'
    $result.Error = "$_"
    throw
}
finally
{
    foreach ($pin in $pinHashes.Keys)
    {
        if (-not (Test-Path -LiteralPath $pin) -or (Get-FileHash -LiteralPath $pin).Hash -ne $pinHashes[$pin])
        { $result.Status = 'Failed'; $result.Error = "Изменился global.json: $pin" }
    }
    $result['Finished'] = Get-Date -Format o
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding UTF8
    Write-Host "Результат и резервные копии: $run"
}
if ($result.Status -eq 'Failed') { throw $result.Error }
Write-Host 'Шаги NVM/Node/Terminal выполнены. Откройте новую оболочку для обновления PATH. Оставшиеся действия:'
$result.Remaining | ForEach-Object { Write-Host $_ }
