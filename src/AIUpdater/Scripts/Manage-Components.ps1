# Component lifecycle operations are deliberately limited to one selected component.
function Invoke-ComponentAction
{
    param([string]$Stage, [ValidateSet('Install', 'Uninstall')][string]$Action)

    if ($Stage -match 'plugins$')
    {
        throw 'Select an individual plugin in the application to install or uninstall it'
    }
    Write-Log "$Action component: $Stage"
    $desktopPackages = @{
        'Codex desktop' = @('9PLM9XGG6VKS', 'msstore')
        'Claude Desktop' = @('Anthropic.Claude', 'winget')
        'Antigravity Desktop' = @('Google.Antigravity', 'winget')
    }
    if ( $desktopPackages.ContainsKey($Stage))
    {
        if (-not (Test-Tool 'winget'))
        {
            throw 'winget is required'
        }
        $package = $desktopPackages[$Stage]
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

    if ($Stage -in @('Codex npm package', 'Claude npm package', 'Active Codex CLI'))
    {
        $npmPackage = if ($Stage -match 'Codex')
        {
            '@openai/codex'
        }
        else
        {
            '@anthropic-ai/claude-code'
        }
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

    $native = switch ($Stage)
    {
        'Claude Code' {
            @{ Tool = 'claude'; Path = (Join-Path $env:USERPROFILE '.local\bin\claude.exe'); Url = 'https://claude.ai/install.ps1' }
        }
        'Antigravity CLI' {
            @{ Tool = 'agy'; Path = (Join-Path $env:LOCALAPPDATA 'agy\bin\agy.exe'); Url = 'https://antigravity.google/cli/install.ps1' }
        }
        default {
            throw "Unsupported component: $Stage"
        }
    }
    if ($Action -eq 'Install')
    {
        if ((Test-Tool $native.Tool) -or (Test-Path -LiteralPath $native.Path))
        {
            Write-Log 'CLI is already installed'; return
        }
        $installer = Join-Path ([IO.Path]::GetTempPath()) ("AIUpdater-install-{0}.ps1" -f [guid]::NewGuid().ToString('N'))
        try
        {
            Invoke-WebRequest -Uri $native.Url -OutFile $installer -UseBasicParsing
            Invoke-LoggedCommand 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer)
            if (-not (Test-Path -LiteralPath $native.Path))
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
        $command = Get-Command $native.Tool -ErrorAction SilentlyContinue
        if ($command -and [IO.Path]::GetFullPath($command.Source) -ne [IO.Path]::GetFullPath($native.Path))
        {
            throw 'CLI uses a different installation method; uninstall it using its original installer or the npm component'
        }
        if (Test-Path -LiteralPath $native.Path)
        {
            Remove-Item -LiteralPath $native.Path -Force
        }
        else
        {
            Write-Log 'CLI is already uninstalled'
        }
        Write-Log 'CLI executable removed; user settings and conversations preserved'
    }
}
