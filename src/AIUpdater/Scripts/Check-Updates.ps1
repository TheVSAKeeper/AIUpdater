function Write-CheckResult
{
    param([string]$Stage, [string]$Result, [string]$Detail, $Installed = $null, $Available = $null, [string]$Info = $null)
    Write-Log $Detail
    Write-Log "=== CHECK ${Result}: $Stage ==="
    $payload = [ordered]@{
        Stage = $Stage
        Result = $Result
        Installed = if ($null -eq $Installed) { $null } else { "$Installed" }
        Available = if ($null -eq $Available) { $null } else { "$Available" }
        Info = $Info
    } | ConvertTo-Json -Compress
    Write-Log "UI_RESULT $payload"
}

function Compare-CheckVersions
{
    param([string]$Stage, $Installed, $Available)
    if ($null -eq $Installed -or $null -eq $Available)
    {
        Write-CheckResult $Stage 'UNKNOWN' "Version comparison unavailable. Installed: $Installed; latest: $Available" $Installed $Available -Info 'Не удалось получить установленную или доступную версию'
    }
    elseif ((Compare-PackageVersions "$Installed" "$Available") -lt 0)
    {
        Write-CheckResult $Stage 'AVAILABLE' "Update available: $Installed -> $Available" $Installed $Available
    }
    else
    {
        Write-CheckResult $Stage 'CURRENT' "Up to date: $Installed (latest: $Available)" $Installed $Available
    }
}

function Test-WingetUpdate
{
    param([string]$Stage, [string]$PackageId, [string]$Source)
    if (-not (Test-Tool 'winget'))
    {
        Write-CheckResult $Stage 'ERROR' 'winget is unavailable; versions could not be compared' -Info 'winget недоступен'
        return
    }
    $state = Get-WingetPackageState -PackageId $PackageId -Source $Source
    switch ($state.Status)
    {
        'NOT_INSTALLED' {
            Write-CheckResult $Stage 'NOT_INSTALLED' 'Application is not installed'
        }
        'COMMAND_FAILED' {
            throw "winget list failed: $( $state.ExitCode ): $( $state.Text )"
        }
        'INSTALLED' {
            if ($null -eq $state.Available -and $Source -eq 'msstore')
            {
                Write-CheckResult $Stage 'UNKNOWN' 'Microsoft Store did not expose an available version in winget list' $state.Installed -Info 'winget list не подтвердил доступную версию Microsoft Store'
                return
            }
            $available = if ($null -ne $state.Available) { $state.Available } else { $state.Installed }
            Compare-CheckVersions $Stage $state.Installed $available
        }
        default {
            Write-CheckResult $Stage 'ERROR' "Could not parse winget version information: $( $state.Text )" -Info 'Не удалось прочитать версии из ответа winget'
        }
    }
}

function Test-CodexDesktopUpdate
{
    param([string]$Stage)
    $installed = Get-CodexVersion
    if ($null -eq $installed)
    {
        Write-CheckResult $Stage 'NOT_INSTALLED' 'ChatGPT / Codex desktop is not installed'
        return
    }
    try
    {
        $state = Get-CodexPackagedStoreUpdateState
        if (-not $state.Success)
        {
            throw $state.Error
        }
        $definition = Get-StageDefinition $Stage
        $updates = @($state.Updates | Where-Object Name -EQ $definition.AppxName)
        if ($updates.Count -gt 0)
        {
            $reportedVersion = "$( $updates[0].Version )"
            $available = if (-not [string]::IsNullOrWhiteSpace($reportedVersion) -and $reportedVersion -ne "$installed") { $reportedVersion } else { $null }
            $detail = if ($null -ne $available)
            {
                "Microsoft Store reports an update. Installed: $installed; available: $available"
            }
            else
            {
                "Microsoft Store reports an update for installed version $installed"
            }
            $info = if ($null -eq $available) { 'Microsoft Store не передаёт номер новой версии до установки' } else { $null }
            Write-CheckResult $Stage 'AVAILABLE' $detail $installed $available -Info $info
        }
        else
        {
            Write-CheckResult $Stage 'CURRENT' "Microsoft Store reports no updates. Installed: $installed" $installed
        }
    }
    catch
    {
        Write-Log "Microsoft Store check unavailable: $( $_.Exception.Message )"
        $state = Get-CodexDesktopWingetState
        if ($null -ne $state -and $null -ne $state.Available)
        {
            Compare-CheckVersions $Stage $installed $state.Available
        }
        else
        {
            Write-CheckResult $Stage 'UNKNOWN' "Installed: $installed. Microsoft Store could not confirm whether an update is available" $installed
        }
    }
}

function Test-CliUpdate
{
    param([string]$Stage, [string]$Tool, [string]$PackageName, [switch]$NpmPackage)
    if ($NpmPackage)
    {
        try
        {
            $state = Get-NpmGlobalPackageState $PackageName
        }
        catch
        {
            Write-CheckResult $Stage 'ERROR' "Could not inspect global npm packages: $( $_.Exception.Message )" -Info 'Не удалось выполнить проверку или разобрать список глобальных пакетов npm'
            return
        }
        if ($state.Status -ne 'INSTALLED')
        {
            Write-CheckResult $Stage $state.Status $state.Detail -Info $state.Detail
            return
        }
        $installed = $state.Version
    }
    else
    {
        if (-not (Test-Tool $Tool))
        {
            Write-CheckResult $Stage 'NOT_INSTALLED' "$Tool is not installed"
            return
        }
        $installed = Get-ToolVersion $Tool
    }
    if ($null -eq $installed)
    {
        Write-CheckResult $Stage 'ERROR' 'Could not read installed version' -Info 'Не удалось прочитать установленную версию'
        return
    }
    try
    {
        $available = Get-NpmLatestVersion $PackageName $installed
    }
    catch
    {
        Write-CheckResult $Stage 'ERROR' "Latest npm version query failed: $( $_.Exception.Message )" $installed -Info 'Не удалось выполнить запрос доступной версии в npm'
        return
    }
    if ($null -eq $available)
    {
        Write-CheckResult $Stage 'ERROR' 'Could not read latest npm version' $installed -Info 'Не удалось получить доступную версию из npm'
        return
    }
    Compare-CheckVersions $Stage $installed $available
}

function Get-AgyLatestVersion
{
    $architecture = if ($env:PROCESSOR_ARCHITEW6432)
    {
        $env:PROCESSOR_ARCHITEW6432
    }
    else
    {
        $env:PROCESSOR_ARCHITECTURE
    }
    $platform = switch ($architecture)
    {
        'AMD64' { 'windows_amd64' }
        'ARM64' { 'windows_arm64' }
        default { throw "Unsupported architecture for agy update check: $architecture" }
    }
    $response = Invoke-WebRequest -Uri "https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/$platform.json" -UseBasicParsing -TimeoutSec 20 -ErrorAction Stop
    $content = [string]$response.Content
    if (-not $content.TrimStart().StartsWith('{'))
    {
        throw 'Expected an agy release manifest object'
    }
    $manifest = $content | ConvertFrom-Json -ErrorAction Stop
    if ($manifest -isnot [pscustomobject] -or $manifest.version -isnot [string] -or
        $manifest.version -cnotmatch '\A[0-9]+(?:\.[0-9]+){1,3}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z')
    {
        throw 'Invalid agy release manifest version'
    }
    return $manifest.version
}

function Test-PluginsUpdate
{
    param([string]$Stage, [string]$Tool)
    if (-not (Test-Tool $Tool))
    {
        Write-CheckResult $Stage 'NOT_INSTALLED' "$Tool is not installed"
        return
    }
    $result = Invoke-CommandCapture -FilePath $Tool -Arguments @('plugin', 'list', '--json') -NoLogCommand
    if ($result.ExitCode -ne 0)
    {
        throw "Could not list plugins: $( $result.Text )"
    }
    Write-Log "Installed plugins: $( $result.Text )"
    try
    {
        $plugins = $result.Text | ConvertFrom-Json
        if ($Tool -eq 'codex')
        {
            if ($null -eq $plugins -or $plugins.installed -isnot [array])
            {
                throw 'Missing installed plugin array'
            }
            $count = @($plugins.installed | Where-Object { $null -ne $_ }).Count
        }
        else
        {
            if (-not $result.Text.TrimStart().StartsWith('['))
            {
                throw 'Expected a plugin array'
            }
            $count = @($plugins | Where-Object { $null -ne $_ }).Count
        }
        Write-CheckResult $Stage 'DIRECT' 'Plugin comparison requires a marketplace refresh; use the Update action' -Info "Плагинов: $count"
    }
    catch
    {
        Write-CheckResult $Stage 'UNKNOWN' "Could not parse installed plugins: $( $_.Exception.Message )" -Info 'Не удалось прочитать список плагинов'
    }
}

function Invoke-CheckStage
{
    param([string]$Stage)
    $definition = Get-StageDefinition $Stage
    if ($definition.Kind -eq 'Development')
    {
        $installed = $null
        try
        {
            if ($definition.Tool) { $installed = Get-ToolVersion $definition.Tool }
            $owner = Get-DevelopmentToolOwner $Stage
            if ($owner -eq 'missing')
            {
                Write-CheckResult $Stage 'NOT_INSTALLED' "$Stage is not installed"
                return
            }
            if ($owner -eq 'nvm')
            {
                if ($null -eq $installed) { $installed = Get-ToolVersion 'node' }
                if ($null -eq $installed) { throw 'Could not read the active Node version' }
                try
                {
                    $available = Get-NodeLatestVersion $installed
                }
                catch
                {
                    Write-CheckResult $Stage 'ERROR' $_.Exception.Message $installed -Info $_.Exception.Message
                    return
                }
                Compare-CheckVersions $Stage $installed $available
                return
            }
            if ($Stage -eq '.NET SDK 10')
            {
                $sdks = @(Get-DevelopmentSdkVersions)
                if (-not $sdks.Count)
                {
                    Write-CheckResult $Stage 'NOT_INSTALLED' '.NET SDK 10 is not installed'
                    return
                }
                $installed = $sdks | Sort-Object { [version]$_ } -Descending | Select-Object -First 1
                $plan = @(Get-DevelopmentSdkPlan $sdks)
                $updates = @($plan | Where-Object Update)
                $detail = ($plan | ForEach-Object { "$($_.Installed) -> $($_.Available)" }) -join '; '
                $status = if ($updates.Count) { 'AVAILABLE' } else { 'CURRENT' }
                Write-CheckResult $Stage $status "SDK feature bands: $detail" ($plan.Installed -join '; ') ($plan.Available -join '; ') -Info $detail
                return
            }
            $package = Get-DevelopmentPackage $Stage
            Test-WingetUpdate $Stage $package[0] $package[1]
        }
        catch
        {
            Write-CheckResult $Stage 'ERROR' $_.Exception.Message $installed -Info $_.Exception.Message
        }
        return
    }
    switch ($definition.Kind)
    {
        'StoreDesktop' {
            Test-CodexDesktopUpdate $Stage
        }
        'WingetDesktop' {
            Test-WingetUpdate $Stage $definition.PackageId $definition.Source
        }
        { $_ -in @('NpmCli', 'NativeCli') } {
            Test-CliUpdate $Stage $definition.Tool $definition.NpmPackage
        }
        'NpmGlobal' {
            Test-CliUpdate $Stage $definition.Tool $definition.NpmPackage -NpmPackage
        }
        'Plugins' {
            Test-PluginsUpdate $Stage $definition.Tool
        }
        'AgyCli' {
            $toolPath = Get-AgyExecutablePath
            if (-not (Test-Path -LiteralPath $toolPath))
            {
                Write-CheckResult $Stage 'NOT_INSTALLED' 'Antigravity CLI (agy) is not installed'
                return
            }
            try
            {
                $result = Invoke-CommandCapture -FilePath $toolPath -Arguments @('--version') -NoLogCommand
            }
            catch
            {
                Write-CheckResult $Stage 'ERROR' "Could not start agy: $( $_.Exception.Message )" -Info 'Не удалось запустить agy для чтения версии'
                return
            }
            if ($result.ExitCode -ne 0)
            {
                Write-CheckResult $Stage 'ERROR' "Could not read agy version (exit $( $result.ExitCode )): $( $result.Text )" -Info "Команда agy --version завершилась с кодом $( $result.ExitCode )"
                return
            }
            $installed = ConvertTo-VersionOrNull $result.Text
            if ($null -eq $installed)
            {
                Write-CheckResult $Stage 'ERROR' "Could not parse agy version: $( $result.Text )" -Info 'agy запущен, но его версию не удалось разобрать'
                return
            }
            try
            {
                $available = Get-AgyLatestVersion
                Compare-CheckVersions $Stage $installed $available
            }
            catch
            {
                Write-CheckResult $Stage 'ERROR' "Latest agy version query failed: $( $_.Exception.Message )" $installed -Info 'Не удалось получить доступную версию agy из манифеста'
                return
            }
        }
    }
}
