# Read-only checks: do not call update functions or refresh plugin marketplaces.
function Write-CheckResult {
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

function Compare-CheckVersions {
    param([string]$Stage, $Installed, $Available)
    if ($null -eq $Installed -or $null -eq $Available) {
        Write-CheckResult $Stage 'UNKNOWN' "Version comparison unavailable. Installed: $Installed; latest: $Available" $Installed $Available -Info 'Не удалось получить установленную или доступную версию'
    } elseif ((Compare-PackageVersions "$Installed" "$Available") -lt 0) {
        Write-CheckResult $Stage 'AVAILABLE' "Update available: $Installed -> $Available" $Installed $Available
    } else {
        Write-CheckResult $Stage 'CURRENT' "Up to date: $Installed (latest: $Available)" $Installed $Available
    }
}

function Test-WingetUpdate {
    param([string]$Stage, [string]$PackageId, [string]$Source)
    if (-not (Test-Tool 'winget')) {
        Write-CheckResult $Stage 'UNKNOWN' 'winget is unavailable; versions could not be compared'
        return
    }
    $result = Invoke-CommandCapture -FilePath 'winget' -Arguments @(
        'list', '--id', $PackageId, '--exact', '--source', $Source,
        '--accept-source-agreements', '--disable-interactivity'
    ) -NoLogCommand
    if ($result.ExitCode -eq -1978335212) {
        Write-CheckResult $Stage 'NOT_INSTALLED' 'Application is not installed'
        return
    }
    if ($result.ExitCode -ne 0) { throw "winget list failed: $($result.ExitCode): $($result.Text)" }
    foreach ($line in ($result.Text -split "`r?`n")) {
        if ($line -match "\b$([regex]::Escape($PackageId))\s+([0-9]+(?:\.[0-9]+){1,3})(?:\s+([0-9]+(?:\.[0-9]+){1,3}))?(?:\s+$([regex]::Escape($Source)))?\s*$") {
            $installed = [version]$Matches[1]
            $available = if ($Matches[2]) { [version]$Matches[2] } else { $installed }
            Compare-CheckVersions $Stage $installed $available
            return
        }
    }
    Write-CheckResult $Stage 'UNKNOWN' "Could not parse winget version information: $($result.Text)"
}

function Test-CodexDesktopUpdate {
    param([string]$Stage)
    $installed = Get-CodexVersion
    if ($null -eq $installed) {
        Write-CheckResult $Stage 'NOT_INSTALLED' 'ChatGPT / Codex desktop is not installed'
        return
    }
    try {
        $state = Get-CodexPackagedStoreUpdateState
        if (-not $state.Success) { throw $state.Error }
        $updates = @($state.Updates | Where-Object Name -EQ 'OpenAI.Codex')
        if ($updates.Count -gt 0) {
            Write-CheckResult $Stage 'AVAILABLE' "Microsoft Store reports an update. Installed: $installed; available: $($updates[0].Version)" $installed $updates[0].Version
        } else {
            Write-CheckResult $Stage 'CURRENT' "Microsoft Store reports no updates. Installed: $installed" $installed
        }
    } catch {
        Write-Log "Microsoft Store check unavailable: $($_.Exception.Message)"
        $state = Get-CodexDesktopWingetState
        if ($null -ne $state -and $null -ne $state.Available) {
            Compare-CheckVersions $Stage $installed $state.Available
        } else {
            Write-CheckResult $Stage 'UNKNOWN' "Installed: $installed. Microsoft Store could not confirm whether an update is available" $installed
        }
    }
}

function Test-CliUpdate {
    param([string]$Stage, [string]$Tool, [string]$PackageName, [switch]$NpmPackage)
    if ($NpmPackage) {
        $state = Get-NpmGlobalPackageState $PackageName
        if ($state.Status -ne 'INSTALLED') {
            Write-CheckResult $Stage $state.Status $state.Detail -Info $state.Detail
            return
        }
        $installed = $state.Version
    } else {
        if (-not (Test-Tool $Tool)) {
            Write-CheckResult $Stage 'NOT_INSTALLED' "$Tool is not installed"
            return
        }
        $installed = Get-ToolVersion $Tool
    }
    if ($null -eq $installed) {
        Write-CheckResult $Stage 'ERROR' 'Could not read installed version' -Info 'Не удалось прочитать установленную версию'
        return
    }
    try {
        $available = Get-NpmLatestVersion $PackageName $installed
    } catch {
        Write-CheckResult $Stage 'ERROR' "Latest npm version query failed: $($_.Exception.Message)" $installed -Info 'Не удалось выполнить запрос доступной версии в npm'
        return
    }
    if ($null -eq $available) {
        Write-CheckResult $Stage 'ERROR' 'Could not read latest npm version' $installed -Info 'Не удалось получить доступную версию из npm'
        return
    }
    Compare-CheckVersions $Stage $installed $available
}

function Test-PluginsUpdate {
    param([string]$Stage, [string]$Tool)
    if (-not (Test-Tool $Tool)) {
        Write-CheckResult $Stage 'NOT_INSTALLED' "$Tool is not installed"
        return
    }
    $result = Invoke-CommandCapture -FilePath $Tool -Arguments @('plugin', 'list', '--json') -NoLogCommand
    if ($result.ExitCode -ne 0) { throw "Could not list plugins: $($result.Text)" }
    Write-Log "Installed plugins: $($result.Text)"
    try {
        $plugins = $result.Text | ConvertFrom-Json
        if ($Tool -eq 'codex') {
            if ($null -eq $plugins -or $plugins.installed -isnot [array]) { throw 'Missing installed plugin array' }
            $count = @($plugins.installed | Where-Object { $null -ne $_ }).Count
        } else {
            if (-not $result.Text.TrimStart().StartsWith('[')) { throw 'Expected a plugin array' }
            $count = @($plugins | Where-Object { $null -ne $_ }).Count
        }
        Write-CheckResult $Stage 'DIRECT' 'Plugin comparison requires a marketplace refresh; use the Update action' -Info "Плагинов: $count"
    } catch {
        Write-CheckResult $Stage 'UNKNOWN' "Could not parse installed plugins: $($_.Exception.Message)" -Info 'Не удалось прочитать список плагинов'
    }
}

function Invoke-CheckStage {
    param([string]$Stage)
    switch ($Stage) {
        'Codex desktop' { Test-CodexDesktopUpdate $Stage }
        'Active Codex CLI' { Test-CliUpdate $Stage 'codex' '@openai/codex' }
        'Codex npm package' { Test-CliUpdate $Stage 'codex' '@openai/codex' -NpmPackage }
        'Codex plugins' { Test-PluginsUpdate $Stage 'codex' }
        'Claude Desktop' { Test-WingetUpdate $Stage 'Anthropic.Claude' 'winget' }
        'Claude Code' { Test-CliUpdate $Stage 'claude' '@anthropic-ai/claude-code' }
        'Claude npm package' { Test-CliUpdate $Stage 'claude' '@anthropic-ai/claude-code' -NpmPackage }
        'Claude plugins' { Test-PluginsUpdate $Stage 'claude' }
        'Antigravity Desktop' { Test-WingetUpdate $Stage 'Google.Antigravity' 'winget' }
        'Antigravity CLI' {
            $command = Get-Command agy -ErrorAction SilentlyContinue
            $toolPath = if ($command) { $command.Source } else { Join-Path $env:LOCALAPPDATA 'agy\bin\agy.exe' }
            if (-not (Test-Path -LiteralPath $toolPath)) {
                Write-CheckResult $Stage 'NOT_INSTALLED' 'Antigravity CLI (agy) is not installed'
                return
            }
            try {
                $result = Invoke-CommandCapture -FilePath $toolPath -Arguments @('--version') -NoLogCommand
            } catch {
                Write-CheckResult $Stage 'ERROR' "Could not start agy: $($_.Exception.Message)" -Info 'Не удалось запустить agy для чтения версии'
                return
            }
            if ($result.ExitCode -ne 0) {
                Write-CheckResult $Stage 'ERROR' "Could not read agy version (exit $($result.ExitCode)): $($result.Text)" -Info "Команда agy --version завершилась с кодом $($result.ExitCode)"
                return
            }
            $installed = ConvertTo-VersionOrNull $result.Text
            if ($null -eq $installed) {
                Write-CheckResult $Stage 'ERROR' "Could not parse agy version: $($result.Text)" -Info 'agy запущен, но его версию не удалось разобрать'
            } else {
                Write-CheckResult $Stage 'DIRECT' "Installed agy: $($result.Text). Latest version is checked by its updater" $installed -Info 'Новая версия определяется при запуске обновления'
            }
        }
        default { throw "Unknown stage: $Stage" }
    }
}
