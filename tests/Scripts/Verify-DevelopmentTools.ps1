$ErrorActionPreference = 'Stop'
$scriptsDirectory = Join-Path $PSScriptRoot '..\..\src\AIUpdater\Scripts'
. (Join-Path $scriptsDirectory 'Manage-Components.ps1')
. (Join-Path $scriptsDirectory 'Check-Updates.ps1')
$ownerDefinition = (Get-Item Function:\Get-DevelopmentToolOwner).ScriptBlock
function Get-DevelopmentToolOwner { param($Stage) return 'winget' }
function Get-DevelopmentSdkVersions { return @('10.0.303') }
function Get-ToolVersion { param($Tool) return '22.15.0' }
$script:messages = [System.Collections.Generic.List[string]]::new()
$script:commands = [System.Collections.Generic.List[object]]::new()
function Write-Log { param($Message) $script:messages.Add($Message) }
function Test-Tool { param($Name) return $script:hasWinget }
function Invoke-CommandCapture
{
    param($FilePath, $Arguments, [switch]$NoLogCommand)
    if ($FilePath -ne 'winget') { throw "Unexpected tool: $FilePath" }
    $script:commands.Add(@($Arguments))
    if ($Arguments[0] -eq 'list' -and $Arguments[2] -eq 'Microsoft.WindowsTerminal')
    {
        return [pscustomobject]@{ ExitCode = $script:terminalCatalogCode; Text = $script:terminalCatalogResponse }
    }
    return [pscustomobject]@{
        ExitCode = $(if ($Arguments[0] -eq 'list') { $script:listCode } else { $script:upgradeCode })
        Text = $script:response
    }
}
function Read-Result
{
    $rows = @($script:messages | Where-Object { $_ -like 'UI_RESULT *' })
    if ($rows.Count -ne 1) { throw "Expected one result, got $( $rows.Count )" }
    return $rows[0].Substring(10) | ConvertFrom-Json
}
$script:hasWinget = $true
$script:upgradeCode = 0
$script:terminalCatalogCode = 0
$script:terminalCatalogResponse = 'Windows Terminal Microsoft.WindowsTerminal 1.0.0'
foreach ($entry in (Get-DevelopmentPackages).GetEnumerator())
{
    if ($entry.Key -eq '.NET SDK 10') { continue }
    $id = $entry.Value[0]
    if ($entry.Key -eq 'Node.js LTS') { $id = 'OpenJS.NodeJS.22' }
    $source = $entry.Value[1]
    foreach ($case in @(
        @{ Code = 0; Text = "Application $id 1.0.0 2.0.0 $source"; Expected = 'AVAILABLE' },
        @{ Code = 0; Text = "Application $id 1.0.0"; Expected = 'CURRENT' },
        @{ Code = -1978335212; Text = ''; Expected = 'NOT_INSTALLED' },
        @{ Code = 123; Text = 'source failed'; Expected = 'ERROR' },
        @{ Code = 0; Text = "Application $id Unknown"; Expected = 'ERROR' }
    ))
    {
        $script:messages.Clear()
        $script:commands.Clear()
        $script:listCode = $case.Code
        $script:response = $case.Text
        Invoke-CheckStage $entry.Key
        $result = Read-Result
        if ($result.Result -ne $case.Expected) { throw "$( $entry.Key ): expected $( $case.Expected ), got $( $result.Result )" }
        $expectedCommands = if ($entry.Key -eq 'Windows Terminal' -and $case.Expected -eq 'CURRENT') { 2 } else { 1 }
        if ($script:commands.Count -ne $expectedCommands -or @($script:commands | Where-Object { $_[0] -ne 'list' }).Count) { throw 'Check attempted a mutation or used unexpected queries' }
        if ($result.Result -eq 'ERROR' -and -not $result.Info) { throw 'Error has no explanation' }
    }
    $script:listCode = 0
    foreach ($code in @(0, -1978335189, 123))
    {
        $script:commands.Clear()
        $script:upgradeCode = $code
        $failed = $false
        try { Update-DevelopmentTool $entry.Key } catch { $failed = $true }
        if ($failed -ne ($code -eq 123)) { throw "Upgrade exit code $code handled incorrectly" }
        $expected = "upgrade --id $id -e --source $source --accept-source-agreements --accept-package-agreements --disable-interactivity"
        if ($script:commands.Count -ne 2 -or ($script:commands[1] -join ' ') -ne $expected) { throw 'Wrong upgrade target or source' }
    }
    $script:commands.Clear()
    $script:listCode = -1978335212
    Update-DevelopmentTool $entry.Key
    if ($script:commands.Count -ne 1) { throw 'Absent application triggered an upgrade' }
}
$script:listCode = 0
$script:response = 'Терминал Windows 9N0DX20HK701 1.25.2733.0'
foreach ($case in @(
    @{ Code = 0; Text = 'Терминал Windows Microsoft.WindowsTerminal 1.25.2733.0 1.25.3131.0'; Expected = 'AVAILABLE'; Available = '1.25.3131.0' },
    @{ Code = 0; Text = 'Windows Terminal Microsoft.WindowsTerminal 1.25.2733.0'; Expected = 'CURRENT'; Available = '1.25.2733.0' },
    @{ Code = 0; Text = 'Windows Terminal Microsoft.WindowsTerminal 1.25.2733.0 1.24.0.0'; Expected = 'CURRENT'; Available = '1.24.0.0' },
    @{ Code = 123; Text = 'source failed'; Expected = 'ERROR'; Available = $null },
    @{ Code = -1978335212; Text = ''; Expected = 'ERROR'; Available = $null },
    @{ Code = 0; Text = 'Windows Terminal Microsoft.WindowsTerminal Unknown'; Expected = 'ERROR'; Available = $null },
    @{ Code = 0; Text = 'Windows Terminal Microsoft.WindowsTerminal 1.24.0.0 1.25.3131.0'; Expected = 'ERROR'; Available = $null }
))
{
    $script:messages.Clear()
    $script:commands.Clear()
    $script:terminalCatalogCode = $case.Code
    $script:terminalCatalogResponse = $case.Text
    Invoke-CheckStage 'Windows Terminal'
    $result = Read-Result
    if ($result.Result -ne $case.Expected -or $result.Installed -ne '1.25.2733.0' -or $result.Available -ne $case.Available -or -not $result.Info)
    {
        throw 'Terminal catalog comparison lost versions, status or source information'
    }
    $expected = @(
        'list --id 9N0DX20HK701 --exact --source msstore --accept-source-agreements --disable-interactivity',
        'list --id Microsoft.WindowsTerminal --exact --source winget --accept-source-agreements --disable-interactivity'
    )
    if ($script:commands.Count -ne 2 -or ($script:commands[0] -join ' ') -ne $expected[0] -or ($script:commands[1] -join ' ') -ne $expected[1])
    {
        throw 'Terminal comparison used an unexpected command or source'
    }
}
$script:messages.Clear()
$script:commands.Clear()
Test-WingetUpdate 'Other Store app' '9N0DX20HK701' 'msstore'
if ((Read-Result).Result -ne 'UNKNOWN' -or $script:commands.Count -ne 1) { throw 'Terminal fallback changed another Store package check' }
Write-Host 'PASS: Terminal catalog fallback, versions, source failures, identity mismatch and Store upgrade source'
$script:hasWinget = $false
$script:messages.Clear()
$script:commands.Clear()
Invoke-CheckStage 'Windows Terminal'
if ((Read-Result).Result -ne 'ERROR' -or $script:commands.Count) { throw 'Missing winget was not reported as an error' }
Write-Host 'PASS: development tool checks and upgrades, exact IDs and sources, failures and absence'

$script:hasWinget = $true
$script:upgradeCode = 0
$script:commands.Clear()
Invoke-ComponentAction 'Node.js LTS' 'Uninstall'
if ($script:commands.Count -ne 1 -or $script:commands[0][0] -ne 'uninstall' -or $script:commands[0][2] -ne 'OpenJS.NodeJS.22')
{
    throw 'Standalone Node uninstall selected a different release line'
}
function Get-DevelopmentToolOwner { param($Stage) return 'nvm' }
$script:nodeVersion = '22.15.0'
function Get-ToolVersion { param($Tool) return $script:nodeVersion }
$script:nodeIndex = '[{"version":"v26.1.0"},{"version":"v22.23.4"},{"version":"v22.16.0"},{"version":"v22.24.0-rc.1"}]'
function Invoke-WebRequest
{
    param($Uri, [switch]$UseBasicParsing, $TimeoutSec, $ErrorAction)
    if ($Uri -ne 'https://nodejs.org/dist/index.json' -or $TimeoutSec -ne 20) { throw 'Unexpected Node release endpoint' }
    return [pscustomobject]@{ Content = $script:nodeIndex }
}
$script:messages.Clear()
$script:commands.Clear()
Invoke-CheckStage 'Node.js LTS'
$result = Read-Result
if ($result.Result -ne 'AVAILABLE' -or $result.Installed -ne '22.15.0' -or $result.Available -ne '22.23.4' -or $script:commands.Count)
{
    throw 'NVM check crossed a major version, used winget or lost versions'
}
foreach ($invalidIndex in @('{}', 'invalid', '[{"version":"v26.1.0"}]', '[{"version":42}]'))
{
    $script:nodeIndex = $invalidIndex
    $script:messages.Clear()
    Invoke-CheckStage 'Node.js LTS'
    $result = Read-Result
    if ($result.Result -ne 'ERROR' -or $result.Installed -ne '22.15.0') { throw 'Node manifest failure lost the installed version' }
}
$script:nodeIndex = '[{"version":"v22.23.4"}]'
function Invoke-LoggedCommand
{
    param($FilePath, $Arguments)
    $script:commands.Add("$FilePath $($Arguments -join ' ')")
    if ($Arguments[0] -eq 'use') { $script:nodeVersion = $Arguments[1] }
}
$script:commands.Clear()
Update-DevelopmentTool 'Node.js LTS'
if (($script:commands -join '|') -ne 'nvm install 22.23.4|nvm use 22.23.4') { throw 'Node update did not stay with NVM and the current major' }
$failed = $false
try { Invoke-ComponentAction 'Node.js LTS' 'Uninstall' } catch { $failed = $true }
if (-not $failed) { throw 'NVM installation was sent to winget uninstall' }

function Get-DevelopmentToolOwner { param($Stage) return 'winget' }
function Get-DevelopmentSdkVersions { return @('10.0.102', '10.0.302', '10.0.303') }
function Get-DevelopmentSdkPlan { return @([pscustomobject]@{Installed='10.0.102';Available='10.0.114';Update=$true}, [pscustomobject]@{Installed='10.0.303';Available='10.0.303';Update=$false}) }
$script:messages.Clear()
$script:commands.Clear()
Invoke-CheckStage '.NET SDK 10'
$result = Read-Result
if ($result.Result -ne 'AVAILABLE' -or $result.Installed -ne '10.0.102; 10.0.303' -or $result.Available -ne '10.0.114; 10.0.303' -or $result.Info -notlike '*10.0.102 -> 10.0.114*') { throw 'SDK feature-band updates were not reported' }
$failed = $false
try { Invoke-ComponentAction '.NET SDK 10' 'Uninstall' } catch { $failed = $true }
if (-not $failed -or $script:commands.Count) { throw 'SDK guard allowed a destructive automatic upgrade' }

& {
    $previousNvmHome = $env:NVM_HOME
    $previousNvmSymlink = $env:NVM_SYMLINK
    $env:NVM_HOME = 'C:\Users\Fixture\nvm'
    $script:ownerNodeSource = Join-Path $env:NVM_HOME 'v22.15.0\node.exe'
    try
    {
    function Get-Command
    {
        param($Name, [switch]$All, $ErrorAction)
        if ($Name -eq 'node')
        {
            if (-not $script:ownerNodeSource) { return $null }
            return [pscustomobject]@{ Source = $script:ownerNodeSource }
        }
        if ($Name -eq 'pwsh') { return @([pscustomobject]@{ Source = 'C:\Program Files\PowerShell\7\pwsh.exe' }, [pscustomobject]@{ Source = 'C:\Users\admin\.dotnet\tools\pwsh.exe' }) }
        if ($Name -eq 'nvm') { return [pscustomobject]@{ Source = 'C:\Fixture\Nvm2\nvm.exe' } }
    }
    if ((& $ownerDefinition 'Node.js LTS') -ne 'nvm') { throw 'NVM-owned executable was not recognized' }
    $failed = $false
    try { & $ownerDefinition 'PowerShell 7' } catch { $failed = $true }
    if (-not $failed) { throw 'Conflicting PowerShell installations were not rejected' }
    $script:ownerNodeSource = Join-Path $env:NVM_HOME '.shim\node.exe'
    function Invoke-CommandCapture
    {
        param($FilePath, $Arguments, [switch]$NoLogCommand)
        if ($FilePath -ne 'nvm' -or ($Arguments -join ' ') -ne 'config get auto_install') { throw 'Unexpected ownership probe' }
        return [pscustomobject]@{ ExitCode = 0; Text = "auto_install = $script:shimAutoInstall" }
    }
    $script:shimAutoInstall = 'true'
    $failed = $false
    try { & $ownerDefinition 'Node.js LTS' } catch { $failed = $true }
    if (-not $failed) { throw 'NVM shim could install Node during a check' }
    $script:shimAutoInstall = 'false'
    if ((& $ownerDefinition 'Node.js LTS') -ne 'nvm') { throw 'Read-only NVM shim was not recognized' }
    $script:ownerNodeSource = 'C:\fnm\node.exe'
    $failed = $false
    try { & $ownerDefinition 'Node.js LTS' } catch { $failed = $true }
    if (-not $failed) { throw 'Unknown manager was silently replaced with winget' }
    $env:NVM_SYMLINK = 'C:\Fixture\nodejs'
    $script:ownerNodeSource = 'C:\Fixture\nodejs\node.exe'
    function Get-Item { param($LiteralPath, [switch]$Force, $ErrorAction) return [pscustomobject]@{ Attributes = [IO.FileAttributes]::Directory } }
    $failed = $false
    try { & $ownerDefinition 'Node.js LTS' } catch { $failed = $true }
    if (-not $failed) { throw 'A regular Node directory was mistaken for an NVM link' }
    $env:NVM_HOME=$null
    $env:NVM_SYMLINK=$null
    $script:ownerNodeSource='C:\Fixture\Nvm2\.nodejs\node.exe'
    function Get-Item { param($LiteralPath,[switch]$Force,$ErrorAction) return [pscustomobject]@{Attributes=[IO.FileAttributes]::ReparsePoint} }
    $script:shimAutoInstall='true'
    $failed=$false
    try { & $ownerDefinition 'Node.js LTS' } catch { $failed=$true }
    if (-not $failed) { throw 'NVM v2 junction bypassed read-only guard' }
    $script:shimAutoInstall='false'
    if ((& $ownerDefinition 'Node.js LTS') -ne 'nvm') { throw 'NVM v2 without legacy environment variables was not recognized' }
    function Get-Item { param($LiteralPath,[switch]$Force,$ErrorAction) return [pscustomobject]@{Attributes=[IO.FileAttributes]::Directory} }
    $failed=$false
    try { & $ownerDefinition 'Node.js LTS' } catch { $failed=$true }
    if (-not $failed) { throw 'A regular NVM v2 directory was accepted as a managed junction' }
    $script:ownerNodeSource = $null
    $failed = $false
    try { & $ownerDefinition 'Node.js LTS' } catch { $failed = $true }
    if (-not $failed) { throw 'NVM without an active Node was sent to a standalone installer' }
    }
    finally { $env:NVM_HOME = $previousNvmHome; $env:NVM_SYMLINK = $previousNvmSymlink }
}
Write-Host 'PASS: NVM ownership and release-line updates, read-only shim guard, unknown manager, manifest errors, lifecycle guard, PowerShell conflict, SDK pins and Store uncertainty'
