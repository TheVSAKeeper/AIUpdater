$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility
Import-Module Microsoft.PowerShell.Security
Import-Module Microsoft.PowerShell.Management
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'src\AIUpdater\Scripts\Manage-Components.ps1')
. (Join-Path $root 'src\AIUpdater\Scripts\Check-Updates.ps1')
$updateBody = (Get-Item Function:\Update-DevelopmentSdk).ScriptBlock.ToString().Replace('Global\AIUpdater.DotNetSdk', ('Local\AIUpdater.DotNetSdk.Tests.' + [guid]::NewGuid().ToString('N')))
Set-Item Function:\Update-DevelopmentSdk -Value ([scriptblock]::Create($updateBody))
$script:manifestError = $false
$script:downloadError = $false
$script:hash = 'a' * 128
$script:signature = 'Valid'
$script:exitCode = 0
$script:installed = @('10.0.102','10.0.302','10.0.303')
$script:after = @('10.0.102','10.0.114','10.0.302','10.0.303')
$script:installCount = 0
$script:retentionWrites = 0
$script:restored = $false
$script:policy = 'nextSession'
$script:runtimeLost = $false
$script:messages = [Collections.Generic.List[string]]::new()
$script:manifest = @{ 'channel-version' = '10.0'; releases = @(@{ sdks = @(
    @{version='10.0.114'; files=@(@{name='dotnet-sdk-win-x64.exe';rid='win-x64';url='https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.114/dotnet-sdk-10.0.114-win-x64.exe';hash=('a' * 128)})},
    @{version='10.0.303'; files=@(@{name='dotnet-sdk-win-x64.exe';rid='win-x64';url='https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.303/dotnet-sdk-10.0.303-win-x64.exe';hash=('a' * 128)})},
    @{version='10.0.403'; files=@()}, @{version='10.0.115-preview.1'; files=@()}
) }) }
function Invoke-WebRequest
{
    param($Uri,$OutFile,[switch]$UseBasicParsing,$TimeoutSec,$ErrorAction)
    if ($OutFile) { if ($script:downloadError) { throw 'Download failed' }; return }
    if ($script:manifestError) { throw 'Metadata unavailable' }
    return [pscustomobject]@{Content=($script:manifest | ConvertTo-Json -Depth 8)}
}
function Get-DevelopmentSdkVersions { if ($script:installCount) { return $script:after }; return $script:installed }
function Assert-DevelopmentSdkInstallOwner {}
function Write-Log { param($Message) $script:messages.Add($Message) }
function Get-FileHash { param($LiteralPath,$Algorithm) return [pscustomobject]@{Hash=$script:hash} }
function Get-AuthenticodeSignature { param($LiteralPath) return [pscustomobject]@{Status=$script:signature;SignerCertificate=[pscustomobject]@{Subject='CN=Microsoft Corporation, O=Microsoft Corporation, C=US'}} }
function Get-DevelopmentSdkRetention { return [pscustomobject]@{Exists=$true;Value=$script:policy;Kind='String'} }
function Set-DevelopmentSdkRetention
{
    param($Restore=$null)
    $script:retentionWrites++
    if ($Restore) { $script:policy=$Restore.Value; $script:restored=$true } else { $script:policy='never' }
}
function Start-Process
{
    param($FilePath,$ArgumentList,$WindowStyle,[switch]$Wait,[switch]$PassThru)
    if ($script:policy -ne 'never' -or $ArgumentList -notcontains '/norestart' -or $ArgumentList -notcontains '/quiet') { throw 'Unsafe installer invocation' }
    if ($FilePath -notlike '*10.0.114-win-x64.exe') { throw 'Wrong feature band installed' }
    $script:installCount++
    return [pscustomobject]@{ExitCode=$script:exitCode}
}
function Invoke-CommandCapture
{
    param($FilePath,$Arguments,[switch]$NoLogCommand)
    if ($FilePath -ne 'dotnet' -or $Arguments[0] -ne '--list-runtimes') { throw 'Unexpected command or winget mutation' }
    $text = if ($script:runtimeLost -and $script:installCount) { '' } else { 'Microsoft.NETCore.App 10.0.3 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]' }
    return [pscustomobject]@{ExitCode=0;Text=$text}
}
$logPath = Join-Path $root '.kc\scratch\dotnet-sdk-tests\update.log'
$null = New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force
$plan = @(Get-DevelopmentSdkPlan $script:installed)
if ($plan.Count -ne 2 -or @($plan | Where-Object Update).Count -ne 1 -or $plan[0].Available -ne '10.0.114') { throw 'Feature-band selection failed' }
Invoke-CheckStage '.NET SDK 10'
$result = ($script:messages | Where-Object { $_ -like 'UI_RESULT *' } | Select-Object -Last 1).Substring(10) | ConvertFrom-Json
if ($result.Result -ne 'AVAILABLE' -or $result.Info -notlike '*10.0.102 -> 10.0.114*' -or $script:retentionWrites) { throw 'Read-only SDK check failed' }
foreach ($case in @('success','reboot','installer-failure','missing-sdk','missing-runtime','bad-hash','bad-signature','download-failure'))
{
    $script:installCount=0; $script:retentionWrites=0; $script:restored=$false; $script:policy='nextSession'
    $script:exitCode = if ($case -eq 'reboot') { 3010 } elseif ($case -eq 'installer-failure') { 123 } else { 0 }
    $script:after = if ($case -eq 'missing-sdk') { @('10.0.114','10.0.303') } else { @('10.0.102','10.0.114','10.0.302','10.0.303') }
    $script:runtimeLost = $case -eq 'missing-runtime'
    $script:hash = if ($case -eq 'bad-hash') { 'b' * 128 } else { 'a' * 128 }
    $script:signature = if ($case -eq 'bad-signature') { 'NotSigned' } else { 'Valid' }
    $script:downloadError = $case -eq 'download-failure'
    $failed=$false
    $failure=$null
    try { Update-DevelopmentTool '.NET SDK 10' } catch { $failed=$true; $failure="$_" }
    if ($failed -ne ($case -notin @('success','reboot'))) { throw "Incorrect outcome: $case, $failure" }
    if ($script:installCount -and (-not $script:restored -or $script:policy -ne 'nextSession')) { throw "Retention not restored: $case" }
    if ($case -in @('bad-hash','bad-signature','download-failure') -and ($script:installCount -or $script:retentionWrites)) { throw "Unverified installer mutated system: $case" }
}
$script:manifestError=$true
$script:messages.Clear()
Invoke-CheckStage '.NET SDK 10'
$result = ($script:messages | Where-Object { $_ -like 'UI_RESULT *' } | Select-Object -Last 1).Substring(10) | ConvertFrom-Json
if ($result.Result -ne 'ERROR' -or $result.Installed -ne '10.0.303') { throw 'Metadata failure lost installed version' }
$script:manifestError=$false
$script:installCount=0; $script:retentionWrites=0
$script:installed=@('10.0.114','10.0.303')
Update-DevelopmentSdk
if ($script:retentionWrites -or $script:installCount) { throw 'Current SDKs triggered mutation' }
$script:installed=@()
Update-DevelopmentSdk
if ($script:retentionWrites -or $script:installCount) { throw 'Absent SDK triggered installation' }
$script:installed=@('10.0.102','10.0.302','10.0.303')
$missingBand=$false
try { $null=Get-DevelopmentSdkPlan @('10.0.501') } catch { $missingBand=$true }
if (-not $missingBand) { throw 'Missing feature-band metadata was ignored' }
$script:manifest.releases[0].sdks[0].files[0].url='https://untrusted.invalid/sdk.exe'
$failed=$false
try { $null=Get-DevelopmentSdkPlan $script:installed } catch { $failed=$true }
if (-not $failed) { throw 'Untrusted installer URL accepted' }
Write-Host 'PASS: SDK feature bands, read-only checks, pins retained, installer trust, native failures, reboot handling and retention restoration.'
