param(
    [ValidateSet(
        'Codex desktop',
        'Active Codex CLI',
        'Codex npm package',
        'Codex plugins',
        'Claude Desktop',
        'Claude Code',
        'Claude npm package',
        'Claude plugins',
        'Antigravity Desktop',
        'Antigravity CLI'
    )]
    [string]$OnlyStage,
    [ValidateSet('OpenAI', 'Anthropic', 'Google')]
    [string]$Vendor,
    [switch]$CheckOnly,
    [ValidateSet('Update', 'Install', 'Uninstall')]
    [string]$Action = 'Update',
    [string]$LogPath = (Join-Path $PSScriptRoot 'Update-Codex-last.log')
)

$ErrorActionPreference = 'Stop'
if ($Action -ne 'Update' -and (-not $OnlyStage -or $CheckOnly))
{
    throw 'Install and Uninstall require OnlyStage and cannot be combined with CheckOnly'
}

$productId = '9PLM9XGG6VKS'
$packageName = 'OpenAI.Codex'
$appId = 'OpenAI.Codex_2p2nqsd0c76g0!App'
$claudeDesktopPackageId = 'Anthropic.Claude'
$claudeDesktopAppId = 'Claude_pzs8sxrjxfjjc!Claude'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

function Test-Admin
{
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $CheckOnly -and -not (Test-Admin))
{
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`""
    )
    if ($OnlyStage)
    {
        $arguments += @('-OnlyStage', "`"$OnlyStage`"")
    }
    if ($Vendor)
    {
        $arguments += @('-Vendor', "`"$Vendor`"")
    }
    $arguments += @('-Action', $Action)
    $arguments += @('-LogPath', "`"$LogPath`"")
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden
    exit
}

Set-Content -LiteralPath $logPath -Encoding UTF8 -Value "Codex update run: $( Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz' )"

function Write-Log
{
    param([string]$Message)
    $line = "[$( Get-Date -Format 'HH:mm:ss' )] $Message"
    Add-Content -LiteralPath $logPath -Encoding UTF8 -Value $line
    Write-Host $line
}

function Get-CodexPackage
{
    Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
}

function Get-CodexVersion
{
    $pkg = Get-CodexPackage
    if ($null -eq $pkg)
    {
        return $null
    }

    return [version]$pkg.Version
}

function Invoke-LoggedCommand
{
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    Write-Log "Running: $FilePath $( $Arguments -join ' ' )"
    if ($WorkingDirectory)
    {
        Write-Log "Working directory: $WorkingDirectory"
    }

    $oldLocation = Get-Location
    try
    {
        if ($WorkingDirectory)
        {
            Set-Location -LiteralPath $WorkingDirectory
        }

        $lastLoggedProgress = -10
        $progressActive = $false

        & $FilePath @Arguments 2>&1 | ForEach-Object {
            $line = $_
            $text = ($line | Out-String).TrimEnd()

            $progressMatch = [regex]::Match($text, '([0-9]{1,3})%')
            if ($progressMatch.Success -and $text -match '[\u2588\u2592]|(?:KB|MB|GB)')
            {
                $percent = [int]$progressMatch.Groups[1].Value
                Write-Host "`r$text" -NoNewline
                $progressActive = $true

                if ($percent -ge ($lastLoggedProgress + 10) -or $percent -eq 100)
                {
                    Add-Content -LiteralPath $logPath -Encoding UTF8 -Value $text
                    $lastLoggedProgress = $percent
                }
                return
            }

            if ($text.Trim() -match '^[-\\|/]$')
            {
                Write-Host "`r$text" -NoNewline
                $progressActive = $true
                return
            }

            if ($progressActive)
            {
                Write-Host
                $progressActive = $false
            }

            if ($text.Length -gt 0)
            {
                Add-Content -LiteralPath $logPath -Encoding UTF8 -Value $text
                Write-Host $text
            }
        }

        if ($progressActive)
        {
            Write-Host
        }

        if ($LASTEXITCODE -ne 0)
        {
            throw "$FilePath failed with exit code $LASTEXITCODE"
        }
    }
    finally
    {
        Set-Location -LiteralPath $oldLocation.Path
    }
}

function Invoke-CommandCapture
{
    param([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory, [switch]$NoLogCommand)
    if ([IO.Path]::GetFileNameWithoutExtension($FilePath) -ne 'winget')
    {
        return Invoke-CommandCaptureCore @PSBoundParameters
    }
    # Vendor processes share winget's source database and initialization state.
    $mutex = [Threading.Mutex]::new($false, 'Local\AIUpdater.Winget')
    $acquired = $false
    try
    {
        try
        {
            $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(2))
        }
        catch [Threading.AbandonedMutexException]
        {
            $acquired = $true
        }
        if (-not $acquired)
        {
            throw 'Timed out waiting for another winget query'
        }
        $result = Invoke-CommandCaptureCore @PSBoundParameters
        if ($result.ExitCode -eq -1978335231)
        {
            Write-Log 'winget initialization failed; retrying once'
            Start-Sleep -Milliseconds 500
            $result = Invoke-CommandCaptureCore @PSBoundParameters
        }
        return $result
    }
    finally
    {
        if ($acquired)
        {
            $mutex.ReleaseMutex()
        }
        $mutex.Dispose()
    }
}

function Invoke-CommandCaptureCore
{
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [switch]$NoLogCommand
    )

    if (-not $NoLogCommand)
    {
        Write-Log "Running: $FilePath $( $Arguments -join ' ' )"
        if ($WorkingDirectory)
        {
            Write-Log "Working directory: $WorkingDirectory"
        }
    }

    $oldLocation = Get-Location
    try
    {
        if ($WorkingDirectory)
        {
            Set-Location -LiteralPath $WorkingDirectory
        }

        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        $text = ($output | Out-String).Trim()

        if (-not $NoLogCommand -and $text.Length -gt 0)
        {
            foreach ($line in ($text -split "`r?`n"))
            {
                if ($line.Trim().Length -gt 0)
                {
                    Add-Content -LiteralPath $logPath -Encoding UTF8 -Value $line
                    Write-Host $line
                }
            }
        }

        return [pscustomobject]@{
            ExitCode = $exitCode
            Text = $text
        }
    }
    finally
    {
        Set-Location -LiteralPath $oldLocation.Path
    }
}

function Restart-UpdateServices
{
    Write-Log 'Restarting Store and Windows Update services'
    $serviceNames = @('BITS', 'wuauserv', 'InstallService', 'ClipSVC', 'AppXSvc')

    foreach ($serviceName in $serviceNames)
    {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($null -eq $service)
        {
            Write-Log "Service not found: $serviceName"
            continue
        }

        try
        {
            if ($service.Status -eq 'Running' -and $service.CanStop)
            {
                Write-Log "Stopping service: $serviceName"
                Stop-Service -Name $serviceName -Force -ErrorAction Stop
                Start-Sleep -Seconds 2
            }

            Write-Log "Starting service: $serviceName"
            Start-Service -Name $serviceName -ErrorAction Stop
        }
        catch
        {
            Write-Log "Service action failed for ${serviceName}: $( $_.Exception.Message )"
        }
    }
}

function Ensure-UpdateServicesRunning
{
    Write-Log 'Checking Store and Windows Update services'
    $serviceNames = @('BITS', 'wuauserv', 'InstallService', 'ClipSVC', 'AppXSvc')

    foreach ($serviceName in $serviceNames)
    {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($null -eq $service)
        {
            Write-Log "Service not found: $serviceName"
            continue
        }

        if ($service.Status -ne 'Running')
        {
            try
            {
                Write-Log "Starting stopped update service: $serviceName"
                Start-Service -Name $serviceName -ErrorAction Stop
                $service = Get-Service -Name $serviceName -ErrorAction Stop
            }
            catch
            {
                Write-Log "Could not start update service ${serviceName}: $( $_.Exception.Message )"
                continue
            }
        }

        Write-Log "Update service state: $serviceName=$( $service.Status ), start type=$( $service.StartType )"
    }
}

function Request-StoreUpdateRefresh
{
    $usoClient = Join-Path $env:WINDIR 'System32\UsoClient.exe'
    if (-not (Test-Path -LiteralPath $usoClient))
    {
        Write-Log 'UsoClient.exe not found; Windows Update scan was not requested'
        return
    }

    try
    {
        Write-Log 'Requesting a fresh Windows Update scan before the Microsoft Store retry'
        $scanProcess = Start-Process -FilePath $usoClient -ArgumentList 'StartScan' -WindowStyle Hidden -PassThru
        $scanProcess.WaitForExit(15000) | Out-Null
        Write-Log "Windows Update scan request exited: $( $scanProcess.HasExited )"
    }
    catch
    {
        Write-Log "Windows Update scan request failed: $( $_.Exception.Message )"
    }
}

function Write-AppxEvents
{
    Write-Log 'Recent AppX events for Codex'
    $events = Get-WinEvent -LogName 'Microsoft-Windows-AppXDeploymentServer/Operational' -MaxEvents 80 -ErrorAction SilentlyContinue |
        Where-Object {
            $_.TimeCreated -gt (Get-Date).AddMinutes(-20) -and
                ($_.Message -match 'OpenAI\.Codex|9PLM9XGG6VKS|0x[0-9a-fA-F]+|Codex')
        } |
        Select-Object -First 20

    if ($null -eq $events)
    {
        Write-Log 'No recent AppX events found'
    }
    else
    {
        foreach ($event in $events)
        {
            $message = ($event.Message -replace '\s+', ' ').Trim()
            Write-Log "AppX event $( $event.Id ) $( $event.LevelDisplayName ) $( $event.TimeCreated ): $message"
        }
    }

    Write-StoreEvents
}

function Write-StoreEvents
{
    Write-Log 'Recent Microsoft Store events for Codex'
    $events = Get-WinEvent -LogName 'Microsoft-Windows-Store/Operational' -MaxEvents 300 -ErrorAction SilentlyContinue |
        Where-Object {
            $_.TimeCreated -gt (Get-Date).AddMinutes(-20) -and
                $_.Message -match 'OpenAI\.Codex|9PLM9XGG6VKS'
        } |
        Select-Object -First 40

    if ($null -eq $events)
    {
        Write-Log 'No recent Microsoft Store events found'
        return
    }

    foreach ($event in $events)
    {
        $message = ($event.Message -replace '\s+', ' ').Trim()
        Write-Log "Store event $( $event.Id ) $( $event.LevelDisplayName ) $( $event.TimeCreated ): $message"
    }
}

function Invoke-CodexStoreAction
{
    param(
        [ValidateSet('install', 'upgrade')]
        [string]$Action,
        [int]$Attempt,
        [switch]$Force
    )

    $arguments = @(
        $Action,
        '--id', $productId,
        '--exact',
        '--source', 'msstore',
        '--accept-source-agreements',
        '--accept-package-agreements',
        '--disable-interactivity'
    )
    if ($Force)
    {
        $arguments += '--force'
    }

    Invoke-LoggedCommand -FilePath 'winget' -Arguments $arguments

    Write-Log "winget $Action attempt $Attempt completed"
}

function Test-Tool
{
    param([string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function ConvertTo-VersionOrNull
{
    param([string]$Text)

    if ( [string]::IsNullOrWhiteSpace($Text))
    {
        return $null
    }

    if ($Text -match '(?<![0-9.])([0-9]+(?:\.[0-9]+){1,3}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)(?![0-9.])')
    {
        return $Matches[1]
    }

    return $null
}

function Get-NpmReleaseChannel
{
    param([string]$Version)
    if ($Version -match '^\d+(?:\.\d+){1,3}-([A-Za-z][A-Za-z0-9-]*)(?:\.|\+|$)')
    {
        return $Matches[1]
    }
    if ($Version -match '^\d+(?:\.\d+){1,3}-')
    {
        throw "Cannot infer npm release channel from $Version"
    }
    return 'latest'
}

function Compare-PackageVersions
{
    param([string]$Installed, [string]$Available)
    $pattern = '^(?<core>\d+(?:\.\d+){1,3})(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
    $left = [regex]::Match($Installed, $pattern)
    $right = [regex]::Match($Available, $pattern)
    if (-not $left.Success -or -not $right.Success)
    {
        throw 'Invalid package version'
    }
    $leftCore = @($left.Groups['core'].Value.Split('.'))
    $rightCore = @($right.Groups['core'].Value.Split('.'))
    for ($i = 0; $i -lt 4; $i++) {
        $a = if ($i -lt $leftCore.Count)
        {
            [decimal]$leftCore[$i]
        }
        else
        {
            0
        }
        $b = if ($i -lt $rightCore.Count)
        {
            [decimal]$rightCore[$i]
        }
        else
        {
            0
        }
        if ($a -ne $b)
        {
            return $a.CompareTo($b)
        }
    }
    $leftPre = $left.Groups['pre'].Value
    $rightPre = $right.Groups['pre'].Value
    if (-not $leftPre -and -not $rightPre)
    {
        return 0
    }
    if (-not $leftPre)
    {
        return 1
    }
    if (-not $rightPre)
    {
        return -1
    }
    $aParts = $leftPre.Split('.')
    $bParts = $rightPre.Split('.')
    for ($i = 0; $i -lt [Math]::Min($aParts.Count, $bParts.Count); $i++) {
        $a = $aParts[$i]
        $b = $bParts[$i]
        $aNumeric = $a -match '^\d+$'
        $bNumeric = $b -match '^\d+$'
        if ($aNumeric -and $bNumeric)
        {
            $a = $a.TrimStart('0'); $b = $b.TrimStart('0')
            $comparison = $a.Length.CompareTo($b.Length)
            if ($comparison -eq 0)
            {
                $comparison = [string]::CompareOrdinal($a, $b)
            }
        }
        elseif ($aNumeric -ne $bNumeric)
        {
            $comparison = if ($aNumeric)
            {
                -1
            }
            else
            {
                1
            }
        }
        else
        {
            $comparison = [string]::CompareOrdinal($a, $b)
        }
        if ($comparison -ne 0)
        {
            return [Math]::Sign($comparison)
        }
    }
    return $aParts.Count.CompareTo($bParts.Count)
}

function Get-ToolVersion
{
    param([string]$ToolName)

    if (-not (Test-Tool $ToolName))
    {
        Write-Log "$ToolName not found"
        return $null
    }

    $result = Invoke-CommandCapture -FilePath $ToolName -Arguments @('--version') -NoLogCommand
    if ($result.ExitCode -ne 0)
    {
        Write-Log "$ToolName --version failed with exit code $( $result.ExitCode )"
        return $null
    }

    Write-Log "$ToolName version output: $( $result.Text )"
    return ConvertTo-VersionOrNull $result.Text
}

function Get-NpmLatestVersion
{
    param([string]$PackageName, [string]$InstalledVersion)

    if (-not (Test-Tool 'npm'))
    {
        Write-Log 'npm not found'
        return $null
    }

    $channel = Get-NpmReleaseChannel $InstalledVersion
    Write-Log "Checking npm release channel for ${PackageName}: $channel"
    $field = if ($channel -eq 'latest')
    {
        'version'
    }
    else
    {
        "dist-tags.$channel"
    }
    $result = Invoke-CommandCapture -FilePath 'npm' -Arguments @('view', $PackageName, $field, '--silent') -NoLogCommand
    if ($result.ExitCode -ne 0)
    {
        Write-Log "npm latest lookup failed for ${PackageName}: exit code $( $result.ExitCode )"
        return $null
    }

    $latest = ConvertTo-VersionOrNull $result.Text
    if ($null -eq $latest)
    {
        Write-Log "npm latest lookup returned an unparsable version for ${PackageName}: $( $result.Text )"
    }
    elseif ((Get-NpmReleaseChannel $latest) -ne $channel)
    {
        Write-Log "npm channel $channel returned a different release channel; comparison unavailable"
        return $null
    }
    else
    {
        Write-Log "Latest npm version for ${PackageName} (${channel}): $latest"
    }

    return $latest
}

function Get-NpmGlobalPackageState
{
    param([string]$PackageName)

    $state = [pscustomobject]@{ Status = 'ERROR'; Version = $null; Detail = '' }
    if (-not (Test-Tool 'npm'))
    {
        $state.Detail = 'Для проверки глобального пакета требуется npm'
        return $state
    }

    try
    {
        $result = Invoke-CommandCapture -FilePath 'npm' -Arguments @('list', '-g', '--depth=0', '--json', '--silent') -NoLogCommand
        if ($result.ExitCode -ne 0)
        {
            Write-Log "npm global list failed: $( $result.ExitCode ): $( $result.Text )"
            $state.Detail = "Не удалось прочитать глобальные пакеты npm (код $( $result.ExitCode ))"
            return $state
        }
        $data = $result.Text | ConvertFrom-Json
        if ($data -isnot [pscustomobject] -or $data.PSObject.Properties['error'] -or
            -not ($data.PSObject.Properties['dependencies'] -or $data.PSObject.Properties['name'] -or $data.PSObject.Properties['path']))
        {
            throw 'Unexpected npm list response'
        }
        $dependencies = $data.PSObject.Properties['dependencies']
        if ($null -ne $dependencies -and $dependencies.Value -isnot [pscustomobject])
        {
            throw 'Expected a dependencies object'
        }
        $dependency = if ($null -ne $dependencies)
        {
            $dependencies.Value.PSObject.Properties[$PackageName]
        }
        else
        {
            $null
        }
        if ($null -eq $dependency)
        {
            $state.Status = 'NOT_INSTALLED'
            $state.Detail = 'Глобальный npm-пакет не установлен в активном npm-каталоге'
            return $state
        }
        $version = ConvertTo-VersionOrNull $dependency.Value.version
        if ($null -eq $version)
        {
            $state.Detail = 'Пакет найден, но его установленную версию не удалось прочитать'
            return $state
        }

        $state.Status = 'INSTALLED'
        $state.Version = $version
    }
    catch
    {
        Write-Log "Failed to parse npm list output for ${PackageName}: $( $_.Exception.Message )"
        $state.Detail = 'Не удалось выполнить проверку или разобрать список глобальных пакетов npm'
    }
    return $state
}

function Get-NpmGlobalPackageVersion
{
    param([string]$PackageName)

    $state = Get-NpmGlobalPackageState $PackageName
    if ($state.Status -eq 'INSTALLED')
    {
        return $state.Version
    }
    Write-Log "${PackageName}: $( $state.Detail )"
    return $null
}

function Get-CodexDesktopProcesses
{
    $pkg = Get-CodexPackage
    if ($null -eq $pkg)
    {
        return @()
    }

    $installRoot = [string]$pkg.InstallLocation
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try
        {
            $_.Path -and $_.Path.StartsWith($installRoot, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch
        {
            $false
        }
    })
}

function Stop-CodexProcess
{
    if ($script:codexWasStopped)
    {
        return
    }

    $processes = @(Get-CodexDesktopProcesses)
    if ($processes.Count -eq 0)
    {
        Write-Log 'ChatGPT/Codex desktop is not running'
        return
    }

    Write-Log "Closing ChatGPT/Codex desktop ($( $processes.Count ) package processes)"
    $mainProcess = $processes |
        Where-Object { $_.ProcessName -eq 'ChatGPT' -and $_.MainWindowHandle -ne 0 } |
        Select-Object -First 1

    if ($null -eq $mainProcess)
    {
        Write-Log 'ChatGPT window is not visible; opening it to request a normal window close'
        Start-Process "shell:AppsFolder\$appId"
        Start-Sleep -Seconds 3
        $mainProcess = @(Get-CodexDesktopProcesses) |
            Where-Object { $_.ProcessName -eq 'ChatGPT' -and $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
    }

    if ($null -eq $mainProcess)
    {
        Write-Log 'ChatGPT window could not be opened; background package processes will be stopped automatically'
    }
    else
    {
        $closeRequested = $mainProcess.CloseMainWindow()
        if ($closeRequested)
        {
            Write-Log 'Sent the ChatGPT/Codex normal window close command (WM_CLOSE)'
        }
        else
        {
            Write-Log 'ChatGPT/Codex rejected the normal window close command'
        }
    }

    $deadline = (Get-Date).AddSeconds(8)
    do
    {
        Start-Sleep -Milliseconds 500
        $remaining = @(Get-CodexDesktopProcesses)
    } while ($remaining.Count -gt 0 -and (Get-Date) -lt $deadline)

    if ($remaining.Count -gt 0)
    {
        Write-Log "Normal close left $( $remaining.Count ) package processes; stopping them automatically"
        $remaining | Sort-Object Id -Descending | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 2
        $remaining = @(Get-CodexDesktopProcesses)
        if ($remaining.Count -gt 0)
        {
            throw "Could not stop $( $remaining.Count ) ChatGPT/Codex package processes"
        }
        Write-Log 'Remaining ChatGPT/Codex package processes were stopped automatically'
    }
    else
    {
        Write-Log 'ChatGPT/Codex closed normally after the window close command'
    }

    $script:codexWasStopped = $true
    Write-Log 'ChatGPT/Codex desktop and its background processes are fully stopped'
}

function Stop-ClaudeDesktopProcess
{
    if ($script:claudeDesktopWasStopped)
    {
        return
    }

    Write-Log 'Stopping Claude Desktop'
    $processes = Get-Process -Name Claude -ErrorAction SilentlyContinue
    if ($processes)
    {
        $processes | Stop-Process -Force
        $script:claudeDesktopWasStopped = $true
        Start-Sleep -Seconds 2
    }
}

function Start-ClaudeDesktopProcess
{
    if (-not $script:claudeDesktopWasStopped)
    {
        return
    }

    try
    {
        Write-Log 'Starting Claude Desktop'
        $claudeApp = Get-StartApps | Where-Object { $_.Name -eq 'Claude' } | Select-Object -First 1
        $appToStart = if ($claudeApp)
        {
            $claudeApp.AppID
        }
        else
        {
            $claudeDesktopAppId
        }
        Start-Process "shell:AppsFolder\$appToStart"
    }
    catch
    {
        Write-Log "Failed to start Claude Desktop: $( $_.Exception.Message )"
    }
}

function Start-CodexProcess
{
    if (-not $script:codexWasStopped)
    {
        return
    }

    try
    {
        Write-Log 'Starting Codex'
        Start-Process "shell:AppsFolder\$appId"
    }
    catch
    {
        Write-Log "Failed to start Codex: $( $_.Exception.Message )"
    }
}

function Get-CodexDesktopWingetState
{
    if (-not (Test-Tool 'winget'))
    {
        Write-Log 'winget not found; ChatGPT/Codex desktop update skipped'
        return $null
    }

    $result = Invoke-CommandCapture -FilePath 'winget' -Arguments @(
        'list',
        '--id', $productId,
        '--exact',
        '--source', 'msstore',
        '--accept-source-agreements'
    ) -NoLogCommand

    if ($result.ExitCode -ne 0)
    {
        if ($null -eq (Get-CodexPackage))
        {
            return [pscustomobject]@{
                IsInstalled = $false
                Installed = $null
                Available = $null
            }
        }

        Write-Log "Could not read ChatGPT/Codex desktop version from Microsoft Store: exit code $( $result.ExitCode )"
        return $null
    }

    foreach ($line in ($result.Text -split "`r?`n"))
    {
        if ($line -match "\b$([regex]::Escape($productId) )\s+([0-9]+(?:\.[0-9]+){1,3})(?:\s+([0-9]+(?:\.[0-9]+){1,3}))?(?:\s+msstore)?\s*$")
        {
            return [pscustomobject]@{
                IsInstalled = $true
                Installed = [version]$Matches[1]
                Available = if ($Matches[2])
                {
                    [version]$Matches[2]
                }
                else
                {
                    $null
                }
            }
        }
    }

    Write-Log 'Could not parse ChatGPT/Codex desktop version from winget output'
    return $null
}

function Get-CodexPackagedStoreUpdateState
{
    $resultPath = Join-Path $env:TEMP "codex-store-query-$([guid]::NewGuid().ToString('N') ).json"
    $escapedResultPath = $resultPath.Replace("'", "''")
    $innerScript = @"
Add-Type -AssemblyName System.Runtime.WindowsRuntime
`$resultPath = '$escapedResultPath'
try {
    `$context = [Windows.Services.Store.StoreContext,Windows.Services.Store,ContentType=WindowsRuntime]::GetDefault()
    `$operation = `$context.GetAppAndOptionalStorePackageUpdatesAsync()
    `$updateType = [Windows.Services.Store.StorePackageUpdate,Windows.Services.Store,ContentType=WindowsRuntime]
    `$resultType = [System.Collections.Generic.IReadOnlyList``1].MakeGenericType(`$updateType)
    `$method = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object { `$_.Name -eq 'AsTask' -and `$_.IsGenericMethod -and `$_.GetGenericArguments().Count -eq 1 -and `$_.GetParameters().Count -eq 1 } |
        Select-Object -First 1
    `$task = `$method.MakeGenericMethod(`$resultType).Invoke(`$null, @(`$operation))
    `$task.Wait()
    `$updates = @(foreach (`$update in `$task.Result) {
        `$version = `$update.Package.Id.Version
        [pscustomobject]@{
            Name = `$update.Package.Id.Name
            Version = (`$version.Major, `$version.Minor, `$version.Build, `$version.Revision -join '.')
            Mandatory = `$update.Mandatory
        }
    })
    @{ Success = `$true; Updates = `$updates } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath `$resultPath -Encoding UTF8
} catch {
    @{ Success = `$false; Error = `$_.Exception.ToString() } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath `$resultPath -Encoding UTF8
}
"@

    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($innerScript))
    $invokeArguments = @{
        PackageFamilyName = 'OpenAI.Codex_2p2nqsd0c76g0'
        AppId = 'App'
        Command = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
        Args = "-NoProfile -EncodedCommand $encodedCommand"
        PreventBreakaway = $true
    }
    Invoke-CommandInDesktopPackage @invokeArguments | Out-Null

    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path -LiteralPath $resultPath) -and (Get-Date) -lt $deadline)
    {
        Start-Sleep -Milliseconds 250
    }

    if (-not (Test-Path -LiteralPath $resultPath))
    {
        throw 'Packaged Microsoft Store update query timed out'
    }

    try
    {
        return Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    }
    finally
    {
        Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    }
}

function Start-CodexPackagedStoreInstall
{
    $statusPath = Join-Path $env:TEMP "codex-store-install-$([guid]::NewGuid().ToString('N') ).json"
    $escapedStatusPath = $statusPath.Replace("'", "''")
    $innerScript = @"
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithWindow
{
    void Initialize(IntPtr hwnd);
}

public static class StoreWindowBridge
{
    public static void Initialize(object target, IntPtr hwnd)
    {
        ((IInitializeWithWindow)target).Initialize(hwnd);
    }
}
'@
`$statusPath = '$escapedStatusPath'
`$form = `$null
try {
    `$form = New-Object System.Windows.Forms.Form
    `$form.ShowInTaskbar = `$false
    `$form.WindowState = 'Minimized'
    `$form.Opacity = 0
    `$form.Show()

    `$context = [Windows.Services.Store.StoreContext,Windows.Services.Store,ContentType=WindowsRuntime]::GetDefault()
    [StoreWindowBridge]::Initialize(`$context, `$form.Handle)
    `$queryOperation = `$context.GetAppAndOptionalStorePackageUpdatesAsync()
    `$updateType = [Windows.Services.Store.StorePackageUpdate,Windows.Services.Store,ContentType=WindowsRuntime]
    `$updatesType = [System.Collections.Generic.IReadOnlyList``1].MakeGenericType(`$updateType)
    `$queryMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object { `$_.Name -eq 'AsTask' -and `$_.IsGenericMethod -and `$_.GetGenericArguments().Count -eq 1 -and `$_.GetParameters().Count -eq 1 } |
        Select-Object -First 1
    `$queryTask = `$queryMethod.MakeGenericMethod(`$updatesType).Invoke(`$null, @(`$queryOperation))
    `$queryTask.Wait()
    `$updates = `$queryTask.Result
    if (`$updates.Count -eq 0) {
        @{ State = 'NoUpdate' } | ConvertTo-Json | Set-Content -LiteralPath `$statusPath -Encoding UTF8
        exit
    }

    # PowerShell exposes the WinRT result as a COM collection. Rebuild it as the
    # strongly typed IEnumerable required by the Store installation method.
    `$typedListType = [System.Collections.Generic.List``1].MakeGenericType(`$updateType)
    `$typedUpdates = [Activator]::CreateInstance(`$typedListType)
    foreach (`$update in `$updates) {
        `$typedUpdates.Add(`$update)
    }

    `$updateDetails = @(`$updates | ForEach-Object {
        `$version = `$_.Package.Id.Version
        [pscustomobject]@{
            Name = `$_.Package.Id.Name
            PackageFamilyName = `$_.Package.Id.FamilyName
            Version = (`$version.Major, `$version.Minor, `$version.Build, `$version.Revision -join '.')
            Mandatory = `$_.Mandatory
        }
    })
    `$canSilentlyDownload = `$context.CanSilentlyDownloadStorePackageUpdates
    @{
        State = 'Installing'
        ProcessId = `$PID
        UpdateCount = `$typedUpdates.Count
        Updates = `$updateDetails
        CanSilentlyDownload = `$canSilentlyDownload
        StartedAt = (Get-Date).ToString('o')
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath `$statusPath -Encoding UTF8
    if (-not `$canSilentlyDownload) {
        @{
            State = 'SilentDownloadNotAllowed'
            ProcessId = `$PID
            UpdateCount = `$typedUpdates.Count
            Updates = `$updateDetails
            CanSilentlyDownload = `$false
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath `$statusPath -Encoding UTF8
        exit
    }

    `$installOperation = `$context.TrySilentDownloadAndInstallStorePackageUpdatesAsync(`$typedUpdates)
    `$resultType = [Windows.Services.Store.StorePackageUpdateResult,Windows.Services.Store,ContentType=WindowsRuntime]
    `$progressType = [Windows.Services.Store.StorePackageUpdateStatus,Windows.Services.Store,ContentType=WindowsRuntime]
    `$installMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object { `$_.Name -eq 'AsTask' -and `$_.IsGenericMethod -and `$_.GetGenericArguments().Count -eq 2 -and `$_.GetParameters().Count -eq 1 } |
        Select-Object -First 1
    `$installTask = `$installMethod.MakeGenericMethod(`$resultType, `$progressType).Invoke(`$null, @(`$installOperation))
    `$installTask.Wait()
    `$result = `$installTask.Result
    `$packageStatuses = @(`$result.StorePackageUpdateStatuses | ForEach-Object {
        [pscustomobject]@{
            PackageFamilyName = `$_.PackageFamilyName
            PackageUpdateState = [string]`$_.PackageUpdateState
            ErrorCode = if (`$null -ne `$_.ErrorCode) { ('0x{0:X8}' -f (`$_.ErrorCode.HResult -band 0xffffffffL)) } else { `$null }
        }
    })
    @{
        State = 'Completed'
        ProcessId = `$PID
        OverallState = [string]`$result.OverallState
        PackageStatuses = `$packageStatuses
        Updates = `$updateDetails
        CompletedAt = (Get-Date).ToString('o')
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath `$statusPath -Encoding UTF8
} catch {
    @{
        State = 'Failed'
        ProcessId = `$PID
        Error = `$_.Exception.ToString()
        ErrorHResult = if (`$null -ne `$_.Exception) { ('0x{0:X8}' -f (`$_.Exception.HResult -band 0xffffffffL)) } else { `$null }
        FailedAt = (Get-Date).ToString('o')
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath `$statusPath -Encoding UTF8
} finally {
    if (`$null -ne `$form) {
        `$form.Close()
        `$form.Dispose()
    }
}
"@

    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($innerScript))
    $invokeArguments = @{
        PackageFamilyName = 'OpenAI.Codex_2p2nqsd0c76g0'
        AppId = 'App'
        Command = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
        Args = "-NoProfile -STA -EncodedCommand $encodedCommand"
        PreventBreakaway = $true
    }
    Invoke-CommandInDesktopPackage @invokeArguments | Out-Null

    return $statusPath
}

function Wait-CodexPackagedStoreInstall
{
    param(
        [string]$StatusPath,
        [version]$PreviousVersion
    )

    Write-Log 'Microsoft Store update installation started through the ChatGPT/Codex package context'
    $deadline = (Get-Date).AddMinutes(5)
    $lastState = $null
    $helperProcessId = $null
    $loggedFinalStatus = $false
    $completedDeadline = $null

    Write-Log "Packaged Microsoft Store status file: $StatusPath"

    try
    {
        while ((Get-Date) -lt $deadline)
        {
            Start-Sleep -Seconds 1
            $currentVersion = Get-CodexVersion
            if ($null -ne $currentVersion -and $currentVersion -gt $PreviousVersion)
            {
                Write-Log "Microsoft Store installed ChatGPT/Codex desktop version: $currentVersion"
                return $currentVersion
            }

            if (Test-Path -LiteralPath $StatusPath)
            {
                try
                {
                    $status = Get-Content -Raw -LiteralPath $StatusPath | ConvertFrom-Json
                }
                catch
                {
                    # The helper can replace the status file while it is being read.
                    continue
                }

                if ($status.ProcessId)
                {
                    $helperProcessId = [int]$status.ProcessId
                }

                if ($status.State -and $status.State -ne $lastState)
                {
                    Write-Log "Packaged Microsoft Store installer state: $( $status.State )"
                    if ($status.UpdateCount)
                    {
                        Write-Log "Packaged Microsoft Store update count: $( $status.UpdateCount ); silent download allowed: $( $status.CanSilentlyDownload )"
                    }
                    foreach ($update in @($status.Updates))
                    {
                        Write-Log "Packaged Microsoft Store update: $( $update.PackageFamilyName ), version $( $update.Version ), mandatory $( $update.Mandatory )"
                    }
                    $lastState = $status.State
                }

                if ($status.State -eq 'Failed')
                {
                    throw "Packaged Microsoft Store installer failed: $( $status.Error )"
                }

                if ($status.State -eq 'NoUpdate')
                {
                    throw 'Microsoft Store no longer reports an installable ChatGPT/Codex update'
                }

                if ($status.State -eq 'SilentDownloadNotAllowed')
                {
                    throw 'Microsoft Store settings do not allow silent ChatGPT/Codex updates'
                }

                if ($status.State -eq 'Completed' -and -not $loggedFinalStatus)
                {
                    $packageStatusList = @($status.PackageStatuses)
                    $details = @($packageStatusList | ForEach-Object {
                        "$( $_.PackageFamilyName ): $( $_.PackageUpdateState ), $( $_.ErrorCode )"
                    }) -join '; '

                    Write-Log "Packaged Microsoft Store result: OverallState=$( $status.OverallState ); PackageStatuses=$details"
                    Write-Log "Packaged Microsoft Store raw status: $( $status | ConvertTo-Json -Compress -Depth 8 )"
                    $loggedFinalStatus = $true

                    if ($packageStatusList.Count -eq 0)
                    {
                        throw 'Microsoft Store returned Completed without package status details'
                    }

                    $failedPackages = @($packageStatusList | Where-Object {
                        ([string]$_.PackageUpdateState -match 'Error|Failed|Canceled') -or
                            ($_.ErrorCode -and $_.ErrorCode -notmatch '^0x0+$')
                    })

                    if ($failedPackages.Count -gt 0)
                    {
                        throw "Microsoft Store reported package failure despite Completed state. $details"
                    }

                    if ($status.OverallState -notmatch 'Completed')
                    {
                        throw "Microsoft Store installation completed with state: $( $status.OverallState ). $details"
                    }

                    $completedDeadline = (Get-Date).AddSeconds(30)
                    Write-Log 'Microsoft Store reported success; waiting up to 30 seconds for the installed package version to change'
                }

                if ($null -ne $completedDeadline -and (Get-Date) -ge $completedDeadline)
                {
                    throw "Microsoft Store reported Completed, but the installed version remained $PreviousVersion after 30 seconds"
                }

            }
        }

        throw 'Timed out waiting for the Microsoft Store to install the ChatGPT/Codex update'
    }
    finally
    {
        if ($helperProcessId)
        {
            Get-Process -Id $helperProcessId -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        }
        Remove-Item -LiteralPath $StatusPath -Force -ErrorAction SilentlyContinue
    }
}

function Update-CodexDesktop
{
    Ensure-UpdateServicesRunning

    $state = Get-CodexDesktopWingetState
    if ($null -eq $state)
    {
        return
    }

    if (-not $state.IsInstalled)
    {
        Write-Log 'ChatGPT/Codex desktop is not installed; use the separate Install action'
        return
    }

    Write-Log "Current ChatGPT/Codex desktop version: $( $state.Installed )"
    $packagedStoreState = Get-CodexPackagedStoreUpdateState
    if (-not $packagedStoreState.Success)
    {
        Write-Log "Packaged Microsoft Store query failed: $( $packagedStoreState.Error )"
    }

    $packagedUpdateCount = if ($packagedStoreState.Success)
    {
        @($packagedStoreState.Updates).Count
    }
    else
    {
        0
    }
    if ($packagedUpdateCount -gt 0)
    {
        Write-Log "ChatGPT/Codex reports $packagedUpdateCount installable Microsoft Store update(s)"
        $previousVersion = $state.Installed
        Stop-CodexProcess
        $statusPath = Start-CodexPackagedStoreInstall
        try
        {
            Wait-CodexPackagedStoreInstall -StatusPath $statusPath -PreviousVersion $previousVersion | Out-Null
            return
        }
        catch
        {
            $packagedError = $_.Exception.Message
            Write-Log "Packaged Microsoft Store installation did not complete: $packagedError"
            Write-StoreEvents
            Ensure-UpdateServicesRunning
            Request-StoreUpdateRefresh
            Start-Sleep -Seconds 5
            Write-Log 'Trying a forced Microsoft Store installation through winget'
            try
            {
                Invoke-CodexStoreAction -Action 'install' -Attempt 2 -Force
            }
            catch
            {
                Write-Log "Forced Microsoft Store installation failed: $( $_.Exception.Message )"
                Restart-UpdateServices
                Request-StoreUpdateRefresh
                Start-Sleep -Seconds 5
                Write-Log 'Retrying the forced Microsoft Store installation after service recovery'
                Invoke-CodexStoreAction -Action 'install' -Attempt 3 -Force
            }

            $installedVersion = Get-CodexVersion
            if ($null -ne $installedVersion -and $installedVersion -gt $previousVersion)
            {
                Write-Log "winget installed ChatGPT/Codex desktop version: $installedVersion"
                return
            }

            throw "The forced winget installation finished, but the ChatGPT/Codex version did not change from $previousVersion. Packaged Store error: $packagedError"
        }
    }

    if ($null -eq $state.Available -or $state.Installed -ge $state.Available)
    {
        Write-Log 'Neither the ChatGPT/Codex updater nor winget reports a newer desktop version; update skipped'
        return
    }

    Write-Log "winget reports a ChatGPT/Codex desktop update: $( $state.Installed ) -> $( $state.Available )"
    Stop-CodexProcess

    try
    {
        Invoke-CodexStoreAction -Action 'upgrade' -Attempt 1
    }
    catch
    {
        Write-Log "First ChatGPT/Codex desktop upgrade attempt failed: $( $_.Exception.Message )"
        Restart-UpdateServices
        Start-Sleep -Seconds 5
        Invoke-CodexStoreAction -Action 'upgrade' -Attempt 2
    }

    $updatedVersion = Get-CodexVersion
    if ($null -eq $updatedVersion)
    {
        throw 'ChatGPT/Codex desktop package was not found after update'
    }

    Write-Log "Updated ChatGPT/Codex desktop version: $updatedVersion"
}

function Get-ClaudeDesktopWingetState
{
    if (-not (Test-Tool 'winget'))
    {
        Write-Log 'winget not found; Claude Desktop update skipped'
        return $null
    }

    $result = Invoke-CommandCapture -FilePath 'winget' -Arguments @(
        'list',
        '--id', $claudeDesktopPackageId,
        '--exact',
        '--source', 'winget',
        '--accept-source-agreements'
    ) -NoLogCommand

    if ($result.ExitCode -ne 0)
    {
        if ($result.ExitCode -eq -1978335212)
        {
            return [pscustomobject]@{
                IsInstalled = $false
                Installed = $null
                Available = $null
            }
        }

        Write-Log "Could not read Claude Desktop version from winget: exit code $( $result.ExitCode )"
        return $null
    }

    foreach ($line in ($result.Text -split "`r?`n"))
    {
        if ($line -match "\b$([regex]::Escape($claudeDesktopPackageId) )\s+([0-9]+(?:\.[0-9]+){1,3})(?:\s+([0-9]+(?:\.[0-9]+){1,3}))?\s+")
        {
            return [pscustomobject]@{
                IsInstalled = $true
                Installed = [version]$Matches[1]
                Available = if ($Matches[2])
                {
                    [version]$Matches[2]
                }
                else
                {
                    $null
                }
            }
        }
    }

    Write-Log 'Claude Desktop is not installed according to winget'
    return $null
}

function Update-ClaudeDesktop
{
    $state = Get-ClaudeDesktopWingetState
    if ($null -eq $state)
    {
        return
    }

    if (-not $state.IsInstalled)
    {
        Write-Log 'Claude Desktop is not installed; use the separate Install action'
        return
    }

    Write-Log "Current Claude Desktop version: $( $state.Installed )"
    if ($null -eq $state.Available -or $state.Installed -ge $state.Available)
    {
        Write-Log 'Claude Desktop is already up to date'
        return
    }

    Write-Log "Claude Desktop update needed: $( $state.Installed ) -> $( $state.Available )"
    Stop-ClaudeDesktopProcess
    Invoke-LoggedCommand -FilePath 'winget' -Arguments @(
        'upgrade',
        '--id', $claudeDesktopPackageId,
        '--exact',
        '--source', 'winget',
        '--accept-source-agreements',
        '--accept-package-agreements',
        '--disable-interactivity'
    )

    $updatedState = Get-ClaudeDesktopWingetState
    if ($null -ne $updatedState)
    {
        Write-Log "Claude Desktop version after update: $( $updatedState.Installed )"
    }
}

function Update-CodexPrerelease
{
    param([string]$Version)
    $command = Get-Command codex -ErrorAction Stop
    if (Test-Tool 'npm')
    {
        $prefix = Invoke-CommandCapture 'npm' @('prefix', '-g') -NoLogCommand
        if ($prefix.ExitCode -eq 0 -and [IO.Path]::GetDirectoryName($command.Source) -eq $prefix.Text.Trim())
        {
            Stop-CodexProcess
            Invoke-LoggedCommand 'npm' @('install', '-g', "@openai/codex@$Version")
            return
        }
    }
    $standalonePath = Join-Path $env:LOCALAPPDATA 'Programs\OpenAI\Codex\bin\codex.exe'
    if ([IO.Path]::GetFullPath($command.Source) -ne [IO.Path]::GetFullPath($standalonePath))
    {
        throw 'Cannot safely update this prerelease CLI installation; use its original installer with the required release version'
    }
    $installer = Join-Path ([IO.Path]::GetTempPath()) ("AIUpdater-codex-{0}.ps1" -f [guid]::NewGuid().ToString('N'))
    $previousNonInteractive = $env:CODEX_NON_INTERACTIVE
    try
    {
        Invoke-WebRequest 'https://chatgpt.com/codex/install.ps1' -OutFile $installer -UseBasicParsing
        $env:CODEX_NON_INTERACTIVE = '1'
        Stop-CodexProcess
        Invoke-LoggedCommand 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer, '-Release', $Version)
    }
    finally
    {
        $env:CODEX_NON_INTERACTIVE = $previousNonInteractive
        if (Test-Path -LiteralPath $installer)
        {
            Remove-Item -LiteralPath $installer -Force
        }
    }
}

function Update-ActiveCodexCli
{
    if (-not (Test-Tool 'codex'))
    {
        Write-Log 'Codex CLI not found'
        return
    }

    $currentVersion = Get-ToolVersion 'codex'
    $latestVersion = Get-NpmLatestVersion '@openai/codex' $currentVersion
    $channel = Get-NpmReleaseChannel $currentVersion
    if ($channel -ne 'latest' -and $null -eq $latestVersion)
    {
        throw "Cannot determine the latest Codex CLI version in channel $channel; update skipped"
    }

    if ($null -ne $currentVersion -and $null -ne $latestVersion -and (Compare-PackageVersions $currentVersion $latestVersion) -ge 0)
    {
        Write-Log "Active Codex CLI is already up to date in channel $channel ($currentVersion)"
        return
    }

    if ($null -ne $currentVersion -and $null -ne $latestVersion)
    {
        Write-Log "Active Codex CLI update needed: $currentVersion -> $latestVersion"
    }
    else
    {
        Write-Log 'Active Codex CLI version comparison was inconclusive; running codex updater'
    }

    if ($channel -ne 'latest')
    {
        Update-CodexPrerelease $latestVersion
    }
    else
    {
        Stop-CodexProcess
        Invoke-LoggedCommand -FilePath 'codex' -Arguments @('update')
    }
    $updatedVersion = Get-ToolVersion 'codex'
    if ($channel -ne 'latest' -and ($null -eq $updatedVersion -or
        (Get-NpmReleaseChannel $updatedVersion) -ne $channel -or
        (Compare-PackageVersions $updatedVersion $latestVersion) -lt 0))
    {
        throw "Codex CLI did not reach the requested $channel version $latestVersion (installed: $updatedVersion)"
    }
    if ($null -ne $updatedVersion)
    {
        Write-Log "Active Codex CLI version after update: $updatedVersion"
    }
}

function Update-NpmGlobalPackage
{
    param(
        [string]$PackageName,
        [string]$DisplayName
    )

    $currentVersion = Get-NpmGlobalPackageVersion $PackageName
    if ($null -eq $currentVersion)
    {
        Write-Log "$DisplayName npm package is not globally installed; npm update skipped"
        return
    }

    Write-Log "$DisplayName global npm version: $currentVersion"
    $latestVersion = Get-NpmLatestVersion $PackageName $currentVersion
    if ($null -eq $latestVersion)
    {
        Write-Log "$DisplayName npm latest version unavailable; npm update skipped"
        return
    }

    if ((Compare-PackageVersions $currentVersion $latestVersion) -ge 0)
    {
        Write-Log "$DisplayName global npm package is already up to date"
        return
    }

    Write-Log "$DisplayName global npm update needed: $currentVersion -> $latestVersion"
    Invoke-LoggedCommand -FilePath 'npm' -Arguments @('install', '-g', "$PackageName@$latestVersion")
    $updatedVersion = Get-NpmGlobalPackageVersion $PackageName
    if ($null -ne $updatedVersion)
    {
        Write-Log "$DisplayName global npm version after update: $updatedVersion"
    }
}

function Update-ClaudeCode
{
    if (-not (Test-Tool 'claude'))
    {
        Write-Log 'Claude Code CLI not found'
        return
    }

    $currentVersion = Get-ToolVersion 'claude'
    $latestVersion = Get-NpmLatestVersion '@anthropic-ai/claude-code' $currentVersion

    if ($null -ne $currentVersion -and $null -ne $latestVersion -and (Compare-PackageVersions $currentVersion $latestVersion) -ge 0)
    {
        Write-Log 'Claude Code is already up to date'
        return
    }

    if ((Get-NpmReleaseChannel $currentVersion) -ne 'latest')
    {
        throw 'Native Claude prerelease updates cannot guarantee the selected channel; update the matching npm component or use its original installer'
    }

    if ($null -ne $currentVersion -and $null -ne $latestVersion)
    {
        Write-Log "Claude Code update needed: $currentVersion -> $latestVersion"
    }
    else
    {
        Write-Log 'Claude Code version comparison was inconclusive; running claude updater'
    }

    Invoke-LoggedCommand -FilePath 'claude' -Arguments @('update')
    $updatedVersion = Get-ToolVersion 'claude'
    if ($null -ne $updatedVersion)
    {
        Write-Log "Claude Code version after update: $updatedVersion"
    }
}

function Update-CodexPlugins
{
    if (-not (Test-Tool 'codex'))
    {
        Write-Log 'Codex CLI not found; Codex plugin update skipped'
        return
    }

    Invoke-LoggedCommand -FilePath 'codex' -Arguments @('plugin', 'marketplace', 'upgrade', '--json')

    $result = Invoke-CommandCapture -FilePath 'codex' -Arguments @('plugin', 'list', '--json') -NoLogCommand
    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Text))
    {
        Write-Log "Could not read Codex plugin list: exit code $( $result.ExitCode )"
        return
    }

    try
    {
        $pluginList = $result.Text | ConvertFrom-Json
        foreach ($plugin in $pluginList.installed)
        {
            $state = if ($plugin.enabled)
            {
                'enabled'
            }
            else
            {
                'disabled'
            }
            Write-Log "Codex plugin installed: $( $plugin.pluginId ), version $( $plugin.version ), $state"
        }
    }
    catch
    {
        Write-Log "Failed to parse Codex plugin list: $( $_.Exception.Message )"
    }

    Write-Log 'Codex plugin marketplaces refreshed. Installed bundled/runtime plugin versions are updated by Codex desktop/CLI updates.'
}

function Update-ClaudePlugins
{
    if (-not (Test-Tool 'claude'))
    {
        Write-Log 'Claude Code CLI not found; Claude plugin update skipped'
        return
    }

    Invoke-LoggedCommand -FilePath 'claude' -Arguments @('plugin', 'marketplace', 'update')

    $result = Invoke-CommandCapture -FilePath 'claude' -Arguments @('plugin', 'list', '--json') -NoLogCommand
    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Text))
    {
        Write-Log "Could not read Claude plugin list: exit code $( $result.ExitCode )"
        return
    }

    $plugins = @()
    try
    {
        $parsedPlugins = $result.Text | ConvertFrom-Json
        foreach ($plugin in $parsedPlugins)
        {
            $plugins += $plugin
        }
    }
    catch
    {
        Write-Log "Failed to parse Claude plugin list: $( $_.Exception.Message )"
        return
    }

    foreach ($plugin in $plugins)
    {
        $scope = [string]$plugin.scope
        $pluginId = [string]$plugin.id
        $workingDirectory = $null

        if ([string]::IsNullOrWhiteSpace($pluginId) -or [string]::IsNullOrWhiteSpace($scope))
        {
            continue
        }

        if ($scope -eq 'managed')
        {
            Write-Log "Claude plugin skipped because it is managed: $pluginId"
            continue
        }

        if (($scope -eq 'project' -or $scope -eq 'local') -and $plugin.projectPath)
        {
            $workingDirectory = [string]$plugin.projectPath
        }

        if (($scope -eq 'project' -or $scope -eq 'local') -and -not $workingDirectory)
        {
            Write-Log "Claude plugin skipped because project path is unknown: $pluginId ($scope)"
            continue
        }

        if ($workingDirectory -and -not (Test-Path -LiteralPath $workingDirectory -PathType Container))
        {
            Write-Log "Claude plugin skipped because project directory no longer exists: $pluginId ($scope), $workingDirectory"
            continue
        }

        try
        {
            Write-Log "Updating Claude plugin: $pluginId ($scope), current version $( $plugin.version )"
            Invoke-LoggedCommand -FilePath 'claude' -Arguments @('plugin', 'update', '-s', $scope, $pluginId) -WorkingDirectory $workingDirectory
        }
        catch
        {
            $script:HadFailure = $true
            $script:FailureCount++
            Write-Log "Claude plugin update failed for ${pluginId}: $( $_.Exception.Message )"
        }
    }
}

function Update-AntigravityCli
{
    $command = Get-Command agy -ErrorAction SilentlyContinue
    $toolPath = if ($command)
    {
        $command.Source
    }
    else
    {
        Join-Path $env:LOCALAPPDATA 'agy\bin\agy.exe'
    }
    if (-not (Test-Path -LiteralPath $toolPath))
    {
        Write-Log 'Antigravity CLI (agy) not installed; update skipped'
        return
    }
    Invoke-LoggedCommand -FilePath $toolPath -Arguments @('update')
}

function Update-AntigravityDesktop
{
    if (-not (Test-Tool 'winget'))
    {
        throw 'winget not found; cannot check Antigravity Desktop updates'
    }
    $result = Invoke-CommandCapture -FilePath 'winget' -Arguments @(
        'list', '--id', 'Google.Antigravity', '--exact', '--disable-interactivity', '--accept-source-agreements'
    ) -NoLogCommand
    if ($result.ExitCode -eq -1978335212)
    {
        Write-Log 'Antigravity Desktop not installed; update skipped'
        return
    }
    if ($result.ExitCode -ne 0)
    {
        throw "winget list failed with exit code $( $result.ExitCode ): $( $result.Text )"
    }
    $result = Invoke-CommandCapture -FilePath 'winget' -Arguments @(
        'upgrade', '--id', 'Google.Antigravity', '--exact', '--source', 'winget',
        '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity'
    )
    if ($result.ExitCode -eq -1978335189)
    {
        Write-Log 'Antigravity Desktop is already up to date'
    }
    elseif ($result.ExitCode -ne 0)
    {
        throw "Antigravity Desktop upgrade failed with exit code $( $result.ExitCode )"
    }
}

function Invoke-UpdateStep
{
    param(
        [string]$Name,
        [scriptblock]$ScriptBlock
    )

    Write-Log "=== $Name ==="
    $failureBeforeStep = $script:FailureCount
    try
    {
        & $ScriptBlock
    }
    catch
    {
        $script:HadFailure = $true
        $script:FailureCount++
        Write-Log "$Name failed: $( $_.Exception.Message )"
        if ($Name -eq 'Codex desktop')
        {
            Write-AppxEvents
        }
    }
    if ($script:FailureCount -gt $failureBeforeStep)
    {
        Write-Log "=== FAILED: $Name ==="
    }
    else
    {
        Write-Log "=== COMPLETED: $Name ==="
    }
}

try
{
    . (Join-Path $PSScriptRoot 'Check-Updates.ps1')
    . (Join-Path $PSScriptRoot 'Manage-Components.ps1')
    if ($CheckOnly)
    {
        Write-Log 'Check-only mode: no installations, marketplace refreshes or application restarts'
    }
    $script:codexWasStopped = $false
    $script:claudeDesktopWasStopped = $false
    $script:HadFailure = $false
    $script:FailureCount = 0

    $steps = [ordered]@{
        'Codex desktop' = { Update-CodexDesktop }
        'Active Codex CLI' = { Update-ActiveCodexCli }
        'Codex npm package' = { Update-NpmGlobalPackage -PackageName '@openai/codex' -DisplayName 'Codex CLI' }
        'Codex plugins' = { Update-CodexPlugins }
        'Claude Desktop' = { Update-ClaudeDesktop }
        'Claude Code' = { Update-ClaudeCode }
        'Claude npm package' = { Update-NpmGlobalPackage -PackageName '@anthropic-ai/claude-code' -DisplayName 'Claude Code' }
        'Claude plugins' = { Update-ClaudePlugins }
        'Antigravity Desktop' = { Update-AntigravityDesktop }
        'Antigravity CLI' = { Update-AntigravityCli }
    }

    foreach ($step in $steps.GetEnumerator())
    {
        $stepVendor = if ($step.Key -match 'Codex')
        {
            'OpenAI'
        }
        elseif ($step.Key -match 'Claude')
        {
            'Anthropic'
        }
        else
        {
            'Google'
        }
        if ((-not $OnlyStage -or $step.Key -eq $OnlyStage) -and (-not $Vendor -or $stepVendor -eq $Vendor))
        {
            $failureBeforeAction = $script:FailureCount
            if ($CheckOnly)
            {
                $stageName = $step.Key
                Invoke-UpdateStep -Name $stageName -ScriptBlock { Invoke-CheckStage $stageName }
            }
            elseif ($Action -ne 'Update')
            {
                $stageName = $step.Key
                Invoke-UpdateStep -Name $stageName -ScriptBlock { Invoke-ComponentAction -Stage $stageName -Action $Action }
            }
            else
            {
                Invoke-UpdateStep -Name $step.Key -ScriptBlock $step.Value
            }
            if (-not $CheckOnly -and $script:FailureCount -eq $failureBeforeAction)
            {
                if ($Action -eq 'Uninstall')
                {
                    Write-CheckResult $step.Key 'NOT_INSTALLED' 'Component removed'
                }
                else
                {
                    try
                    {
                        Invoke-CheckStage $step.Key
                    }
                    catch
                    {
                        Write-CheckResult $step.Key 'UNKNOWN' "Post-action check failed: $( $_.Exception.Message )"
                    }
                }
            }
        }
    }

    if ($script:HadFailure)
    {
        if ($CheckOnly)
        {
            Write-Log 'Done with errors; check-only mode did not change applications'
        }
        else
        {
            Write-Log 'Done with errors; applications closed for updating will remain closed'
        }
        exit 1
    }

    if (-not $CheckOnly -and $Action -ne 'Uninstall')
    {
        Start-CodexProcess
        Start-ClaudeDesktopProcess
    }
    Write-Log 'Done'
}
catch
{
    Write-Log "Run failed: $( $_.Exception.Message )"
    Write-AppxEvents
    if ($CheckOnly)
    {
        Write-Log 'Check-only mode did not change applications'
    }
    else
    {
        Write-Log 'Applications closed for updating will remain closed because the update failed'
    }
    exit 1
}
