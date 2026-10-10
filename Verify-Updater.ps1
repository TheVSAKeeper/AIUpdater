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
