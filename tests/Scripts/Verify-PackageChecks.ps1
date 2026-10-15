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
foreach ($function in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false))
{
    Invoke-Expression $function.Extent.Text
}
. (Join-Path $scriptsDirectory 'Check-Updates.ps1')
$script:messages = [System.Collections.Generic.List[string]]::new()
$script:hasNpm = $true
$script:agyExists = $true
$script:latest = '2.0.0'
$script:lookupCount = 0
$script:throwCapture = $false
function Write-Log
{
    param($Message) $script:messages.Add($Message)
}
function Test-Tool
{
    param($Name) return $script:hasNpm
}
function Invoke-LoggedCommand
{
    throw 'Read-only check tried to modify software'
}
function Invoke-CommandCapture
{
    param($FilePath, $Arguments, [switch]$NoLogCommand)
    if ($script:throwCapture)
    {
        throw 'Simulated process start failure'
    }
    if ($FilePath -eq 'npm')
    {
        if (($Arguments -join ' ') -ne 'list -g --depth=0 --json --silent')
        {
            throw 'Expected complete global npm inventory'
        }
        return $script:npmResult
    }
    if (($Arguments -join ' ') -ne '--version')
    {
        throw 'agy check tried to run its updater'
    }
    return $script:agyResult
}
function Get-NpmLatestVersion
{
    $script:lookupCount++
    if ($script:throwLookup)
    {
        throw 'Simulated registry failure'
    }
    return $script:latest
}
function Get-Command
{
    return [pscustomobject]@{ Source = 'C:\mock-agy.exe' }
}
function Test-Path
{
    param($LiteralPath) return $script:agyExists
}
function Read-Result
{
    $results = @($script:messages | Where-Object { $_ -like 'UI_RESULT *' } | ForEach-Object { $_.Substring(10) | ConvertFrom-Json })
    if ($results.Count -ne 1)
    {
        throw "Expected one result, received $( $results.Count )"
    }
    return $results[0]
}

foreach ($package in @('@openai/codex', '@anthropic-ai/claude-code'))
{
    foreach ($case in @(
        @{ Code = 0; Text = '{"name":"npm"}'; Expected = 'NOT_INSTALLED' },
        @{ Code = 0; Text = '{"dependencies":{}}'; Expected = 'NOT_INSTALLED' },
        @{ Code = 0; Text = '{"dependencies":{"other":{"version":"1.0.0"}}}'; Expected = 'NOT_INSTALLED' },
        @{ Code = 1; Text = '{"name":"npm"}'; Expected = 'ERROR' },
        @{ Code = 42; Text = 'Access denied'; Expected = 'ERROR' },
        @{ Code = 0; Text = '{bad json'; Expected = 'ERROR' },
        @{ Code = 0; Text = ''; Expected = 'ERROR' },
        @{ Code = 0; Text = '[]'; Expected = 'ERROR' },
        @{ Code = 0; Text = '{}'; Expected = 'ERROR' },
        @{ Code = 0; Text = '{"error":{"code":"EACCES"}}'; Expected = 'ERROR' },
        @{ Code = 0; Text = '{"dependencies":[]}'; Expected = 'ERROR' },
        @{ Code = 0; Text = ('{"dependencies":{"' + $package + '":{}}}'); Expected = 'ERROR' },
        @{ Code = 0; Text = ('{"dependencies":{"' + $package + '":{"version":"invalid"}}}'); Expected = 'ERROR' },
        @{ Code = 0; Text = ('{"dependencies":{"' + $package + '":{"version":"1.0.0"}}}'); Expected = 'AVAILABLE' }
    ))
    {
        $script:messages.Clear()
        $script:lookupCount = 0
        $script:npmResult = [pscustomobject]@{ ExitCode = $case.Code; Text = $case.Text }
        Test-CliUpdate 'test npm' 'unused' $package -NpmPackage
        $result = Read-Result
        if ($result.Result -ne $case.Expected)
        {
            throw "${package}: expected $( $case.Expected ), received $( $result.Result ), output=$( $case.Text )"
        }
        if ($case.Expected -ne 'AVAILABLE' -and $script:lookupCount)
        {
            throw 'Absent or unreadable package triggered a registry lookup'
        }
        if ($case.Expected -eq 'ERROR' -and -not $result.Info)
        {
            throw 'Check error has no explanation'
        }
    }
}
$script:messages.Clear()
$script:hasNpm = $false
Test-CliUpdate 'test npm' 'unused' '@openai/codex' -NpmPackage
if ((Read-Result).Result -ne 'ERROR')
{
    throw 'Missing npm was reported as an absent package'
}
$script:hasNpm = $true
$script:npmResult = [pscustomobject]@{ ExitCode = 0; Text = '{"dependencies":{"@openai/codex":{"version":"1.0.0"}}}' }
$script:messages.Clear()
$script:latest = $null
Test-CliUpdate 'test npm' 'unused' '@openai/codex' -NpmPackage
$result = Read-Result
if ($result.Result -ne 'ERROR' -or $result.Installed -ne '1.0.0')
{
    throw 'Registry lookup failure lost installed version or became absence'
}
$script:latest = '2.0.0'
$script:throwLookup = $true
$script:messages.Clear()
Test-CliUpdate 'test npm' 'unused' '@openai/codex' -NpmPackage
$result = Read-Result
if ($result.Result -ne 'ERROR' -or $result.Installed -ne '1.0.0')
{
    throw 'Thrown registry failure lost installed version'
}
$script:throwLookup = $false
$script:throwCapture = $true
$script:messages.Clear()
Test-CliUpdate 'test npm' 'unused' '@openai/codex' -NpmPackage
if ((Read-Result).Result -ne 'ERROR')
{
    throw 'Failed npm launch became absence'
}
$script:throwCapture = $false
Write-Output 'PASS: both npm packages, absence from complete inventory, exit/JSON/version failures, missing npm, failed launch, registry failure preserving installed version'

foreach ($case in @(
    @{ Exists = $false; Code = 0; Text = ''; Expected = 'NOT_INSTALLED' },
    @{ Exists = $true; Code = 0; Text = '1.3.5'; Expected = 'DIRECT' },
    @{ Exists = $true; Code = 7; Text = 'Access denied'; Expected = 'ERROR' },
    @{ Exists = $true; Code = 0; Text = 'invalid'; Expected = 'ERROR' }
))
{
    $script:messages.Clear()
    $script:agyExists = $case.Exists
    $script:agyResult = [pscustomobject]@{ ExitCode = $case.Code; Text = $case.Text }
    Invoke-CheckStage 'Antigravity CLI'
    $result = Read-Result
    if ($result.Result -ne $case.Expected)
    {
        throw "agy: expected $( $case.Expected ), received $( $result.Result )"
    }
    if ($case.Expected -eq 'DIRECT' -and $result.Installed -ne '1.3.5')
    {
        throw 'agy lost installed version'
    }
}
$script:agyExists = $true
$script:throwCapture = $true
$script:messages.Clear()
Invoke-CheckStage 'Antigravity CLI'
if ((Read-Result).Result -ne 'ERROR')
{
    throw 'Failed agy launch became absence'
}
Write-Output 'PASS: agy absent, installed without comparison, nonzero exit, invalid version, failed launch; updater never invoked'
