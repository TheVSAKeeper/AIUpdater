$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility
Import-Module Microsoft.PowerShell.Security
Import-Module Microsoft.PowerShell.Management
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'src\AIUpdater\Scripts\Manage-Components.ps1')
. (Join-Path $root 'src\AIUpdater\Scripts\Check-Updates.ps1')
$script:DotNetSdkMutexName = 'Local\AIUpdater.DotNetSdk.Tests.' + [guid]::NewGuid().ToString('N')
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
$scratchDir = Join-Path ([IO.Path]::GetTempPath()) ('aiu-dotnet-sdk-tests-' + [guid]::NewGuid().ToString('N'))
$logPath = Join-Path $scratchDir 'update.log'
$null = New-Item -ItemType Directory -Path $scratchDir -Force
try
{
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

    & {
        $script:channelPolicies = @{ '10.0' = 'nextSession'; '11.0' = 'nextSession'; '12.0' = 'nextSession' }
        $script:channelSdks = [Collections.Generic.List[string]]::new([string[]]@('10.0.102', '11.0.100'))
        $script:channelRuntimes = [Collections.Generic.List[object]]::new([object[]]@(
            [pscustomobject]@{ Name = 'Microsoft.NETCore.App'; Version = '10.0.3' },
            [pscustomobject]@{ Name = 'Microsoft.NETCore.App'; Version = '11.0.0' }
        ))
        $script:executedInstallers = [Collections.Generic.List[string]]::new()
        $script:signatureValid = $true

        $manifests = @{
            '10.0' = @{
                'channel-version' = '10.0'
                'support-phase' = 'active'
                releases = @(@{
                    sdks = @(@{ version = '10.0.114'; files = @(@{ name = 'dotnet-sdk-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.114/dotnet-sdk-10.0.114-win-x64.exe'; hash = ('a' * 128) }) })
                    runtime = @{ version = '10.0.5'; files = @(@{ name = 'dotnet-runtime-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.5/dotnet-runtime-10.0.5-win-x64.exe'; hash = ('a' * 128) }) }
                    windowsdesktop = @{ version = '10.0.5'; files = @(@{ name = 'windowsdesktop-runtime-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.5/windowsdesktop-runtime-10.0.5-win-x64.exe'; hash = ('a' * 128) }) }
                    'aspnetcore-runtime' = @{ version = '10.0.5'; files = @(@{ name = 'aspnetcore-runtime-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.5/aspnetcore-runtime-10.0.5-win-x64.exe'; hash = ('a' * 128) }) }
                })
            }
            '11.0' = @{
                'channel-version' = '11.0'
                'support-phase' = 'active'
                releases = @(@{
                    sdks = @(@{ version = '11.0.101'; files = @(@{ name = 'dotnet-sdk-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/11.0.101/dotnet-sdk-11.0.101-win-x64.exe'; hash = ('a' * 128) }) })
                    runtime = @{ version = '11.0.1'; files = @(@{ name = 'dotnet-runtime-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/11.0.1/dotnet-runtime-11.0.1-win-x64.exe'; hash = ('a' * 128) }) }
                    windowsdesktop = @{ version = '11.0.1'; files = @(@{ name = 'windowsdesktop-runtime-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/11.0.1/windowsdesktop-runtime-11.0.1-win-x64.exe'; hash = ('a' * 128) }) }
                    'aspnetcore-runtime' = @{ version = '11.0.1'; files = @(@{ name = 'aspnetcore-runtime-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/11.0.1/aspnetcore-runtime-11.0.1-win-x64.exe'; hash = ('a' * 128) }) }
                })
            }
            '12.0' = @{
                'channel-version' = '12.0'
                'support-phase' = 'preview'
                releases = @(@{
                    sdks = @(@{ version = '12.0.100-preview.2.26150.4'; files = @(@{ name = 'dotnet-sdk-win-x64.exe'; rid = 'win-x64'; url = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/12.0.100-preview.2.26150.4/dotnet-sdk-12.0.100-preview.2.26150.4-win-x64.exe'; hash = ('a' * 128) }) })
                })
            }
        }

        function Get-DotNetWindowsArchitecture { return [pscustomobject]@{ Rid = 'win-x64'; Label = 'x64' } }
        function Assert-DevelopmentSdkInstallOwner { param([switch]$AllowMissingDotNet) }
        function Get-FileHash { param($LiteralPath, $Algorithm) return [pscustomobject]@{ Hash = ('a' * 128) } }
        function Get-AuthenticodeSignature
        {
            param($LiteralPath)
            $status = if ($script:signatureValid) { 'Valid' } else { 'NotSigned' }
            return [pscustomobject]@{ Status = $status; SignerCertificate = [pscustomobject]@{ Subject = 'CN=Microsoft Corporation, O=Microsoft Corporation, C=US' } }
        }
        function Invoke-WebRequest
        {
            param($Uri, $OutFile, [switch]$UseBasicParsing, $TimeoutSec, $ErrorAction)
            if ($OutFile) { Set-Content -LiteralPath $OutFile -Value 'stub' -Encoding ASCII; return }
            foreach ($ch in $manifests.Keys)
            {
                if ($Uri -eq "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$ch/releases.json")
                {
                    return [pscustomobject]@{ Content = ($manifests[$ch] | ConvertTo-Json -Depth 10) }
                }
            }
            throw "Unexpected metadata URI: $Uri"
        }
        function Get-DotNetInstalledSdks
        {
            param([string]$Channel = '')
            if ($Channel) { return @($script:channelSdks | Where-Object { $_.StartsWith("$Channel.", [StringComparison]::Ordinal) }) }
            return @($script:channelSdks)
        }
        function Get-DevelopmentSdkVersions { return @(Get-DotNetInstalledSdks | Where-Object { $_ -match '^([1-9][0-9]+)\.[0-9]+\.[0-9]+$' }) }
        function Get-DotNetInstalledRuntimes { return @($script:channelRuntimes) }
        function Get-DevelopmentSdkRetention
        {
            param([string]$Channel = '10.0')
            return [pscustomobject]@{ Exists = $true; Value = $script:channelPolicies[$Channel]; Kind = 'String' }
        }
        function Set-DevelopmentSdkRetention
        {
            param($Restore = $null, [string]$Channel = '10.0')
            if ($Restore) { $script:channelPolicies[$Channel] = $Restore.Value }
            else { $script:channelPolicies[$Channel] = 'never' }
        }
        function Start-Process
        {
            param($FilePath, $ArgumentList, $WindowStyle, [switch]$Wait, [switch]$PassThru)
            $leaf = Split-Path -Leaf $FilePath
            $script:executedInstallers.Add("$leaf|$($ArgumentList -join ' ')")
            if ($ArgumentList -contains '/install')
            {
                if ($leaf -match '^dotnet-sdk-(.+)-win-x64\.exe$')
                {
                    $ver = $Matches[1]
                    $ch = ($ver -split '\.')[0..1] -join '.'
                    if ($script:channelPolicies[$ch] -ne 'never') { throw "Retention policy was not active for channel $ch" }
                    if ($ver -notin $script:channelSdks) { $script:channelSdks.Add($ver) }
                }
                elseif ($leaf -match '^(dotnet-runtime|windowsdesktop-runtime|aspnetcore-runtime)-(.+)-win-x64\.exe$')
                {
                    $fw = switch ($Matches[1])
                    {
                        'dotnet-runtime' { 'Microsoft.NETCore.App' }
                        'windowsdesktop-runtime' { 'Microsoft.WindowsDesktop.App' }
                        'aspnetcore-runtime' { 'Microsoft.AspNetCore.App' }
                    }
                    $ver = $Matches[2]
                    $script:channelRuntimes.Add([pscustomobject]@{ Name = $fw; Version = $ver })
                }
            }
            return [pscustomobject]@{ ExitCode = 0 }
        }

        $multiPlan = @(Get-DevelopmentSdkPlan (Get-DevelopmentSdkVersions))
        if ($multiPlan.Count -ne 2 -or @($multiPlan | Where-Object Update).Count -ne 2) { throw 'Multi-channel SDK plan failed across 10.0 and 11.0' }

        Update-DevelopmentSdk
        if ('10.0.114' -notin $script:channelSdks -or '11.0.101' -notin $script:channelSdks -or '10.0.102' -notin $script:channelSdks -or '11.0.100' -notin $script:channelSdks)
        {
            throw 'Multi-channel servicing failed to install updates or lost pinned SDKs'
        }
        if ($script:channelPolicies['10.0'] -ne 'nextSession' -or $script:channelPolicies['11.0'] -ne 'nextSession')
        {
            throw 'Per-channel retention policies were not restored after multi-channel servicing'
        }

        Invoke-DotNetPackageOperation -Action Install -Kind DesktopRuntime -Channel '11.0'
        Invoke-DotNetPackageOperation -Action Install -Kind AspNetCoreRuntime -Channel '11.0'
        Invoke-DotNetPackageOperation -Action Install -Kind Sdk -Channel '12.0'
        if ('12.0.100-preview.2.26150.4' -notin $script:channelSdks) { throw 'Preview channel SDK install failed' }
        if (-not @($script:channelRuntimes | Where-Object { $_.Name -eq 'Microsoft.WindowsDesktop.App' -and $_.Version -eq '11.0.1' }).Count) { throw '.NET 11 Desktop Runtime install failed' }
        if (-not @($script:channelRuntimes | Where-Object { $_.Name -eq 'Microsoft.AspNetCore.App' -and $_.Version -eq '11.0.1' }).Count) { throw '.NET 11 ASP.NET Core Runtime install failed' }

        $fakeBundleExe = Join-Path $scratchDir 'PackageCacheStub\dotnet-sdk-11.0.101-win-x64.exe'
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $fakeBundleExe) -Force
        Set-Content -LiteralPath $fakeBundleExe -Value 'stub' -Encoding ASCII
        $prevProgramData = $env:ProgramData
        try
        {
            $fakeProgramData = Join-Path $scratchDir 'ProgramData'
            $cachedExe = Join-Path $fakeProgramData 'Package Cache\{11111111-2222-3333-4444-555555555555}\dotnet-sdk-11.0.101-win-x64.exe'
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $cachedExe) -Force
            Set-Content -LiteralPath $cachedExe -Value 'stub' -Encoding ASCII
            $env:ProgramData = $fakeProgramData

            function Get-DotNetUninstallEntries
            {
                return @(
                    [pscustomobject]@{
                        DisplayName = 'Microsoft .NET SDK 11.0.101 (x64)'
                        Publisher = 'Microsoft Corporation'
                        SystemComponent = 0
                        QuietUninstallString = "`"$cachedExe`" /uninstall /quiet"
                    }
                )
            }

            $script:executedInstallers.Clear()
            Invoke-DotNetPackageOperation -Action Uninstall -Kind Sdk -Channel '11.0' -Version '11.0.101'
            if ($script:executedInstallers.Count -ne 1 -or $script:executedInstallers[0] -notlike 'dotnet-sdk-11.0.101-win-x64.exe|/uninstall /quiet /norestart*')
            {
                throw 'Standalone Burn bundle uninstall did not invoke quiet norestart uninstaller'
            }

            $vsBlocked = $false
            try { Invoke-DotNetPackageOperation -Action Uninstall -Kind Sdk -Channel '10.0' -Version '10.0.302' } catch { $vsBlocked = $true }
            if (-not $vsBlocked) { throw 'Visual Studio or unmanaged SDK uninstall was not blocked' }

            $script:signatureValid = $false
            $untrustedBlocked = $false
            try { Invoke-DotNetPackageOperation -Action Uninstall -Kind Sdk -Channel '11.0' -Version '11.0.101' } catch { $untrustedBlocked = $true }
            if (-not $untrustedBlocked) { throw 'Unsigned cached bundle uninstaller was not rejected' }
        }
        finally
        {
            $env:ProgramData = $prevProgramData
        }
    }
}
finally
{
    Remove-Item -LiteralPath $scratchDir -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'PASS: SDK feature bands, read-only checks, pins retained, installer trust, native failures, reboot handling and retention restoration.'
