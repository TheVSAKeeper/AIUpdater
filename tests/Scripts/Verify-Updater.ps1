$ErrorActionPreference = 'Stop'
$scriptsDirectory = Join-Path $PSScriptRoot '..\..\src\AIUpdater\Scripts'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $scriptsDirectory 'Update-Codex.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count)
{
    throw ($parseErrors | Out-String)
}
# Load function definitions without running installers, elevation or application shutdown.
foreach ($function in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false))
{
    Invoke-Expression $function.Extent.Text
}
$script:messages = [System.Collections.Generic.List[string]]::new()
$script:commands = [System.Collections.Generic.List[string]]::new()
function Write-Log
{
    param($Message) $script:messages.Add($Message)
}
function Test-Tool
{
    param($Name) return $true
}
function Invoke-LoggedCommand
{
    param($FilePath, $Arguments, $WorkingDirectory) $script:commands.Add("$FilePath $( $Arguments -join ' ' )")
}
function Invoke-CommandCapture
{
    param($FilePath, $Arguments, $WorkingDirectory, [switch]$NoLogCommand)
    if ($FilePath -eq 'claude')
    {
        return [pscustomobject]@{ ExitCode = 0; Text = '[{"id":"missing@market","scope":"project","projectPath":"C:\\AIUpdater-nonexistent-test-project"},{"id":"valid@market","scope":"user","version":"1"}]' }
    }
    return [pscustomobject]@{ ExitCode = $script:wingetExitCode; Text = 'mock winget result' }
}
$script:HadFailure = $false
$script:FailureCount = 0
Update-ClaudePlugins
if ($script:HadFailure)
{
    throw 'Missing project directory incorrectly fails the update'
}
if ($script:commands -match 'missing@market')
{
    throw 'Attempted update in missing project'
}
if (-not ($script:commands -match 'valid@market'))
{
    throw 'Valid user plugin was not updated'
}
if (-not ($script:messages -match 'project directory no longer exists'))
{
    throw 'Missing project was not reported'
}

$script:wingetExitCode = 0
Update-AntigravityDesktop
$script:wingetExitCode = -1978335189
# Mock list succeeds, while upgrade reports no available update.
function Invoke-CommandCapture
{
    param($FilePath, $Arguments)
    return [pscustomobject]@{
        ExitCode = $( if ($Arguments[0] -eq 'list')
        {
            0
        }
        else
        {
            $script:wingetExitCode
        } ); Text = 'mock'
    }
}
Update-AntigravityDesktop
if (-not ($script:messages -match 'already up to date'))
{
    throw 'No-update result was not accepted'
}
$script:wingetExitCode = 123
$failed = $false
try
{
    Update-AntigravityDesktop
}
catch
{
    $failed = $true
}
if (-not $failed)
{
    throw 'Installer error incorrectly accepted'
}

Invoke-UpdateStep -Name 'Antigravity CLI' -ScriptBlock { throw 'simulated failure' }
Invoke-UpdateStep -Name 'Claude Code' -ScriptBlock { }
if (-not ($script:messages -contains '=== FAILED: Antigravity CLI ==='))
{
    throw 'Missing failed stage result'
}
if (-not ($script:messages -contains '=== COMPLETED: Claude Code ==='))
{
    throw 'A previous error leaked into the next stage'
}
Write-Output 'PASS: missing project skip, valid plugin update, winget outcomes, independent stage results'

. (Join-Path $scriptsDirectory 'Check-Updates.ps1')
$script:commands.Clear()
$script:messages.Clear()
function Invoke-LoggedCommand
{
    throw 'Check-only mode attempted a modifying command'
}
function Stop-CodexProcess
{
    throw 'Check-only mode stopped Codex'
}
function Stop-ClaudeDesktopProcess
{
    throw 'Check-only mode stopped Claude'
}
function Start-CodexProcess
{
    throw 'Check-only mode restarted Codex'
}
function Write-AppxEvents
{
}
function Start-ClaudeDesktopProcess
{
    throw 'Check-only mode restarted Claude'
}
function Get-CodexVersion
{
    return [version]'1.0.0'
}
function Get-CodexPackagedStoreUpdateState
{
    return [pscustomobject]@{ Success = $true; Updates = @([pscustomobject]@{ Name = 'OpenAI.Codex'; Version = '2.0.0' }) }
}
$script:testScriptPath = $PSCommandPath
function Get-Command
{
    param($Name, $ErrorAction) return [pscustomobject]@{ Source = $script:testScriptPath }
}
function Invoke-CommandCapture
{
    param($FilePath, $Arguments, $WorkingDirectory, [switch]$NoLogCommand)
    $script:commands.Add("$FilePath $( $Arguments -join ' ' )")
    if ($Arguments[0] -eq '--version')
    {
        return [pscustomobject]@{ ExitCode = 0; Text = '1.0.0' }
    }
    if ($FilePath -eq 'npm' -and $Arguments[0] -eq 'view')
    {
        return [pscustomobject]@{ ExitCode = 0; Text = '2.0.0' }
    }
    if ($FilePath -eq 'npm' -and $Arguments[0] -eq 'list')
    {
        return [pscustomobject]@{ ExitCode = 0; Text = '{"dependencies":{"@openai/codex":{"version":"1.0.0"},"@anthropic-ai/claude-code":{"version":"1.0.0"}}}' }
    }
    if ($FilePath -eq 'winget' -and $Arguments[0] -eq 'list')
    {
        $packageId = $Arguments[2]
        return [pscustomobject]@{ ExitCode = 0; Text = "Application $packageId 1.0.0 2.0.0 winget" }
    }
    if ($Arguments[0] -eq 'plugin' -and $Arguments[1] -eq 'list')
    {
        $text = if ($script:pluginTestText)
        {
            $script:pluginTestText
        }
        elseif ($FilePath -eq 'codex')
        {
            '{"installed":[{"pluginId":"first"},{"pluginId":"second"}]}'
        }
        else
        {
            '[{"id":"first"}]'
        }
        return [pscustomobject]@{ ExitCode = 0; Text = $text }
    }
    throw "Check-only mode attempted an unexpected command: $FilePath $( $Arguments -join ' ' )"
}

# Exercise the actual dispatcher, including the branch that normally starts applications.
$CheckOnly = $true
$Action = 'Update'
$OnlyStage = $null
$Vendor = $null
$dispatch = $ast.EndBlock.Statements | Where-Object {
    $_ -is [System.Management.Automation.Language.TryStatementAst]
} | Select-Object -Last 1
$checkScriptPath = Join-Path $scriptsDirectory 'Check-Updates.ps1'
$manageScriptPath = Join-Path $scriptsDirectory 'Manage-Components.ps1'
$dispatchText = $dispatch.Extent.Text.Replace(". (Join-Path `$PSScriptRoot 'Check-Updates.ps1')", '. $checkScriptPath').Replace(". (Join-Path `$PSScriptRoot 'Manage-Components.ps1')", '. $manageScriptPath').Replace('exit 1', 'throw ($script:messages -join "`n")')
Invoke-Expression $dispatchText
$checkResults = @($script:messages | Where-Object { $_ -match '^=== CHECK ' })
if ($checkResults.Count -ne 10)
{
    throw "Expected ten check results; received $( $checkResults.Count )"
}
if (@($checkResults | Where-Object { $_ -match 'CHECK AVAILABLE' }).Count -ne 7)
{
    throw 'Available versions were not detected'
}
if (@($checkResults | Where-Object { $_ -match 'CHECK DIRECT' }).Count -ne 3)
{
    throw 'Unsupported comparisons were not marked as direct updates'
}
if (@($checkResults | Where-Object { $_ -match 'CHECK UNKNOWN' }).Count -ne 0)
{
    throw 'Intentional comparison skips were marked as failures'
}
if ($script:HadFailure)
{
    throw 'Check-only dispatcher failed'
}
$uiResults = @($script:messages | Where-Object { $_ -like 'UI_RESULT *' } | ForEach-Object { $_.Substring(10) | ConvertFrom-Json })
if ($uiResults.Count -ne 10)
{
    throw 'Missing structured results for check-only stages'
}
$cliResult = $uiResults | Where-Object Stage -EQ 'Claude Code'
if ($cliResult.Result -ne 'AVAILABLE' -or $cliResult.Installed -ne '1.0.0' -or $cliResult.Available -ne '2.0.0')
{
    throw 'Structured result lost CLI versions'
}
if (($uiResults | Where-Object Stage -EQ 'Codex plugins').Info -ne 'Плагинов: 2' -or
    ($uiResults | Where-Object Stage -EQ 'Claude plugins').Info -ne 'Плагинов: 1')
{
    throw 'Incorrect plugin counts'
}
$script:messages.Clear()
$script:pluginTestText = 'invalid json'
Invoke-CheckStage 'Codex plugins'
if (-not ($script:messages -contains '=== CHECK UNKNOWN: Codex plugins ==='))
{
    throw 'Invalid plugin output was marked as a direct update'
}
$script:pluginTestText = $null

$script:messages.Clear()
Compare-CheckVersions 'test' ([version]'2.0') ([version]'2.0')
Compare-CheckVersions 'test' ([version]'3.0') ([version]'2.0')
Compare-CheckVersions 'test' ([version]'1.0') $null
if (@($script:messages | Where-Object { $_ -match 'CHECK CURRENT' }).Count -ne 2)
{
    throw 'Current versions misclassified'
}
if (-not ($script:messages -match 'CHECK UNKNOWN'))
{
    throw 'Missing latest version misclassified'
}
function Test-Tool
{
    return $false
}
Invoke-CheckStage 'Claude Code'
Invoke-CheckStage 'Claude Desktop'
if (-not ($script:messages -match 'CHECK NOT_INSTALLED: Claude Code'))
{
    throw 'Missing CLI misclassified'
}
if (-not ($script:messages -match 'CHECK UNKNOWN: Claude Desktop'))
{
    throw 'Missing winget misclassified'
}
Write-Output 'PASS: all ten check-only stages, no modifying commands or restarts, available/current/unknown/missing outcomes'

function Test-Tool
{
    return $true
}
function Invoke-CommandCapture
{
    return [pscustomobject]@{ ExitCode = 0; Text = $script:wingetRow }
}
foreach ($case in @(
    @{ Row = 'Antigravity 2.22.0 Google.Antigravity 2.22.0'; Result = 'CURRENT' },
    @{ Row = 'Claude Anthropic.Claude 1.44121.2 2.19675.1'; Result = 'AVAILABLE' },
    @{ Row = 'Claude Anthropic.Claude 1.44121.2 2.19675.1 winget'; Result = 'AVAILABLE' }
))
{
    $script:messages.Clear()
    $script:wingetRow = $case.Row
    $packageId = if ($case.Row -match 'Google')
    {
        'Google.Antigravity'
    }
    else
    {
        'Anthropic.Claude'
    }
    Test-WingetUpdate 'test' $packageId 'winget'
    if (-not ($script:messages -contains "=== CHECK $( $case.Result ): test ==="))
    {
        throw "winget row misparsed: $( $case.Row )"
    }
}

# Check bounded retry and retain the final failure code for unrelated failures.
$captureFunction = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-CommandCapture' }, $false)[0]
Invoke-Expression $captureFunction.Extent.Text
function Invoke-CommandCaptureCore
{
    param($FilePath, $Arguments, $WorkingDirectory, [switch]$NoLogCommand)
    $script:captureCount++
    return [pscustomobject]@{
        ExitCode = $( if ($script:captureCount -eq 1)
        {
            $script:firstExitCode
        }
        else
        {
            0
        } ); Text = ''
    }
}
$script:captureCount = 0
$script:firstExitCode = -1978335231
$result = Invoke-CommandCapture 'winget' @('list') -NoLogCommand
if ($script:captureCount -ne 2 -or $result.ExitCode -ne 0)
{
    throw 'winget initialization retry failed'
}
$script:captureCount = 0
$script:firstExitCode = 123
$result = Invoke-CommandCapture 'winget' @('list') -NoLogCommand
if ($script:captureCount -ne 1 -or $result.ExitCode -ne 123)
{
    throw 'Unrelated winget failure was hidden'
}
Write-Output 'PASS: winget output with/without source column, initialization retry, failure preservation'

# Lifecycle tests capture commands without installing or removing real software.
function Invoke-LoggedCommand
{
    param($FilePath, $Arguments) $script:commands.Add("$FilePath $( $Arguments -join ' ' )")
}
function Invoke-CommandCapture
{
    param($FilePath, $Arguments)
    $script:commands.Add("$FilePath $( $Arguments -join ' ' )")
    return [pscustomobject]@{ ExitCode = $script:lifecycleExitCode; Text = '' }
}
$script:lifecycleExitCode = 0
$script:commands.Clear()
Invoke-ComponentAction 'Claude Desktop' 'Install'
Invoke-ComponentAction 'Antigravity Desktop' 'Uninstall'
Invoke-ComponentAction 'Codex npm package' 'Install'
Invoke-ComponentAction 'Claude npm package' 'Uninstall'
if ($script:commands.Count -ne 4)
{
    throw 'Unexpected lifecycle commands'
}
if (-not ($script:commands -match 'winget install --id Anthropic.Claude --exact'))
{
    throw 'Missing desktop installation'
}
if (-not ($script:commands -match 'winget uninstall --id Google.Antigravity --exact'))
{
    throw 'Missing desktop removal'
}
if (-not ($script:commands -contains 'npm install -g @openai/codex@latest'))
{
    throw 'Missing npm installation'
}
if (-not ($script:commands -contains 'npm uninstall -g @anthropic-ai/claude-code'))
{
    throw 'Missing npm removal'
}
$script:lifecycleExitCode = 123
$failed = $false
try
{
    Invoke-ComponentAction 'Claude Desktop' 'Uninstall'
}
catch
{
    $failed = $true
}
if (-not $failed)
{
    throw 'Uninstall error incorrectly accepted'
}
$script:lifecycleExitCode = -1978335212
Invoke-ComponentAction 'Claude Desktop' 'Uninstall'
$failed = $false
try
{
    Invoke-ComponentAction 'Codex plugins' 'Uninstall'
}
catch
{
    $failed = $true
}
if (-not $failed)
{
    throw 'Plugin group removal incorrectly accepted'
}
$script:commands.Clear()
$failed = $false
try
{
    Invoke-ComponentAction 'Antigravity CLI' 'Uninstall'
}
catch
{
    $failed = $true
}
if (-not $failed -or $script:commands.Count)
{
    throw 'CLI from an unknown installation was not protected'
}

# Verify the dispatcher selects a single component and never restarts a removed app.
$script:lifecycleExitCode = 0
$script:commands.Clear()
$CheckOnly = $false
$OnlyStage = 'Claude npm package'
$Vendor = 'Anthropic'
$Action = 'Uninstall'
Invoke-Expression $dispatchText
if ($script:commands.Count -ne 1 -or $script:commands[0] -ne 'npm uninstall -g @anthropic-ai/claude-code')
{
    throw 'Removal affected another component'
}

function Get-ClaudeDesktopWingetState
{
    return [pscustomobject]@{ IsInstalled = $false }
}
function Get-CodexDesktopWingetState
{
    return [pscustomobject]@{ IsInstalled = $false }
}
function Ensure-UpdateServicesRunning
{
}
$script:commands.Clear()
Update-ClaudeDesktop
Update-CodexDesktop
if ($script:commands.Count)
{
    throw 'Update installed an absent application'
}
Write-Output 'PASS: separate install/remove commands, uninstall errors, absent packages, plugin guard, selected-component dispatch, updates do not install absent apps'

foreach ($case in @(
    @{ Left = '0.163.0-alpha.5'; Right = '0.163.0-alpha.11'; Expected = -1 },
    @{ Left = '0.163.0-alpha.11'; Right = '0.163.0-alpha.5'; Expected = 1 },
    @{ Left = '0.163.0-alpha.5'; Right = '0.162.1'; Expected = 1 },
    @{ Left = '0.163.0-alpha.5'; Right = '0.163.0'; Expected = -1 },
    @{ Left = '0.163.0'; Right = '0.163.0-alpha.5'; Expected = 1 },
    @{ Left = '0.163.0-alpha.5+build.1'; Right = '0.163.0-alpha.5+build.2'; Expected = 0 },
    @{ Left = '1.0.0-beta.2'; Right = '1.0.0-rc.1'; Expected = -1 },
    @{ Left = '1.0.0-alpha'; Right = '1.0.0-alpha.1'; Expected = -1 },
    @{ Left = '1.0.0-alpha.9.2'; Right = '1.0.0-alpha.10'; Expected = -1 },
    @{ Left = '1.0.0-1'; Right = '1.0.0-alpha'; Expected = -1 }
))
{
    if (([Math]::Sign((Compare-PackageVersions $case.Left $case.Right))) -ne $case.Expected)
    {
        throw "Incorrect version precedence: $( $case.Left ) / $( $case.Right )"
    }
}
if ((ConvertTo-VersionOrNull 'codex-cli 0.163.0-alpha.5') -ne '0.163.0-alpha.5')
{
    throw 'Prerelease suffix was lost'
}
if ((Get-NpmReleaseChannel '0.163.0-alpha.5') -ne 'alpha')
{
    throw 'Wrong alpha channel'
}
if ((Get-NpmReleaseChannel '0.162.1') -ne 'latest')
{
    throw 'Wrong stable channel'
}

$script:installedCli = '0.163.0-alpha.5'
$script:alphaVersion = '0.163.0-alpha.11'
$script:stopCount = 0
$script:installerCount = 0
$script:codexCommandPath = Join-Path $env:LOCALAPPDATA 'Programs\OpenAI\Codex\bin\codex.exe'
function Stop-CodexProcess
{
    $script:stopCount++
}
function Get-Command
{
    param($Name, $ErrorAction)
    return [pscustomobject]@{ Source = $script:codexCommandPath }
}
function Invoke-CommandCapture
{
    param($FilePath, $Arguments, [switch]$NoLogCommand)
    $script:commands.Add("$FilePath $( $Arguments -join ' ' )")
    $text = switch ($Arguments[0])
    {
        '--version' {
            "codex-cli $script:installedCli"
        }
        'prefix' {
            'C:\mock-npm'
        }
        'list' {
            '{"dependencies":{"@openai/codex":{"version":"' + $script:installedCli + '"}}}'
        }
        'view' {
            if ($Arguments[2] -eq 'dist-tags.alpha')
            {
                $script:alphaVersion
            }
            else
            {
                '0.162.1'
            }
        }
        default {
            throw 'Unexpected command'
        }
    }
    return [pscustomobject]@{ ExitCode = 0; Text = $text }
}
function Invoke-LoggedCommand
{
    param($FilePath, $Arguments)
    $script:commands.Add("$FilePath $( $Arguments -join ' ' )")
    $script:installedCli = '0.163.0-alpha.11'
}
function Invoke-WebRequest
{
    param($Uri, $OutFile, [switch]$UseBasicParsing)
    if ($Uri -ne 'https://chatgpt.com/codex/install.ps1')
    {
        throw 'Unexpected installer URL'
    }
    $script:installerCount++
    Set-Content -LiteralPath $OutFile -Value '# Mock installer, never executed'
}
$script:messages.Clear()
$script:commands.Clear()
Invoke-CheckStage 'Active Codex CLI'
if (-not ($script:messages -contains '=== CHECK AVAILABLE: Active Codex CLI ==='))
{
    throw 'Alpha update not detected'
}
if (-not ($script:commands -contains 'npm view @openai/codex dist-tags.alpha --silent'))
{
    throw 'Alpha channel was not queried'
}
if ($script:stopCount -or $script:installerCount)
{
    throw 'Prerelease check changed software'
}

$previousNonInteractive = $env:CODEX_NON_INTERACTIVE
Update-ActiveCodexCli
if ($script:installerCount -ne 1 -or $script:stopCount -ne 1)
{
    throw 'Standalone prerelease installer was not used'
}
if (-not ($script:commands -match '-Release 0.163.0-alpha.11$'))
{
    throw 'Standalone update did not pin the alpha version'
}
if ($env:CODEX_NON_INTERACTIVE -ne $previousNonInteractive)
{
    throw 'Installer environment was not restored'
}
if ($script:commands -contains 'codex update')
{
    throw 'Prerelease invoked stable updater'
}

$script:commands.Clear()
$script:codexCommandPath = 'C:\mock-npm\codex.cmd'
Update-CodexPrerelease '0.163.0-alpha.11'
if (-not ($script:commands -contains 'npm install -g @openai/codex@0.163.0-alpha.11'))
{
    throw 'Active npm CLI update did not pin its release'
}
$script:codexCommandPath = 'C:\unknown-installer\codex.exe'
$previousStops = $script:stopCount
$failed = $false
try
{
    Update-CodexPrerelease '0.163.0-alpha.11'
}
catch
{
    $failed = $true
}
if (-not $failed -or $script:stopCount -ne $previousStops)
{
    throw 'Unknown installation was not protected before shutdown'
}
$script:codexCommandPath = Join-Path $env:LOCALAPPDATA 'Programs\OpenAI\Codex\bin\codex.exe'

$script:installedCli = '0.163.0-alpha.5'
$script:commands.Clear()
Update-NpmGlobalPackage '@openai/codex' 'Codex CLI'
if (-not ($script:commands -contains 'npm install -g @openai/codex@0.163.0-alpha.11'))
{
    throw 'npm prerelease update did not preserve channel'
}

$script:installedCli = '0.163.0-alpha.5'
$script:alphaVersion = ''
$script:messages.Clear()
$script:commands.Clear()
Invoke-CheckStage 'Active Codex CLI'
if (-not ($script:messages -contains '=== CHECK ERROR: Active Codex CLI ==='))
{
    throw 'Missing alpha tag was not reported as a check error'
}
$failed = $false
try
{
    Update-ActiveCodexCli
}
catch
{
    $failed = $true
}
if (-not $failed -or ($script:commands -contains 'codex update'))
{
    throw 'Missing alpha tag allowed a stable update'
}
$script:alphaVersion = '0.162.1'
if ($null -ne (Get-NpmLatestVersion '@openai/codex' '0.163.0-alpha.5'))
{
    throw 'Wrong-channel tag was accepted'
}

$script:installedCli = '0.163.0-alpha.11'
$script:alphaVersion = '0.163.0-alpha.11'
$script:commands.Clear()
Update-ActiveCodexCli
if ($script:installerCount -ne 1)
{
    throw 'Current alpha version was reinstalled'
}
$script:installedCli = '0.162.1'
$script:commands.Clear()
Invoke-CheckStage 'Active Codex CLI'
if (-not ($script:commands -contains 'npm view @openai/codex version --silent'))
{
    throw 'Stable lookup changed channel'
}
Write-Output 'PASS: prerelease parsing and precedence, alpha/stable lookups, read-only alpha checks, pinned native/npm updates, missing/wrong channels, current alpha skip'

$script:messages.Clear()
$script:postActionUpdated = $false
function Update-ClaudeCode
{
    $script:postActionUpdated = $true
}
function Start-CodexProcess
{
}
function Start-ClaudeDesktopProcess
{
}
function Get-ToolVersion
{
    param([string]$ToolName)
    if (-not $script:postActionUpdated)
    {
        throw 'Post-action check ran before the update'
    }
    return '2.2.1'
}
function Get-NpmLatestVersion
{
    return '2.2.1'
}
$CheckOnly = $false
$OnlyStage = 'Claude Code'
$Vendor = 'Anthropic'
$Action = 'Update'
Invoke-Expression $dispatchText
$postAction = @($script:messages | Where-Object { $_ -like 'UI_RESULT *' } | ForEach-Object { $_.Substring(10) | ConvertFrom-Json })
if ($postAction.Count -ne 1 -or $postAction[0].Stage -ne 'Claude Code' -or $postAction[0].Installed -ne '2.2.1')
{
    throw 'Dispatcher did not publish the post-update version'
}
Write-Output 'PASS: dispatcher publishes a structured version result after updating the selected component'
