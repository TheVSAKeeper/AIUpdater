$script:DotNetSdkMutexName = 'Global\AIUpdater.DotNetSdk'

function Get-StageDefinitions
{
    return [ordered]@{
        'Codex desktop' = @{ Vendor = 'OpenAI'; Kind = 'StoreDesktop'; PackageId = '9PLM9XGG6VKS'; Source = 'msstore'; AppxName = 'OpenAI.Codex'; PackageFamilyName = 'OpenAI.Codex_2p2nqsd0c76g0'; AppId = 'OpenAI.Codex_2p2nqsd0c76g0!App'; CanManage = $true }
        'Active Codex CLI' = @{ Vendor = 'OpenAI'; Kind = 'NpmCli'; Tool = 'codex'; NpmPackage = '@openai/codex'; Path = (Join-Path $env:LOCALAPPDATA 'Programs\OpenAI\Codex\bin\codex.exe'); Url = 'https://chatgpt.com/codex/install.ps1'; CanManage = $true }
        'Codex npm package' = @{ Vendor = 'OpenAI'; Kind = 'NpmGlobal'; Tool = 'codex'; NpmPackage = '@openai/codex'; DisplayName = 'Codex CLI'; CanManage = $true }
        'Codex plugins' = @{ Vendor = 'OpenAI'; Kind = 'Plugins'; Tool = 'codex'; CanManage = $false }
        'Claude Desktop' = @{ Vendor = 'Anthropic'; Kind = 'WingetDesktop'; PackageId = 'Anthropic.Claude'; Source = 'winget'; AppId = 'Claude_pzs8sxrjxfjjc!Claude'; CanManage = $true }
        'Claude Code' = @{ Vendor = 'Anthropic'; Kind = 'NativeCli'; Tool = 'claude'; NpmPackage = '@anthropic-ai/claude-code'; Path = (Join-Path $env:USERPROFILE '.local\bin\claude.exe'); Url = 'https://claude.ai/install.ps1'; CanManage = $true }
        'Claude npm package' = @{ Vendor = 'Anthropic'; Kind = 'NpmGlobal'; Tool = 'claude'; NpmPackage = '@anthropic-ai/claude-code'; DisplayName = 'Claude Code'; CanManage = $true }
        'Claude plugins' = @{ Vendor = 'Anthropic'; Kind = 'Plugins'; Tool = 'claude'; CanManage = $false }
        'Antigravity Desktop' = @{ Vendor = 'Google'; Kind = 'WingetDesktop'; PackageId = 'Google.Antigravity'; Source = 'winget'; CanManage = $true }
        'Antigravity CLI' = @{ Vendor = 'Google'; Kind = 'AgyCli'; Tool = 'agy'; Path = (Join-Path $env:LOCALAPPDATA 'agy\bin\agy.exe'); Url = 'https://antigravity.google/cli/install.ps1'; CanManage = $true }
        'DSH' = @{ Vendor = 'DeepSeek'; Kind = 'NpmGlobal'; Tool = 'dsh'; NpmPackage = '@deepseek-ai/dsh'; DisplayName = 'DSH'; Channel = 'latest'; CanManage = $true }
        'Windows Terminal' = @{ Vendor = 'Разработка'; Kind = 'Development'; PackageId = '9N0DX20HK701'; Source = 'msstore'; CanManage = $true }
        'PowerShell 7' = @{ Vendor = 'Разработка'; Kind = 'Development'; Tool = 'pwsh'; PackageId = 'Microsoft.PowerShell'; Source = 'winget'; CanManage = $true }
        'Docker Desktop' = @{ Vendor = 'Разработка'; Kind = 'Development'; PackageId = 'Docker.DockerDesktop'; Source = 'winget'; CanManage = $true }
        '.NET SDK 10' = @{ Vendor = 'Разработка'; Kind = 'Development'; PackageId = 'Microsoft.DotNet.SDK.10'; Source = 'winget'; CanManage = $true }
        'Git' = @{ Vendor = 'Разработка'; Kind = 'Development'; PackageId = 'Git.Git'; Source = 'winget'; CanManage = $true }
        'Node.js LTS' = @{ Vendor = 'Разработка'; Kind = 'Development'; Tool = 'node'; PackageId = 'OpenJS.NodeJS.LTS'; Source = 'winget'; CanManage = $true }
    }
}

function Get-StageDefinition
{
    param([string]$Stage)
    $definition = (Get-StageDefinitions)[$Stage]
    if (-not $definition) { throw "Unknown stage: $Stage" }
    return $definition
}

function Get-DevelopmentPackages
{
    $packages = [ordered]@{}
    foreach ($entry in (Get-StageDefinitions).GetEnumerator())
    {
        if ($entry.Value.Kind -eq 'Development')
        {
            $packages[$entry.Key] = @($entry.Value.PackageId, $entry.Value.Source)
        }
    }
    return $packages
}

function Test-Tool
{
    param([string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function ConvertTo-VersionOrNull
{
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text))
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

function Resolve-NpmPackageChannel
{
    param([string]$PackageName, [string]$InstalledVersion)
    foreach ($entry in (Get-StageDefinitions).Values)
    {
        if ($entry.NpmPackage -eq $PackageName -and $entry.Channel)
        {
            return $entry.Channel
        }
    }
    return Get-NpmReleaseChannel $InstalledVersion
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
    for ($i = 0; $i -lt 4; $i++)
    {
        $a = if ($i -lt $leftCore.Count) { [decimal]$leftCore[$i] } else { 0 }
        $b = if ($i -lt $rightCore.Count) { [decimal]$rightCore[$i] } else { 0 }
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
    for ($i = 0; $i -lt [Math]::Min($aParts.Count, $bParts.Count); $i++)
    {
        $a = $aParts[$i]
        $b = $bParts[$i]
        $aNumeric = $a -match '^\d+$'
        $bNumeric = $b -match '^\d+$'
        if ($aNumeric -and $bNumeric)
        {
            $a = $a.TrimStart('0')
            $b = $b.TrimStart('0')
            $comparison = $a.Length.CompareTo($b.Length)
            if ($comparison -eq 0)
            {
                $comparison = [string]::CompareOrdinal($a, $b)
            }
        }
        elseif ($aNumeric -ne $bNumeric)
        {
            $comparison = if ($aNumeric) { -1 } else { 1 }
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

function Get-AgyExecutablePath
{
    $command = Get-Command agy -ErrorAction SilentlyContinue
    if ($command)
    {
        return $command.Source
    }
    return (Get-StageDefinition 'Antigravity CLI').Path
}

function Get-CodexPackage
{
    $definition = Get-StageDefinition 'Codex desktop'
    Get-AppxPackage -Name $definition.AppxName -ErrorAction SilentlyContinue
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

function Get-NpmLatestVersion
{
    param([string]$PackageName, [string]$InstalledVersion)

    if (-not (Test-Tool 'npm'))
    {
        Write-Log 'npm not found'
        return $null
    }

    $channel = Resolve-NpmPackageChannel $PackageName $InstalledVersion
    Write-Log "Checking npm release channel for ${PackageName}: $channel"
    $field = if ($channel -eq 'latest') { 'version' } else { "dist-tags.$channel" }
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
    elseif ($channel -ne 'latest' -and (Get-NpmReleaseChannel $latest) -ne $channel)
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

function Get-WingetPackageState
{
    param([string]$PackageId, [string]$Source)
    $result = Invoke-CommandCapture -FilePath 'winget' -Arguments @(
        'list', '--id', $PackageId, '--exact', '--source', $Source,
        '--accept-source-agreements', '--disable-interactivity'
    ) -NoLogCommand
    if ($result.ExitCode -eq -1978335212)
    {
        return [pscustomobject]@{ Status = 'NOT_INSTALLED'; Installed = $null; Available = $null; ExitCode = $result.ExitCode; Text = $result.Text }
    }
    if ($result.ExitCode -ne 0)
    {
        return [pscustomobject]@{ Status = 'COMMAND_FAILED'; Installed = $null; Available = $null; ExitCode = $result.ExitCode; Text = $result.Text }
    }
    foreach ($line in ($result.Text -split "`r?`n"))
    {
        if ($line -match "\b$([regex]::Escape($PackageId) )\s+([0-9]+(?:\.[0-9]+){1,3})(?:\s+([0-9]+(?:\.[0-9]+){1,3}))?(?:\s+$([regex]::Escape($Source) ))?\s*$")
        {
            return [pscustomobject]@{
                Status = 'INSTALLED'
                Installed = [version]$Matches[1]
                Available = if ($Matches[2]) { [version]$Matches[2] } else { $null }
                ExitCode = 0
                Text = $result.Text
            }
        }
    }
    return [pscustomobject]@{ Status = 'UNPARSED'; Installed = $null; Available = $null; ExitCode = 0; Text = $result.Text }
}

function Invoke-WingetUpgrade
{
    param([string]$Stage, [string]$PackageId, [string]$Source)
    if (-not (Test-Tool 'winget')) { throw 'winget is required' }
    $state = Invoke-CommandCapture 'winget' @(
        'list', '--id', $PackageId, '-e', '--source', $Source,
        '--accept-source-agreements', '--disable-interactivity'
    ) -NoLogCommand
    if ($state.ExitCode -eq -1978335212)
    {
        Write-Log "$Stage is not installed; update skipped"
        return
    }
    if ($state.ExitCode -ne 0) { throw "winget list failed: $( $state.ExitCode ): $( $state.Text )" }
    $result = Invoke-CommandCapture 'winget' @(
        'upgrade', '--id', $PackageId, '-e', '--source', $Source,
        '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity'
    )
    if ($result.ExitCode -eq -1978335189)
    {
        Write-Log "$Stage is already up to date"
    }
    elseif ($result.ExitCode -ne 0)
    {
        throw "$Stage upgrade failed: $( $result.ExitCode ): $( $result.Text )"
    }
}

function Get-DevelopmentToolOwner
{
    param([string]$Stage)
    if ($Stage -eq 'Node.js LTS')
    {
        $node = Get-Command node -ErrorAction SilentlyContinue
        if (-not $node)
        {
            if (Test-Tool 'nvm') { throw 'NVM is installed but no Node version is active; select or install a version through NVM' }
            return 'missing'
        }
        $nodePath = [IO.Path]::GetFullPath($node.Source)
        $nvmCommand = Get-Command nvm -ErrorAction SilentlyContinue
        $modernNodeRoot = $null
        $roots = @($env:NVM_HOME, $env:NVM_SYMLINK)
        if ($nvmCommand -and $nvmCommand.Source)
        {
            $nvmDirectory = Split-Path -Parent $nvmCommand.Source
            $modernNodeRoot = Join-Path $nvmDirectory '.nodejs'
            $roots += @($modernNodeRoot, (Join-Path $nvmDirectory '.shim'))
        }
        foreach ($root in $roots)
        {
            if ($root -and $nodePath.StartsWith([IO.Path]::GetFullPath($root).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase))
            {
                $linkRoot = if ($modernNodeRoot -and $nodePath.StartsWith($modernNodeRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { $modernNodeRoot }
                    elseif ($root -eq $env:NVM_SYMLINK) { $root } else { $null }
                if ($linkRoot)
                {
                    $entry = Get-Item -LiteralPath $linkRoot -Force -ErrorAction Stop
                    if (-not ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint))
                    {
                        throw 'NVM_SYMLINK points to a regular directory; resolve the competing Node installation before updating'
                    }
                }
                if (-not (Test-Tool 'nvm')) { throw 'Node belongs to NVM, but nvm is unavailable' }
                if ($nodePath -match '\\\.(shim|nodejs)\\')
                {
                    $configuration = Invoke-CommandCapture 'nvm' @('config', 'get', 'auto_install') -NoLogCommand
                    if ($configuration.ExitCode -ne 0 -or $configuration.Text -notmatch '\bfalse\b' -or $configuration.Text -match '\btrue\b')
                    {
                        throw 'NVM shim auto-install must be disabled before a read-only Node check; use link mode or disable auto_install in NVM'
                    }
                }
                return 'nvm'
            }
        }
        if ($nodePath -notmatch '\\nodejs\\node\.exe$')
        {
            throw "Unknown Node installation owner: $nodePath"
        }
    }
    if ($Stage -eq 'PowerShell 7')
    {
        $paths = @(Get-Command pwsh -All -ErrorAction SilentlyContinue | ForEach-Object Source | Select-Object -Unique)
        if ($paths.Count -gt 1) { throw 'Several PowerShell installations are on PATH; resolve the conflict before updating' }
        if ($paths.Count -eq 1 -and $paths[0] -match '\\.dotnet\\tools\\')
        {
            throw 'PowerShell is a .NET global tool; use dotnet tool update instead of winget'
        }
    }
    return 'winget'
}

function Get-DevelopmentPackage
{
    param([string]$Stage)
    $package = (Get-DevelopmentPackages)[$Stage]
    if (-not $package) { throw "Unsupported development tool: $Stage" }
    if ($Stage -eq 'Node.js LTS' -and (Test-Tool 'node'))
    {
        $installed = Get-ToolVersion 'node'
        if ($null -eq $installed) { throw 'Could not read the active Node version' }
        return @("OpenJS.NodeJS.$(([version]$installed).Major)", 'winget')
    }
    return $package
}

function Get-NodeLatestVersion
{
    param([string]$Installed)
    $major = ([version]$Installed).Major
    $response = Invoke-WebRequest -Uri 'https://nodejs.org/dist/index.json' -UseBasicParsing -TimeoutSec 20 -ErrorAction Stop
    if (-not ([string]$response.Content).TrimStart().StartsWith('[')) { throw 'Expected a Node release array' }
    $releases = $response.Content | ConvertFrom-Json -ErrorAction Stop
    $versions = @($releases | Where-Object { $_.version -is [string] -and $_.version -match "\Av$major\.[0-9]+\.[0-9]+\z" } |
        ForEach-Object { [version]$_.version.Substring(1) } | Sort-Object -Descending)
    if (-not $versions.Count) { throw "No published release for Node $major" }
    return $versions[0].ToString()
}

function Get-DotNetWindowsArchitecture
{
    $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    switch ($arch)
    {
        'AMD64' { return [pscustomobject]@{ Rid = 'win-x64'; Label = 'x64' } }
        'ARM64' { return [pscustomobject]@{ Rid = 'win-arm64'; Label = 'arm64' } }
        default { throw "Unsupported Windows architecture for .NET installer: $arch" }
    }
}

function Get-DotNetCliPath
{
    if (Test-Tool 'dotnet') { return 'dotnet' }
    $systemDotNet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $systemDotNet) { return $systemDotNet }
    return $null
}

function Test-DotNetTrustedInstallerUrl
{
    param([string]$Url, [string]$ExpectedFileName)
    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri)) { return $false }
    if ($uri.Scheme -cne 'https' -or -not [string]::IsNullOrEmpty($uri.Query) -or -not [string]::IsNullOrEmpty($uri.Fragment)) { return $false }
    if ($uri.Host -in @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com'))
    {
        return $uri.AbsolutePath.EndsWith("/$ExpectedFileName", [StringComparison]::OrdinalIgnoreCase)
    }
    return $false
}

$script:DotNetProgressHandler = {
    param([string]$Detail)
    [Console]::Out.WriteLine("[$(Get-Date -Format 'HH:mm:ss')] UI_PROGRESS $Detail")
    [Console]::Out.Flush()
}

function Publish-DotNetProgress
{
    param([string]$Detail)
    if ([string]::IsNullOrWhiteSpace($Detail)) { return }
    if ($script:DotNetProgressHandler)
    {
        & $script:DotNetProgressHandler $Detail
    }
}

function Write-DotNetStep
{
    param([string]$Message)
    Write-Log $Message
    Publish-DotNetProgress $Message
}

function Format-DotNetMegabytes
{
    param([long]$Bytes)
    return ([double]$Bytes / 1048576.0).ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
}

function Copy-StreamWithProgress
{
    param(
        [Parameter(Mandatory)][IO.Stream]$InputStream,
        [Parameter(Mandatory)][IO.Stream]$OutputStream,
        [long]$TotalBytes = -1,
        [Parameter(Mandatory)][string]$Label,
        [int]$ReportIntervalMs = 200
    )
    $buffer = New-Object byte[] 65536
    [long]$downloaded = 0
    $totalMb = if ($TotalBytes -gt 0) { Format-DotNetMegabytes $TotalBytes } else { '' }
    if ($TotalBytes -gt 0)
    {
        Publish-DotNetProgress "Скачивание ${Label}: 0% (0.0 / $totalMb МБ)"
    }
    else
    {
        Publish-DotNetProgress "Скачивание ${Label}: 0.0 МБ"
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    [long]$lastReportMs = -$ReportIntervalMs
    $lastPercent = 0
    while ($true)
    {
        $read = $InputStream.Read($buffer, 0, $buffer.Length)
        if ($read -le 0) { break }
        $OutputStream.Write($buffer, 0, $read)
        $downloaded += $read
        $elapsed = $watch.ElapsedMilliseconds
        if ($TotalBytes -gt 0)
        {
            $percent = [int][Math]::Min(99L, [Math]::Floor(($downloaded * 100.0) / $TotalBytes))
            if (($elapsed - $lastReportMs) -ge $ReportIntervalMs -and ($percent -ne $lastPercent -or ($elapsed - $lastReportMs) -ge 800))
            {
                $curMb = Format-DotNetMegabytes $downloaded
                Publish-DotNetProgress "Скачивание ${Label}: ${percent}% ($curMb / $totalMb МБ)"
                $lastReportMs = $elapsed
                $lastPercent = $percent
            }
        }
        elseif (($elapsed - $lastReportMs) -ge $ReportIntervalMs)
        {
            $curMb = Format-DotNetMegabytes $downloaded
            Publish-DotNetProgress "Скачивание ${Label}: $curMb МБ"
            $lastReportMs = $elapsed
        }
    }
    $OutputStream.Flush()
    $finalMb = Format-DotNetMegabytes $downloaded
    if ($TotalBytes -gt 0)
    {
        $shownTotal = if ($downloaded -gt $TotalBytes) { $finalMb } else { $totalMb }
        Publish-DotNetProgress "Скачивание ${Label}: 100% ($finalMb / $shownTotal МБ)"
    }
    else
    {
        Publish-DotNetProgress "Скачивание ${Label}: 100% ($finalMb МБ)"
    }
}

function Receive-DotNetInstallerFile
{
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$OutFile,
        [Parameter(Mandatory)][string]$Label,
        [int]$TimeoutSec = 600
    )
    $webCmd = Get-Command Invoke-WebRequest -ErrorAction SilentlyContinue
    if ($webCmd -and $webCmd.CommandType -eq 'Function')
    {
        Publish-DotNetProgress "Скачивание ${Label}: 0%"
        Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing -TimeoutSec $TimeoutSec -ErrorAction Stop
        Publish-DotNetProgress "Скачивание ${Label}: 100%"
        return
    }

    if (-not ('System.Net.Http.HttpClient' -as [type]))
    {
        Add-Type -AssemblyName System.Net.Http
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $partFile = "$OutFile.part"
    if (Test-Path -LiteralPath $partFile) { Remove-Item -LiteralPath $partFile -Force -ErrorAction SilentlyContinue }
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
    $response = $null
    try
    {
        $response = $client.GetAsync($Uri, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $null = $response.EnsureSuccessStatusCode()
        $totalBytes = if ($null -ne $response.Content.Headers.ContentLength) { [long]$response.Content.Headers.ContentLength } else { -1L }
        $inStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        try
        {
            $outStream = [IO.FileStream]::new($partFile, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None, 65536)
            try
            {
                Copy-StreamWithProgress -InputStream $inStream -OutputStream $outStream -TotalBytes $totalBytes -Label $Label
            }
            finally
            {
                $outStream.Dispose()
            }
        }
        finally
        {
            $inStream.Dispose()
        }
        if (Test-Path -LiteralPath $OutFile) { Remove-Item -LiteralPath $OutFile -Force }
        Move-Item -LiteralPath $partFile -Destination $OutFile -Force
    }
    catch
    {
        if (Test-Path -LiteralPath $partFile) { Remove-Item -LiteralPath $partFile -Force -ErrorAction SilentlyContinue }
        throw
    }
    finally
    {
        if ($response) { $response.Dispose() }
        $client.Dispose()
    }
}

function Get-DotNetInstalledSdks
{
    param([string]$Channel = '')
    $cli = Get-DotNetCliPath
    if (-not $cli) { return @() }
    $result = Invoke-CommandCapture $cli @('--list-sdks') -NoLogCommand
    if ($result.ExitCode -ne 0) { throw "Could not list .NET SDKs: $($result.Text)" }
    $pattern = if ($Channel)
    {
        "^$([regex]::Escape($Channel))\.[0-9]+(?:-[0-9A-Za-z.-]+)?\s+\["
    }
    else
    {
        '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\s+\['
    }
    return @($result.Text -split "`r?`n" | Where-Object { $_ -match $pattern } |
        ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -Unique)
}

function Get-DotNetInstalledRuntimes
{
    param([string]$SharedFramework = '', [string]$Channel = '')
    $cli = Get-DotNetCliPath
    if (-not $cli) { return @() }
    $result = Invoke-CommandCapture $cli @('--list-runtimes') -NoLogCommand
    if ($result.ExitCode -ne 0) { throw "Could not list .NET runtimes: $($result.Text)" }
    $items = foreach ($line in ($result.Text -split "`r?`n"))
    {
        if ($line -match '^(Microsoft\.(?:NETCore|WindowsDesktop|AspNetCore)\.App)\s+([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)\s+\[')
        {
            $name = $Matches[1]
            $ver = $Matches[2]
            if (($SharedFramework -and $name -ne $SharedFramework) -or ($Channel -and -not $ver.StartsWith("$Channel.", [StringComparison]::Ordinal)))
            {
                continue
            }
            [pscustomobject]@{ Name = $name; Version = $ver }
        }
    }
    return @($items)
}

function Get-DevelopmentSdkVersions
{
    if (-not (Test-Tool 'dotnet')) { return @() }
    $result = Invoke-CommandCapture 'dotnet' @('--list-sdks') -NoLogCommand
    if ($result.ExitCode -ne 0) { throw "Could not list .NET SDKs: $($result.Text)" }
    return @($result.Text -split "`r?`n" | Where-Object { $_ -match '^[1-9][0-9]*\.[0-9]+\.[0-9]+\s+\[' } |
        ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -Unique)
}

function Assert-DevelopmentUpdateAllowed
{
    param([string]$Stage)
    if ($Stage -eq '.NET SDK 10' -and @(Get-DevelopmentSdkVersions).Count -gt 1)
    {
        throw 'Several .NET SDK versions are installed. Check global.json pins and service the required SDKs individually; automatic winget upgrade was not run'
    }
}

function Get-DevelopmentSdkPlan
{
    param([string[]]$Installed)
    if (-not $Installed.Count) { return @() }
    $rid = (Get-DotNetWindowsArchitecture).Rid
    foreach ($channelGroup in ($Installed | Group-Object { $v = [version]$_; "$($v.Major).$($v.Minor)" }))
    {
        $channel = $channelGroup.Name
        Publish-DotNetProgress "Получение метаданных канала .NET $channel..."
        $response = Invoke-WebRequest -Uri "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$channel/releases.json" -UseBasicParsing -TimeoutSec 30 -ErrorAction Stop
        $metadata = $response.Content | ConvertFrom-Json -ErrorAction Stop
        if ($metadata.'channel-version' -ne $channel -or -not $metadata.releases) { throw "Invalid .NET $channel release metadata" }
        $published = @($metadata.releases | ForEach-Object { @($_.sdk) + @($_.sdks) } |
            Where-Object { $_ -and $_.version -match "^$([regex]::Escape($channel))\.[0-9]+$" })
        foreach ($group in ($channelGroup.Group | Group-Object { [math]::Floor(([version]$_).Build / 100) }))
        {
            $current = $group.Group | Sort-Object { [version]$_ } -Descending | Select-Object -First 1
            $release = $published | Where-Object { [math]::Floor(([version]$_.version).Build / 100) -eq [int]$group.Name } |
                Sort-Object { [version]$_.version } -Descending | Select-Object -First 1
            if (-not $release) { throw "No stable release metadata for SDK feature band $channel.$($group.Name)xx" }
            $installerName = "dotnet-sdk-$rid.exe"
            $versionedName = "dotnet-sdk-$($release.version)-$rid.exe"
            $file = @($release.files | Where-Object { $_.rid -eq $rid -and ($_.name -eq $installerName -or $_.name -eq $versionedName) } | Select-Object -First 1)
            if ($file.Count -ne 1 -or $file[0].hash -notmatch '^[a-fA-F0-9]{128}$') { throw 'Invalid .NET SDK installer metadata' }
            if (-not (Test-DotNetTrustedInstallerUrl -Url $file[0].url -ExpectedFileName $versionedName)) { throw 'Unexpected .NET SDK installer URL' }
            [pscustomobject]@{ Channel = $channel; Installed = $current; Available = $release.version; Update = [version]$release.version -gt [version]$current; Url = $file[0].url; Hash = $file[0].hash }
        }
    }
}

function Assert-DevelopmentSdkInstallOwner
{
    param([switch]$AllowMissingDotNet)
    $arch = Get-DotNetWindowsArchitecture
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $cliPath = if ($dotnet) { $dotnet.Source } else { Get-DotNetCliPath }
    if (-not $cliPath -and -not $AllowMissingDotNet)
    {
        throw 'dotnet CLI was not found on PATH'
    }
    if (-not [Environment]::Is64BitProcess -or ($cliPath -and $cliPath -ine "$env:ProgramFiles\dotnet\dotnet.exe"))
    {
        throw "SDK servicing supports the system-wide $($arch.Label) Microsoft installer installation only; inspect the active dotnet owner"
    }
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
    { throw 'SDK servicing requires an elevated updater to preserve installed SDKs through the Microsoft installer policy' }
}

function Get-DevelopmentSdkRetention
{
    param([string]$Channel = '10.0')
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $base.OpenSubKey("SOFTWARE\Microsoft\.NET\$Channel")
    try
    {
        $exists = $key -and 'RemovePreviousVersion' -in $key.GetValueNames()
        $value = $null
        $kind = $null
        if ($exists)
        {
            $value = $key.GetValue('RemovePreviousVersion', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $kind = "$($key.GetValueKind('RemovePreviousVersion'))"
        }
        return [pscustomobject]@{ Exists = [bool]$exists; Value = $value; Kind = $kind }
    }
    finally { if ($key) { $key.Dispose() }; $base.Dispose() }
}

function Set-DevelopmentSdkRetention
{
    param($Restore = $null, [string]$Channel = '10.0')
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $base.CreateSubKey("SOFTWARE\Microsoft\.NET\$Channel")
    try
    {
        if ($null -eq $Restore) { $key.SetValue('RemovePreviousVersion', 'never', [Microsoft.Win32.RegistryValueKind]::String) }
        else
        {
            if ($key.GetValue('RemovePreviousVersion') -ne 'never') { throw '.NET retention policy changed concurrently; previous value was not restored' }
            if ($Restore.Exists) { $key.SetValue('RemovePreviousVersion', $Restore.Value, [Microsoft.Win32.RegistryValueKind]::$($Restore.Kind)) }
            else { $key.DeleteValue('RemovePreviousVersion', $false) }
        }
    }
    finally { $key.Dispose(); $base.Dispose() }
}

function Invoke-VerifiedDotNetInstaller
{
    param(
        [string]$Channel,
        [string]$Url,
        [string]$ExpectedHash,
        [string]$InstallerFileName,
        [string]$EvidenceDirectory,
        [string]$Description
    )
    $installer = Join-Path $EvidenceDirectory $InstallerFileName
    Write-Log "Скачивание установщика $InstallerFileName ($Description)..."
    Receive-DotNetInstallerFile -Uri $Url -OutFile $installer -Label $InstallerFileName -TimeoutSec 600
    Write-DotNetStep "Проверка контрольной суммы SHA-512: $InstallerFileName..."
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA512).Hash -ine $ExpectedHash) { throw 'SDK installer SHA512 mismatch' }
    Write-DotNetStep "Проверка цифровой подписи Microsoft: $InstallerFileName..."
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation,') { throw 'SDK installer Microsoft signature is invalid' }
    $before = if ($Channel -eq '10.0') { Get-DevelopmentSdkRetention } else { Get-DevelopmentSdkRetention -Channel $Channel }
    $before | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory "retention-$Channel-before.json") -Encoding UTF8
    try
    {
        if ($Channel -eq '10.0') { Set-DevelopmentSdkRetention } else { Set-DevelopmentSdkRetention -Channel $Channel }
        $currentRetention = if ($Channel -eq '10.0') { Get-DevelopmentSdkRetention } else { Get-DevelopmentSdkRetention -Channel $Channel }
        if ($currentRetention.Value -ne 'never') { throw 'SDK retention policy could not be verified' }
        Write-DotNetStep "Установка $InstallerFileName (сохранение установленных версий)..."
        $logFile = Join-Path $EvidenceDirectory ("install-" + [IO.Path]::GetFileNameWithoutExtension($InstallerFileName) + ".log")
        $process = Start-Process -FilePath $installer -ArgumentList @('/install','/quiet','/norestart',"/log `"$logFile`"") -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin @(0,3010)) { throw "SDK installer failed: $($process.ExitCode)" }
        if ($process.ExitCode -eq 3010) { Write-Log 'SDK installation succeeded; Windows reports that a restart is required (not initiated)' }
    }
    finally
    {
        if ($Channel -eq '10.0') { Set-DevelopmentSdkRetention -Restore $before } else { Set-DevelopmentSdkRetention -Restore $before -Channel $Channel }
    }
}

function Update-DevelopmentSdk
{
    $ProgressPreference = 'SilentlyContinue'
    Publish-DotNetProgress 'Проверка установленных версий .NET SDK...'
    $installed = @(Get-DevelopmentSdkVersions)
    if (-not $installed.Count) { Write-Log '.NET SDK is not installed; update skipped'; return }
    $plan = @(Get-DevelopmentSdkPlan $installed | Where-Object Update)
    if (-not $plan.Count) { Write-Log 'Installed .NET SDK feature bands are up to date'; return }
    Assert-DevelopmentSdkInstallOwner
    $rid = (Get-DotNetWindowsArchitecture).Rid
    $mutex = [Threading.Mutex]::new($false, $script:DotNetSdkMutexName)
    $acquired = $false
    try
    {
        try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Another SDK servicing operation is running' }
        $directory = Join-Path (Split-Path -Parent $logPath) ('dotnet-sdk-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $directory
        $installed | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'sdks-before.json') -Encoding UTF8
        $runtimes = Invoke-CommandCapture 'dotnet' @('--list-runtimes') -NoLogCommand
        if ($runtimes.ExitCode -ne 0) { throw 'Could not inventory runtimes before SDK servicing' }
        $runtimes.Text | Set-Content -LiteralPath (Join-Path $directory 'runtimes-before.txt') -Encoding UTF8
        foreach ($item in $plan)
        {
            $channel = if ($item.PSObject.Properties['Channel'] -and $item.Channel) { $item.Channel } else { '10.0' }
            Invoke-VerifiedDotNetInstaller `
                -Channel $channel `
                -Url $item.Url `
                -ExpectedHash $item.Hash `
                -InstallerFileName "dotnet-sdk-$($item.Available)-$rid.exe" `
                -EvidenceDirectory $directory `
                -Description "Updating SDK feature band: $($item.Installed) -> $($item.Available)"
            Publish-DotNetProgress 'Проверка установленных SDK и сред выполнения...'
            $after = @(Get-DevelopmentSdkVersions)
            foreach ($version in @($installed) + @($item.Available))
            {
                if ($version -notin $after) { throw "Required SDK $version is missing after servicing; evidence: $directory" }
            }
            $runtimeAfter = Invoke-CommandCapture 'dotnet' @('--list-runtimes') -NoLogCommand
            if ($runtimeAfter.ExitCode -ne 0) { throw 'Could not verify runtimes after SDK servicing' }
            foreach ($line in ($runtimes.Text -split '\r?\n' | Where-Object { $_.Trim() }))
            {
                if ($line.Trim() -notin @($runtimeAfter.Text -split '\r?\n' | ForEach-Object { $_.Trim() }))
                { throw "A previous runtime is missing after servicing: $line" }
            }
        }
        Write-DotNetStep "Обновление .NET SDK проверено; предыдущие версии сохранены"
    }
    finally { if ($acquired) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
}

function Resolve-DotNetReleaseInstaller
{
    param(
        [Parameter(Mandatory)]$Metadata,
        [Parameter(Mandatory)][string]$Channel,
        [Parameter(Mandatory)][ValidateSet('Sdk', 'DesktopRuntime', 'AspNetCoreRuntime', 'Runtime')][string]$Kind,
        [string]$Version = '',
        [switch]$IncludePrerelease
    )
    if ($Metadata.'channel-version' -ne $Channel -or -not $Metadata.releases)
    {
        throw "Invalid .NET $Channel release metadata"
    }
    $rid = (Get-DotNetWindowsArchitecture).Rid
    $prefix = switch ($Kind)
    {
        'Sdk' { 'dotnet-sdk' }
        'Runtime' { 'dotnet-runtime' }
        'DesktopRuntime' { 'windowsdesktop-runtime' }
        'AspNetCoreRuntime' { 'aspnetcore-runtime' }
    }
    $installerName = "$prefix-$rid.exe"
    $entries = switch ($Kind)
    {
        'Sdk' { @($Metadata.releases | ForEach-Object { @($_.sdk) + @($_.sdks) } | Where-Object { $_ -and $_.version }) }
        'Runtime' { @($Metadata.releases | ForEach-Object { $_.runtime } | Where-Object { $_ -and $_.version }) }
        'DesktopRuntime' { @($Metadata.releases | ForEach-Object { $_.windowsdesktop } | Where-Object { $_ -and $_.version }) }
        'AspNetCoreRuntime' { @($Metadata.releases | ForEach-Object { $_.'aspnetcore-runtime' } | Where-Object { $_ -and $_.version }) }
    }
    $allowPre = [bool]$IncludePrerelease -or ($Version -match '-')
    $candidates = @($entries | Where-Object {
        $ver = [string]$_.version
        if (-not $ver.StartsWith("$Channel.", [StringComparison]::Ordinal)) { return $false }
        if (-not $allowPre -and $ver -match '-') { return $false }
        if ($Version -and $ver -cne $Version) { return $false }
        return $true
    })
    if (-not $candidates.Count -and -not $Version -and $Metadata.'support-phase' -in @('preview', 'go-live'))
    {
        $candidates = @($entries | Where-Object { ([string]$_.version).StartsWith("$Channel.", [StringComparison]::Ordinal) })
    }
    if (-not $candidates.Count)
    {
        throw "No matching .NET $Kind release found in channel $Channel"
    }
    $selected = $candidates[0]
    foreach ($candidate in $candidates | Select-Object -Skip 1)
    {
        if ((Compare-PackageVersions $selected.version $candidate.version) -lt 0)
        {
            $selected = $candidate
        }
    }
    $versionedFileName = "$prefix-$($selected.version)-$rid.exe"
    $file = @($selected.files | Where-Object { $_.rid -eq $rid -and ($_.name -eq $installerName -or $_.name -eq $versionedFileName) } | Select-Object -First 1)
    if ($file.Count -ne 1 -or $file[0].hash -notmatch '^[a-fA-F0-9]{128}$')
    {
        throw "Invalid installer metadata for .NET $Kind $($selected.version)"
    }
    if (-not (Test-DotNetTrustedInstallerUrl -Url $file[0].url -ExpectedFileName $versionedFileName))
    {
        throw "Untrusted installer URL for .NET $Kind $($selected.version)"
    }
    return [pscustomobject]@{
        Channel = $Channel
        Kind = $Kind
        Version = [string]$selected.version
        FileName = $versionedFileName
        Url = [string]$file[0].url
        Hash = [string]$file[0].hash
    }
}

function Get-DotNetUninstallEntries
{
    $paths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    return @(Get-ItemProperty -Path $paths -ErrorAction SilentlyContinue | Where-Object {
        $_.DisplayName -and $_.SystemComponent -ne 1 -and $_.Publisher -eq 'Microsoft Corporation' -and $_.QuietUninstallString
    })
}

function Find-DotNetStandaloneBundle
{
    param(
        [Parameter(Mandatory)][ValidateSet('Sdk', 'DesktopRuntime', 'AspNetCoreRuntime', 'Runtime')][string]$Kind,
        [Parameter(Mandatory)][string]$Version
    )
    $archLabel = (Get-DotNetWindowsArchitecture).Label
    $escapedVer = [regex]::Escape($Version)
    $escapedArch = [regex]::Escape($archLabel)
    $namePattern = switch ($Kind)
    {
        'Sdk' { "^Microsoft \.NET(?: Core)? SDK(?: -)? $escapedVer \($escapedArch\)$" }
        'Runtime' { "^Microsoft \.NET(?: Core)? Runtime - $escapedVer \($escapedArch\)$" }
        'DesktopRuntime' { "^Microsoft Windows Desktop Runtime - $escapedVer \($escapedArch\)$" }
        'AspNetCoreRuntime' { "^Microsoft ASP\.NET Core (?:$escapedVer - Shared Framework|Runtime - $escapedVer) \($escapedArch\)$" }
    }
    $entry = @(Get-DotNetUninstallEntries | Where-Object { $_.DisplayName -match $namePattern }) | Select-Object -First 1
    if (-not $entry)
    {
        throw "Standalone Microsoft installer bundle for .NET $Kind $Version ($archLabel) was not found; if installed through Visual Studio, manage it in Visual Studio Installer"
    }
    $quiet = [string]$entry.QuietUninstallString
    if ($quiet -notmatch '^\s*"([^"]+\.exe)"\s+/uninstall')
    {
        throw "Unsupported uninstall command format for $($entry.DisplayName)"
    }
    $exePath = [IO.Path]::GetFullPath($Matches[1])
    $cacheRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'Package Cache')).TrimEnd('\') + '\'
    if (-not $exePath.StartsWith($cacheRoot, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $exePath))
    {
        throw "Cached Microsoft bundle executable was not found at $exePath"
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $exePath
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation,')
    {
        throw "Cached bundle uninstaller signature is invalid: $exePath"
    }
    return [pscustomobject]@{
        DisplayName = [string]$entry.DisplayName
        ExecutablePath = $exePath
    }
}

function Invoke-DotNetPackageOperation
{
    param(
        [Parameter(Mandatory)][ValidateSet('Install', 'Update', 'Uninstall')][string]$Action,
        [Parameter(Mandatory)][ValidateSet('Sdk', 'DesktopRuntime', 'AspNetCoreRuntime', 'Runtime')][string]$Kind,
        [Parameter(Mandatory)][string]$Channel,
        [string]$Version = '',
        [switch]$IncludePrerelease
    )
    $ProgressPreference = 'SilentlyContinue'
    if ($Channel -notmatch '^[0-9]+\.[0-9]+$') { throw "Invalid .NET channel: $Channel" }
    Assert-DevelopmentSdkInstallOwner -AllowMissingDotNet:($Action -eq 'Install')

    if ($Action -eq 'Update' -and $Kind -eq 'Sdk' -and -not $Version)
    {
        $allChannelSdks = @(Get-DotNetInstalledSdks -Channel $Channel)
        if (-not $allChannelSdks.Count)
        {
            throw ".NET SDK $Channel is not installed; use Install instead"
        }
        $channelSdks = @($allChannelSdks | Where-Object { $_ -notmatch '-' })
        if ($channelSdks.Count)
        {
            $plan = @(Get-DevelopmentSdkPlan $channelSdks | Where-Object Update)
            if (-not $plan.Count)
            {
                Write-Log ".NET SDK $Channel feature bands are already up to date"
                return
            }
            $rid = (Get-DotNetWindowsArchitecture).Rid
            $mutex = [Threading.Mutex]::new($false, $script:DotNetSdkMutexName)
            $acquired = $false
            try
            {
                try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
                if (-not $acquired) { throw 'Another .NET installer operation is running' }
                $directory = Join-Path (Split-Path -Parent $logPath) ('dotnet-op-' + [guid]::NewGuid().ToString('N'))
                $null = New-Item -ItemType Directory -Path $directory
                $allSdksBefore = @(Get-DotNetInstalledSdks)
                $allRuntimesBefore = @(Get-DotNetInstalledRuntimes)
                $allSdksBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'sdks-before.json') -Encoding UTF8
                $allRuntimesBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'runtimes-before.json') -Encoding UTF8
                foreach ($item in $plan)
                {
                    Invoke-VerifiedDotNetInstaller `
                        -Channel $Channel `
                        -Url $item.Url `
                        -ExpectedHash $item.Hash `
                        -InstallerFileName "dotnet-sdk-$($item.Available)-$rid.exe" `
                        -EvidenceDirectory $directory `
                        -Description "Updating .NET SDK $Channel feature band: $($item.Installed) -> $($item.Available)"
                    Publish-DotNetProgress "Проверка установленных версий .NET $Channel..."
                    $sdksAfter = @(Get-DotNetInstalledSdks)
                    foreach ($ver in @($allSdksBefore) + @($item.Available))
                    {
                        if ($ver -notin $sdksAfter) { throw "Required SDK $ver is missing after servicing; evidence: $directory" }
                    }
                    $runtimesAfter = @(Get-DotNetInstalledRuntimes)
                    foreach ($rt in $allRuntimesBefore)
                    {
                        if (-not @($runtimesAfter | Where-Object { $_.Name -eq $rt.Name -and $_.Version -eq $rt.Version }).Count)
                        {
                            throw "Previous runtime $($rt.Name) $($rt.Version) is missing after servicing; evidence: $directory"
                        }
                    }
                }
                Write-Log ".NET SDK $Channel servicing completed. Evidence: $directory"
                return
            }
            finally { if ($acquired) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
        }
    }

    if ($Action -eq 'Uninstall')
    {
        if (-not $Version) { throw 'Uninstalling a .NET component requires an explicit version' }
        Publish-DotNetProgress "Поиск системного установщика .NET $Kind $Version..."
        $bundle = Find-DotNetStandaloneBundle -Kind $Kind -Version $Version
        $mutex = [Threading.Mutex]::new($false, $script:DotNetSdkMutexName)
        $acquired = $false
        try
        {
            try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
            if (-not $acquired) { throw 'Another .NET installer operation is running' }
            $directory = Join-Path (Split-Path -Parent $logPath) ('dotnet-uninstall-' + [guid]::NewGuid().ToString('N'))
            $null = New-Item -ItemType Directory -Path $directory
            $uninstallLog = Join-Path $directory "uninstall-$Kind-$Version.log"
            Write-DotNetStep "Удаление $($bundle.DisplayName)..."
            $process = Start-Process -FilePath $bundle.ExecutablePath -ArgumentList @('/uninstall','/quiet','/norestart',"/log `"$uninstallLog`"") -WindowStyle Hidden -Wait -PassThru
            if ($process.ExitCode -notin @(0,3010)) { throw "Bundle uninstaller failed with exit code $($process.ExitCode)" }
            if ($process.ExitCode -eq 3010) { Write-Log 'Uninstallation succeeded; Windows reports that a restart is required (not initiated)' }
            Write-DotNetStep "Удалено: $($bundle.DisplayName)"
            return
        }
        finally { if ($acquired) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
    }

    Write-DotNetStep "Получение метаданных канала .NET $Channel..."
    $response = Invoke-WebRequest -Uri "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$Channel/releases.json" -UseBasicParsing -TimeoutSec 30 -ErrorAction Stop
    $metadata = $response.Content | ConvertFrom-Json -ErrorAction Stop
    $artifact = Resolve-DotNetReleaseInstaller -Metadata $metadata -Channel $Channel -Kind $Kind -Version $Version -IncludePrerelease:$IncludePrerelease
    $mutex = [Threading.Mutex]::new($false, $script:DotNetSdkMutexName)
    $acquired = $false
    try
    {
        try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Another .NET installer operation is running' }
        $directory = Join-Path (Split-Path -Parent $logPath) ('dotnet-install-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $directory
        $sdksBefore = @(Get-DotNetInstalledSdks)
        $runtimesBefore = @(Get-DotNetInstalledRuntimes)
        $sdksBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'sdks-before.json') -Encoding UTF8
        $runtimesBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'runtimes-before.json') -Encoding UTF8
        Invoke-VerifiedDotNetInstaller `
            -Channel $Channel `
            -Url $artifact.Url `
            -ExpectedHash $artifact.Hash `
            -InstallerFileName $artifact.FileName `
            -EvidenceDirectory $directory `
            -Description "Installing .NET $Kind $($artifact.Version)"
        Publish-DotNetProgress "Проверка установленных версий .NET..."
        $sdksAfter = @(Get-DotNetInstalledSdks)
        foreach ($ver in $sdksBefore)
        {
            if ($ver -notin $sdksAfter) { throw "Previously installed SDK $ver is missing after installation; evidence: $directory" }
        }
        $runtimesAfter = @(Get-DotNetInstalledRuntimes)
        foreach ($rt in $runtimesBefore)
        {
            if (-not @($runtimesAfter | Where-Object { $_.Name -eq $rt.Name -and $_.Version -eq $rt.Version }).Count)
            {
                throw "Previously installed runtime $($rt.Name) $($rt.Version) is missing after installation; evidence: $directory"
            }
        }
        if ($Kind -eq 'Sdk')
        {
            if ($artifact.Version -notin $sdksAfter)
            {
                throw ".NET SDK $($artifact.Version) was not detected after installer completed; evidence: $directory"
            }
        }
        else
        {
            $expectedFramework = switch ($Kind)
            {
                'Runtime' { 'Microsoft.NETCore.App' }
                'DesktopRuntime' { 'Microsoft.WindowsDesktop.App' }
                'AspNetCoreRuntime' { 'Microsoft.AspNetCore.App' }
            }
            if (-not @($runtimesAfter | Where-Object { $_.Name -eq $expectedFramework -and $_.Version -eq $artifact.Version }).Count)
            {
                throw ".NET runtime $expectedFramework $($artifact.Version) was not detected after installer completed; evidence: $directory"
            }
        }
        Write-Log "Installed .NET $Kind $($artifact.Version); previous versions retained. Evidence: $directory"
    }
    finally { if ($acquired) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
}

function Update-DevelopmentTool
{
    param([string]$Stage)
    if ($Stage -eq '.NET SDK 10') { Update-DevelopmentSdk; return }
    $owner = Get-DevelopmentToolOwner $Stage
    if ($owner -eq 'missing') { Write-Log "$Stage is not installed; update skipped"; return }
    if ($owner -eq 'nvm')
    {
        $installed = Get-ToolVersion 'node'
        if ($null -eq $installed) { throw 'Could not read the active Node version' }
        $available = Get-NodeLatestVersion $installed
        if ((Compare-PackageVersions $installed $available) -ge 0) { Write-Log 'Node is already up to date in its release line'; return }
        Invoke-LoggedCommand 'nvm' @('install', $available)
        Invoke-LoggedCommand 'nvm' @('use', $available)
        $actual = Get-ToolVersion 'node'
        if ($null -eq $actual -or (Compare-PackageVersions $actual $available) -ne 0)
        {
            throw "Node $available was requested, but the active version is $actual; check project pins and NVM configuration"
        }
        return
    }
    Assert-DevelopmentUpdateAllowed $Stage
    $package = Get-DevelopmentPackage $Stage
    Invoke-WingetUpgrade -Stage $Stage -PackageId $package[0] -Source $package[1]
}

function Invoke-ComponentAction
{
    param([string]$Stage, [ValidateSet('Install', 'Uninstall')][string]$Action)

    $definition = Get-StageDefinition $Stage
    if (-not $definition.CanManage)
    {
        throw 'Select an individual plugin in the application to install or uninstall it'
    }
    Write-Log "$Action component: $Stage"
    if ($Stage -in @('Node.js LTS', 'PowerShell 7'))
    {
        $owner = Get-DevelopmentToolOwner $Stage
        if ($owner -eq 'nvm') { throw 'Node is managed by NVM; install or remove its versions through NVM' }
    }
    if ($Stage -eq '.NET SDK 10') { Assert-DevelopmentUpdateAllowed $Stage }

    if ($definition.Kind -in @('StoreDesktop', 'WingetDesktop', 'Development'))
    {
        if (-not (Test-Tool 'winget'))
        {
            throw 'winget is required'
        }
        $package = if ($Stage -eq 'Node.js LTS') { Get-DevelopmentPackage $Stage } else { @($definition.PackageId, $definition.Source) }
        if ($Action -eq 'Install')
        {
            Invoke-LoggedCommand 'winget' @('install', '--id', $package[0], '--exact', '--source', $package[1], '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity')
            if ($Stage -eq 'Codex desktop' -and $null -eq (Get-CodexVersion))
            {
                throw 'Desktop package was not found after installation'
            }
        }
        elseif ($Stage -eq 'Codex desktop')
        {
            $installed = Get-CodexPackage
            if ($installed)
            {
                $installed | Remove-AppxPackage -ErrorAction Stop
            }
            else
            {
                Write-Log 'Application is already uninstalled'
            }
        }
        else
        {
            $result = Invoke-CommandCapture 'winget' @('uninstall', '--id', $package[0], '--exact', '--source', $package[1], '--accept-source-agreements', '--disable-interactivity')
            if ($result.ExitCode -ne 0 -and $result.ExitCode -ne -1978335212)
            {
                throw "Uninstall failed: $( $result.ExitCode )"
            }
        }
        return
    }

    if ($definition.Kind -in @('NpmCli', 'NpmGlobal'))
    {
        $npmPackage = $definition.NpmPackage
        if ($Stage -eq 'Active Codex CLI' -and (Test-Tool 'codex'))
        {
            if ($Action -eq 'Install')
            {
                Write-Log 'Codex CLI is already installed'; return
            }
            if (-not (Test-Tool 'npm'))
            {
                throw 'Active Codex CLI is not managed by npm; use its original installer to uninstall it'
            }
            $prefix = Invoke-CommandCapture 'npm' @('prefix', '-g') -NoLogCommand
            if ($prefix.ExitCode -ne 0)
            {
                throw 'Cannot determine npm installation directory'
            }
            $command = Get-Command codex -ErrorAction Stop
            if ([IO.Path]::GetDirectoryName($command.Source) -ne $prefix.Text.Trim())
            {
                throw 'Active Codex CLI is not an npm installation; uninstall it using its original installer'
            }
        }
        if (-not (Test-Tool 'npm'))
        {
            throw 'Install Node.js with npm first'
        }
        if ($Action -eq 'Install')
        {
            Invoke-LoggedCommand 'npm' @('install', '-g', "$npmPackage@latest")
        }
        else
        {
            Invoke-LoggedCommand 'npm' @('uninstall', '-g', $npmPackage)
        }
        return
    }

    if ($Action -eq 'Install')
    {
        if ((Test-Tool $definition.Tool) -or (Test-Path -LiteralPath $definition.Path))
        {
            Write-Log 'CLI is already installed'; return
        }
        $installer = Join-Path ([IO.Path]::GetTempPath()) ("AIUpdater-install-{0}.ps1" -f [guid]::NewGuid().ToString('N'))
        try
        {
            Invoke-WebRequest -Uri $definition.Url -OutFile $installer -UseBasicParsing
            Invoke-LoggedCommand 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer)
            if (-not (Test-Path -LiteralPath $definition.Path))
            {
                throw 'Installer finished without creating the CLI executable'
            }
        }
        finally
        {
            if (Test-Path -LiteralPath $installer)
            {
                Remove-Item -LiteralPath $installer -Force
            }
        }
    }
    else
    {
        $command = Get-Command $definition.Tool -ErrorAction SilentlyContinue
        if ($command -and [IO.Path]::GetFullPath($command.Source) -ne [IO.Path]::GetFullPath($definition.Path))
        {
            throw 'CLI uses a different installation method; uninstall it using its original installer or the npm component'
        }
        if (Test-Path -LiteralPath $definition.Path)
        {
            Remove-Item -LiteralPath $definition.Path -Force
        }
        else
        {
            Write-Log 'CLI is already uninstalled'
        }
        Write-Log 'CLI executable removed; user settings and conversations preserved'
    }
}
