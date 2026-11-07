$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$file = Join-Path $root 'build\Finish-DevelopmentMigration.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
. $file
$script:processes = @()
function Get-Process { param($Name, $ErrorAction) return $script:processes }
Assert-NoBlockingProcesses
foreach ($process in @(
    [pscustomobject]@{ Id = 1; ProcessName = 'node'; Path = 'C:\unrelated\node.exe' },
    [pscustomobject]@{ Id = 2; ProcessName = 'nvm'; Path = 'C:\nvm\nvm.exe' },
    [pscustomobject]@{ Id = 3; ProcessName = 'WindowsTerminal'; Path = 'C:\terminal.exe' },
    [pscustomobject]@{ Id = 4; ProcessName = 'pwsh'; Path = 'C:\Users\admin\.dotnet\tools\.store\powershell\pwsh.exe' },
    [pscustomobject]@{ Id = 5; ProcessName = 'pwsh'; Path = $null }
))
{
    $script:processes = @($process)
    $rejected = $false
    try { Assert-NoBlockingProcesses -IncludePowerShell } catch { $rejected = $true }
    if (-not $rejected) { throw "Не заблокирован процесс $($process.ProcessName)." }
    if ($process.ProcessName -eq 'pwsh')
    {
        Assert-NoBlockingProcesses
        if (@(Get-BlockingProcesses -IncludePowerShell | Where-Object ProcessName -eq 'pwsh').Count -ne 1)
        { throw 'Потерян blocker удаления PowerShell.' }
    }
}
$script:processes = @([pscustomobject]@{ Id = 6; ProcessName = 'pwsh'; Path = 'C:\Program Files\PowerShell\7\pwsh.exe' })
Assert-NoBlockingProcesses
Assert-NoBlockingProcesses -IncludePowerShell
$folder = Join-Path ([IO.Path]::GetTempPath()) ('aiu-migration-check-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $folder -Force
try
{
    $script:log = Join-Path $folder 'native.log'
    function Invoke-FakeNative { $global:LASTEXITCODE = $script:fakeCode; 'fixture output' }
    $script:fakeCode = 0
    if ((Invoke-Native 'Invoke-FakeNative' @()) -ne 'fixture output') { throw 'Потерян stdout команды.' }
    $script:fakeCode = 42
    $rejected = $false
    try { $null = Invoke-Native 'Invoke-FakeNative' @() } catch { $rejected = $true }
    if (-not $rejected) { throw 'Проигнорирован ненулевой exit code.' }
    $script:fakeCode = -1978335189
    $null = Invoke-Native 'Invoke-FakeNative' @() @(0,-1978335189)
}
finally
{
    Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
}
$calls = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() })
foreach ($forbidden in @('Stop-Process','taskkill','Remove-Item','Remove-ItemProperty','Set-Acl'))
{
    if ($forbidden -in $calls) { throw "Недопустимая операция $forbidden" }
}
Write-Host 'PASS: parsing, process guards, native exit codes, no process termination or deletion commands.'
