# Read-only checks: do not call update functions or refresh plugin marketplaces.
function Write-CheckResult {
    param([string]$Stage, [string]$Result, [string]$Detail)
    Write-Log $Detail
    Write-Log "=== CHECK ${Result}: $Stage ==="
}

function Compare-CheckVersions {
    param([string]$Stage, $Installed, $Available)
    if ($null -eq $Installed -or $null -eq $Available) {
        Write-CheckResult $Stage 'UNKNOWN' "Version comparison unavailable. Installed: $Installed; latest: $Available"
    } elseif ((Compare-PackageVersions "$Installed" "$Available") -lt 0) {
        Write-CheckResult $Stage 'AVAILABLE' "Update available: $Installed -> $Available"
    } else {
        Write-CheckResult $Stage 'CURRENT' "Up to date: $Installed (latest: $Available)"
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
            Write-CheckResult $Stage 'AVAILABLE' "Microsoft Store reports an update. Installed: $installed; available: $($updates[0].Version)"
        } else {
            Write-CheckResult $Stage 'CURRENT' "Microsoft Store reports no updates. Installed: $installed"
        }
    } catch {
        Write-Log "Microsoft Store check unavailable: $($_.Exception.Message)"
        $state = Get-CodexDesktopWingetState
        if ($null -ne $state -and $null -ne $state.Available) {
            Compare-CheckVersions $Stage $installed $state.Available
        } else {
            Write-CheckResult $Stage 'UNKNOWN' "Installed: $installed. Microsoft Store could not confirm whether an update is available"
        }
    }
}

function Test-CliUpdate {
    param([string]$Stage, [string]$Tool, [string]$PackageName, [switch]$NpmPackage)
    if ($NpmPackage) {
        if (-not (Test-Tool 'npm')) {
            Write-CheckResult $Stage 'UNKNOWN' 'npm is unavailable'
            return
        }
        $installed = Get-NpmGlobalPackageVersion $PackageName
    } else {
        if (-not (Test-Tool $Tool)) {
            Write-CheckResult $Stage 'NOT_INSTALLED' "$Tool is not installed"
            return
        }
        $installed = Get-ToolVersion $Tool
    }
    if ($NpmPackage -and $null -eq $installed) {
        Write-CheckResult $Stage 'UNKNOWN' 'Global npm package is missing or its version could not be read'
        return
    }
    $available = Get-NpmLatestVersion $PackageName $installed
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
    Write-CheckResult $Stage 'UNKNOWN' 'Installed plugin list read. Newer versions cannot be confirmed without refreshing marketplaces; refresh skipped in check-only mode'
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
            $result = Invoke-CommandCapture -FilePath $toolPath -Arguments @('--version') -NoLogCommand
            if ($result.ExitCode -ne 0) { throw "Could not read agy version: $($result.Text)" }
            Write-CheckResult $Stage 'UNKNOWN' "Installed agy: $($result.Text). Latest version cannot be confirmed without invoking its updater"
        }
        default { throw "Unknown stage: $Stage" }
    }
}
