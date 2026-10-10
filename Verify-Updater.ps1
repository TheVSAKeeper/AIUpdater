$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Update-Codex.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
# Load function definitions without running installers, elevation or application shutdown.
foreach ($function in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    Invoke-Expression $function.Extent.Text
}
$script:messages = [System.Collections.Generic.List[string]]::new()
$script:commands = [System.Collections.Generic.List[string]]::new()
function Write-Log { param($Message) $script:messages.Add($Message) }
function Test-Tool { param($Name) return $true }
function Invoke-LoggedCommand { param($FilePath, $Arguments, $WorkingDirectory) $script:commands.Add("$FilePath $($Arguments -join ' ')") }
function Invoke-CommandCapture {
    param($FilePath, $Arguments, $WorkingDirectory, [switch]$NoLogCommand)
    if ($FilePath -eq 'claude') {
        return [pscustomobject]@{ ExitCode = 0; Text = '[{"id":"missing@market","scope":"project","projectPath":"C:\\AIUpdater-nonexistent-test-project"},{"id":"valid@market","scope":"user","version":"1"}]' }
    }
    return [pscustomobject]@{ ExitCode = $script:wingetExitCode; Text = 'mock winget result' }
}
$script:HadFailure = $false
$script:FailureCount = 0
Update-ClaudePlugins
if ($script:HadFailure) { throw 'Missing project directory incorrectly fails the update' }
if ($script:commands -match 'missing@market') { throw 'Attempted update in missing project' }
if (-not ($script:commands -match 'valid@market')) { throw 'Valid user plugin was not updated' }
if (-not ($script:messages -match 'project directory no longer exists')) { throw 'Missing project was not reported' }

$script:wingetExitCode = 0
Update-AntigravityDesktop
$script:wingetExitCode = -1978335189
# Mock list succeeds, while upgrade reports no available update.
function Invoke-CommandCapture {
    param($FilePath, $Arguments)
    return [pscustomobject]@{ ExitCode = $(if ($Arguments[0] -eq 'list') { 0 } else { $script:wingetExitCode }); Text = 'mock' }
}
Update-AntigravityDesktop
if (-not ($script:messages -match 'already up to date')) { throw 'No-update result was not accepted' }
$script:wingetExitCode = 123
$failed = $false
try { Update-AntigravityDesktop } catch { $failed = $true }
if (-not $failed) { throw 'Installer error incorrectly accepted' }

Invoke-UpdateStep -Name 'Antigravity CLI' -ScriptBlock { throw 'simulated failure' }
Invoke-UpdateStep -Name 'Claude Code' -ScriptBlock { }
if (-not ($script:messages -contains '=== FAILED: Antigravity CLI ===')) { throw 'Missing failed stage result' }
if (-not ($script:messages -contains '=== COMPLETED: Claude Code ===')) { throw 'A previous error leaked into the next stage' }
Write-Output 'PASS: missing project skip, valid plugin update, winget outcomes, independent stage results'

. (Join-Path $PSScriptRoot 'Check-Updates.ps1')
$script:commands.Clear()
$script:messages.Clear()
function Invoke-LoggedCommand { throw 'Check-only mode attempted a modifying command' }
function Stop-CodexProcess { throw 'Check-only mode stopped Codex' }
function Stop-ClaudeDesktopProcess { throw 'Check-only mode stopped Claude' }
function Start-CodexProcess { throw 'Check-only mode restarted Codex' }
function Write-AppxEvents { }
function Start-ClaudeDesktopProcess { throw 'Check-only mode restarted Claude' }
function Get-CodexVersion { return [version]'1.0.0' }
function Get-CodexPackagedStoreUpdateState {
    return [pscustomobject]@{ Success = $true; Updates = @([pscustomobject]@{ Name = 'OpenAI.Codex'; Version = '2.0.0' }) }
}
$script:testScriptPath = $PSCommandPath
function Get-Command { param($Name, $ErrorAction) return [pscustomobject]@{ Source = $script:testScriptPath } }
function Invoke-CommandCapture {
    param($FilePath, $Arguments, $WorkingDirectory, [switch]$NoLogCommand)
    $script:commands.Add("$FilePath $($Arguments -join ' ')")
    if ($Arguments[0] -eq '--version') { return [pscustomobject]@{ ExitCode = 0; Text = '1.0.0' } }
    if ($FilePath -eq 'npm' -and $Arguments[0] -eq 'view') {
        return [pscustomobject]@{ ExitCode = 0; Text = '2.0.0' }
    }
    if ($FilePath -eq 'npm' -and $Arguments[0] -eq 'list') {
        return [pscustomobject]@{ ExitCode = 0; Text = ('{"dependencies":{"' + $Arguments[2] + '":{"version":"1.0.0"}}}') }
    }
    if ($FilePath -eq 'winget' -and $Arguments[0] -eq 'list') {
        $packageId = $Arguments[2]
        return [pscustomobject]@{ ExitCode = 0; Text = "Application $packageId 1.0.0 2.0.0 winget" }
    }
    if ($Arguments[0] -eq 'plugin' -and $Arguments[1] -eq 'list') {
        return [pscustomobject]@{ ExitCode = 0; Text = '[]' }
    }
    throw "Check-only mode attempted an unexpected command: $FilePath $($Arguments -join ' ')"
}

# Exercise the actual dispatcher, including the branch that normally starts applications.
$CheckOnly = $true
$OnlyStage = $null
$Vendor = $null
$dispatch = $ast.EndBlock.Statements | Where-Object {
    $_ -is [System.Management.Automation.Language.TryStatementAst]
} | Select-Object -Last 1
$checkScriptPath = Join-Path $PSScriptRoot 'Check-Updates.ps1'
$dispatchText = $dispatch.Extent.Text.Replace(". (Join-Path `$PSScriptRoot 'Check-Updates.ps1')", '. $checkScriptPath').Replace('exit 1', 'throw ($script:messages -join "`n")')
Invoke-Expression $dispatchText
$checkResults = @($script:messages | Where-Object { $_ -match '^=== CHECK ' })
if ($checkResults.Count -ne 10) { throw "Expected ten check results; received $($checkResults.Count)" }
if (@($checkResults | Where-Object { $_ -match 'CHECK AVAILABLE' }).Count -ne 7) { throw 'Available versions were not detected' }
if (@($checkResults | Where-Object { $_ -match 'CHECK UNKNOWN' }).Count -ne 3) { throw 'Unsupported comparisons were incorrectly marked current' }
if ($script:HadFailure) { throw 'Check-only dispatcher failed' }

$script:messages.Clear()
Compare-CheckVersions 'test' ([version]'2.0') ([version]'2.0')
Compare-CheckVersions 'test' ([version]'3.0') ([version]'2.0')
Compare-CheckVersions 'test' ([version]'1.0') $null
if (@($script:messages | Where-Object { $_ -match 'CHECK CURRENT' }).Count -ne 2) { throw 'Current versions misclassified' }
if (-not ($script:messages -match 'CHECK UNKNOWN')) { throw 'Missing latest version misclassified' }
function Test-Tool { return $false }
Invoke-CheckStage 'Claude Code'
Invoke-CheckStage 'Claude Desktop'
if (-not ($script:messages -match 'CHECK NOT_INSTALLED: Claude Code')) { throw 'Missing CLI misclassified' }
if (-not ($script:messages -match 'CHECK UNKNOWN: Claude Desktop')) { throw 'Missing winget misclassified' }
Write-Output 'PASS: all ten check-only stages, no modifying commands or restarts, available/current/unknown/missing outcomes'

function Test-Tool { return $true }
function Invoke-CommandCapture { return [pscustomobject]@{ ExitCode = 0; Text = $script:wingetRow } }
foreach ($case in @(
    @{ Row = 'Antigravity 2.22.0 Google.Antigravity 2.22.0'; Result = 'CURRENT' },
    @{ Row = 'Claude Anthropic.Claude 1.44121.2 2.19675.1'; Result = 'AVAILABLE' },
    @{ Row = 'Claude Anthropic.Claude 1.44121.2 2.19675.1 winget'; Result = 'AVAILABLE' }
)) {
    $script:messages.Clear()
    $script:wingetRow = $case.Row
    $packageId = if ($case.Row -match 'Google') { 'Google.Antigravity' } else { 'Anthropic.Claude' }
    Test-WingetUpdate 'test' $packageId 'winget'
    if (-not ($script:messages -contains "=== CHECK $($case.Result): test ===")) { throw "winget row misparsed: $($case.Row)" }
}

# Check bounded retry and retain the final failure code for unrelated failures.
$captureFunction = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-CommandCapture' }, $false)[0]
Invoke-Expression $captureFunction.Extent.Text
function Invoke-CommandCaptureCore {
    param($FilePath, $Arguments, $WorkingDirectory, [switch]$NoLogCommand)
    $script:captureCount++
    return [pscustomobject]@{ ExitCode = $(if ($script:captureCount -eq 1) { $script:firstExitCode } else { 0 }); Text = '' }
}
$script:captureCount = 0
$script:firstExitCode = -1978335231
$result = Invoke-CommandCapture 'winget' @('list') -NoLogCommand
if ($script:captureCount -ne 2 -or $result.ExitCode -ne 0) { throw 'winget initialization retry failed' }
$script:captureCount = 0
$script:firstExitCode = 123
$result = Invoke-CommandCapture 'winget' @('list') -NoLogCommand
if ($script:captureCount -ne 1 -or $result.ExitCode -ne 123) { throw 'Unrelated winget failure was hidden' }
Write-Output 'PASS: winget output with/without source column, initialization retry, failure preservation'
