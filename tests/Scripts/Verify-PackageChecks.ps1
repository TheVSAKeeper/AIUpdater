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
$script:agyManifestContent = '{"version":"1.3.6"}'
$script:agyManifestError = $null
$script:agyManifestRequests = 0
$script:agyManifestUri = $null
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
function Invoke-WebRequest
{
    param($Uri, [switch]$UseBasicParsing, $TimeoutSec, $ErrorAction)
    $script:agyManifestRequests++
    if ($Uri -ne $script:agyManifestUri -or -not $UseBasicParsing -or $TimeoutSec -ne 20 -or $ErrorAction -ne 'Stop')
    {
        throw "Unexpected agy manifest request: $Uri"
    }
    if ($script:agyManifestError)
    {
        throw $script:agyManifestError
    }
    return [pscustomobject]@{ Content = $script:agyManifestContent }
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

$oldProcessorArchitecture = $env:PROCESSOR_ARCHITECTURE
$oldProcessorArchitew6432 = $env:PROCESSOR_ARCHITEW6432
try
{
    $env:PROCESSOR_ARCHITECTURE = 'AMD64'
    Remove-Item Env:PROCESSOR_ARCHITEW6432 -ErrorAction SilentlyContinue
    $script:agyManifestUri = 'https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/windows_amd64.json'
    $script:agyManifestContent = '{"version":"1.3.6"}'
    foreach ($case in @(
        @{ Exists = $false; Code = 0; Text = ''; Expected = 'NOT_INSTALLED'; Requests = 0 },
        @{ Exists = $true; Code = 0; Text = '1.3.6'; Expected = 'CURRENT'; Requests = 1 },
        @{ Exists = $true; Code = 7; Text = 'Access denied'; Expected = 'ERROR'; Requests = 0 },
        @{ Exists = $true; Code = 0; Text = 'invalid'; Expected = 'ERROR'; Requests = 0 }
    ))
    {
        $script:messages.Clear()
        $script:agyExists = $case.Exists
        $script:agyResult = [pscustomobject]@{ ExitCode = $case.Code; Text = $case.Text }
        $script:agyManifestRequests = 0
        Invoke-CheckStage 'Antigravity CLI'
        $result = Read-Result
        if ($result.Result -ne $case.Expected -or $script:agyManifestRequests -ne $case.Requests)
        {
            throw "agy process: expected $( $case.Expected ) with $( $case.Requests ) manifest requests, received $( $result.Result ) with $( $script:agyManifestRequests )"
        }
        if ($case.Expected -eq 'CURRENT' -and ($result.Installed -ne '1.3.6' -or $result.Available -ne '1.3.6'))
        {
            throw 'agy equal versions were not preserved in the result'
        }
    }

    $script:agyExists = $true
    $script:agyResult = [pscustomobject]@{ ExitCode = 0; Text = '1.3.5' }
    foreach ($case in @(
        @{ Version = '1.3.7'; Expected = 'AVAILABLE' },
        @{ Version = '1.3.5'; Expected = 'CURRENT' },
        @{ Version = '1.3.4'; Expected = 'CURRENT' },
        @{ Version = '1.4.0-rc.1'; Expected = 'AVAILABLE' }
    ))
    {
        $script:agyManifestContent = '{"version":"' + $case.Version + '"}'
        $script:agyManifestError = $null
        $script:agyManifestRequests = 0
        $script:messages.Clear()
        Invoke-CheckStage 'Antigravity CLI'
        $result = Read-Result
        if ($result.Result -ne $case.Expected -or $result.Installed -ne '1.3.5' -or $result.Available -ne $case.Version -or $script:agyManifestRequests -ne 1)
        {
            throw "agy version comparison failed for manifest $($case.Version)"
        }
    }

    foreach ($content in @(
        '{bad json',
        '{}',
        '{"version":null}',
        '{"version":7}',
        '{"version":"invalid"}',
        '{"version":"release 1.3.6"}',
        '{"version":"1.2-beta+"}',
        '{"version":"1.3.6\n"}',
        '{"version":"999999999999999999999999999999999.0"}',
        '[]',
        '[{"version":"1.3.6"}]',
        'null',
        '"1.3.6"',
        '42'
    ))
    {
        $script:agyManifestContent = $content
        $script:agyManifestError = $null
        $script:agyManifestRequests = 0
        $script:messages.Clear()
        Invoke-CheckStage 'Antigravity CLI'
        $result = Read-Result
        if ($result.Result -ne 'ERROR' -or $result.Installed -ne '1.3.5' -or $script:agyManifestRequests -ne 1)
        {
            throw "Invalid agy manifest did not preserve installed version: $content"
        }
    }

    foreach ($message in @('network failure', 'HTTP 503', 'request timed out'))
    {
        $script:agyManifestContent = '{"version":"1.3.6"}'
        $script:agyManifestError = $message
        $script:agyManifestRequests = 0
        $script:messages.Clear()
        Invoke-CheckStage 'Antigravity CLI'
        $result = Read-Result
        if ($result.Result -ne 'ERROR' -or $result.Installed -ne '1.3.5' -or $script:agyManifestRequests -ne 1)
        {
            throw "agy $message did not preserve installed version"
        }
    }

    $script:agyManifestError = $null
    $script:agyManifestContent = '{"version":"1.3.6"}'
    $script:agyResult = [pscustomobject]@{ ExitCode = 0; Text = '1.3.6' }
    foreach ($archCase in @(
        @{ Process = 'AMD64'; Wow = $null; Uri = 'https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/windows_amd64.json' },
        @{ Process = 'ARM64'; Wow = $null; Uri = 'https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/windows_arm64.json' },
        @{ Process = 'AMD64'; Wow = 'ARM64'; Uri = 'https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/windows_arm64.json' },
        @{ Process = 'x86'; Wow = 'AMD64'; Uri = 'https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/windows_amd64.json' }
    ))
    {
        $env:PROCESSOR_ARCHITECTURE = $archCase.Process
        if ($null -eq $archCase.Wow)
        {
            Remove-Item Env:PROCESSOR_ARCHITEW6432 -ErrorAction SilentlyContinue
        }
        else
        {
            $env:PROCESSOR_ARCHITEW6432 = $archCase.Wow
        }
        $script:agyManifestUri = $archCase.Uri
        $script:agyManifestRequests = 0
        $script:messages.Clear()
        Invoke-CheckStage 'Antigravity CLI'
        if ((Read-Result).Result -ne 'CURRENT' -or $script:agyManifestRequests -ne 1)
        {
            throw "agy architecture did not select expected manifest $($archCase.Uri)"
        }
    }

    $env:PROCESSOR_ARCHITECTURE = 'x86'
    Remove-Item Env:PROCESSOR_ARCHITEW6432 -ErrorAction SilentlyContinue
    $script:agyManifestRequests = 0
    $script:messages.Clear()
    Invoke-CheckStage 'Antigravity CLI'
    if ((Read-Result).Result -ne 'ERROR' -or $script:agyManifestRequests -ne 0)
    {
        throw 'Unsupported agy architecture made a manifest request'
    }

    $script:agyManifestUri = 'https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/windows_amd64.json'
    $env:PROCESSOR_ARCHITECTURE = 'AMD64'
    $script:throwCapture = $true
    $script:messages.Clear()
    $script:agyManifestRequests = 0
    Invoke-CheckStage 'Antigravity CLI'
    $result = Read-Result
    if ($result.Result -ne 'ERROR' -or $script:agyManifestRequests -ne 0)
    {
        throw 'Failed agy launch became absence or queried the manifest'
    }
    $script:throwCapture = $false
}
finally
{
    if ($null -eq $oldProcessorArchitecture) { Remove-Item Env:PROCESSOR_ARCHITECTURE -ErrorAction SilentlyContinue } else { $env:PROCESSOR_ARCHITECTURE = $oldProcessorArchitecture }
    if ($null -eq $oldProcessorArchitew6432) { Remove-Item Env:PROCESSOR_ARCHITEW6432 -ErrorAction SilentlyContinue } else { $env:PROCESSOR_ARCHITEW6432 = $oldProcessorArchitew6432 }
}
Write-Output 'PASS: agy absence and process failures, manifest comparison, malformed manifests, request failures, AMD64/ARM64/WOW architecture; updater never invoked'
