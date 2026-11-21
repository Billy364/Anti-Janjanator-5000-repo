#requires -Version 5.1
param([string]$WorkerPath, [string]$RunFolder)
. $WorkerPath
[void][IO.Directory]::CreateDirectory($RunFolder)
$root = Join-Path $RunFolder 'Discord'
[void][IO.Directory]::CreateDirectory((Join-Path $root 'app-1.0.2'))
$main = Join-Path $root 'app-1.0.2\Discord.exe'
[IO.File]::WriteAllText($main, 'synthetic executable; never run')
$file = Join-Path $RunFolder 'UserSettings.fixture.json'
$original = '{"IsUserSettingsMigrationDone":"true","KeepMe":"exact"}'
[IO.File]::WriteAllText($file, $original)
$statePath = Join-Path $RunFolder 'state.json'
$script:protonRunning = $true
$script:discordRunning = $true
$script:connection = 'Connected'
$script:events = @()
$script:failConnect = $false
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
$proton = [pscustomobject]@{ Id=999; ProcessName='ProtonVPN.Client'; Path=(Join-Path $env:ProgramFiles 'Proton\VPN\v5.1.9\ProtonVPN.Client.exe'); SessionId=$session }
function Get-ProtonAppProcesses { if ($script:protonRunning) { $proton } }
function Get-Process {
    [CmdletBinding()]param([string[]]$Name)
    if ($script:protonRunning -and $Name -contains 'ProtonVPN.Client') { $proton }
}
function Get-DiscordAppProcesses {
    param([string[]]$Roots, [object[]]$Inventory, [switch]$UpdatersOnly)
    if ($script:discordRunning -and -not $UpdatersOnly) { [pscustomobject]@{ Id=998; ProcessName='Discord'; Path=$main } }
}
function Stop-Process { throw 'Prototype must never force-stop Proton.' }
function Close-AppGroup {
    param([string]$Label, [scriptblock]$FindProcesses)
    if ($Label -cne 'Discord') { throw 'Prototype tried to close Proton through the force-stop helper.' }
    $script:events += 'CloseDiscord'; $script:discordRunning = $false
}
function Show-ProtonUi { $script:events += 'Show'; $script:protonRunning = $true }
function Get-ProtonUiState { $script:connection }
function Disconnect-ProtonUi { $script:events += 'Disconnect'; $script:connection = 'Disconnected' }
function Exit-ProtonUiGracefully {
    if ($script:connection -ne 'Disconnected') { throw 'Proton was not disconnected before exit.' }
    $script:events += 'GracefulExit'; $script:protonRunning = $false
}
function Connect-ProtonUi {
    $script:events += 'Connect'
    if ($script:failConnect) { throw 'Synthetic reconnect failure.' }
    $script:connection = 'Connected'
}
function Start-DesktopClient {
    param([string]$Path)
    if ($Path -cne $main -or $script:connection -ne 'Connected') { throw 'Unexpected launch target or state.' }
    $script:events += 'LaunchDiscord'
}
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Reject([scriptblock]$Action, [string]$Text) {
    $errorText = ''
    try { & $Action } catch { $errorText = $_.Exception.Message }
    Check ($errorText -like ('*' + $Text + '*')) ('Expected ' + $Text + ', got ' + $errorText)
}
$request = [pscustomobject]@{
    Action='Refresh'; SettingsPath=$file; Roots=@($root); AccountConfirmed=$true
    AllowGracefulRestart=$false; DefaultConnectionConfirmed=$false; RoutingConfirmed=$false
}
Reject { Invoke-PrototypeOperation $request $statePath } 'Complete step 1'
$request.Action = 'Disconnect'; $request.AccountConfirmed = $false
Reject { Invoke-PrototypeOperation $request $statePath } 'Confirm'
Check ($script:events.Count -eq 0) 'Unconfirmed action touched an app.'
$request.AccountConfirmed = $true
$result = Invoke-PrototypeOperation $request $statePath
Check ($result.Success -and $script:protonRunning -and -not $script:discordRunning -and $script:connection -eq 'Disconnected') 'Disconnect failed or terminated Proton.'
Check ([IO.File]::ReadAllText($file) -ceq $original) 'Disconnect changed account settings.'
$request.Action = 'Refresh'
Reject { Invoke-PrototypeOperation $request $statePath } 'explicit graceful-restart'
Check ($script:protonRunning -and [IO.File]::ReadAllText($file) -ceq $original) 'Unconfirmed refresh changed the client or settings.'
$request.AllowGracefulRestart = $true
$script:connection = 'Connected'
Reject { Invoke-PrototypeOperation $request $statePath } 'not disconnected'
$script:connection = 'Disconnected'
$result = Invoke-PrototypeOperation $request $statePath
Check ($result.Success -and -not $script:protonRunning -and (Test-Path $result.BackupPath)) 'Graceful restart refresh failed.'
Check ([IO.File]::ReadAllText($result.BackupPath) -ceq $original) 'Backup differs from original.'
$savedHash = (Get-FileHash $file).Hash
$request.Action = 'Reconnect'
Reject { Invoke-PrototypeOperation $request $statePath } 'Confirm reconnecting'
$request.DefaultConnectionConfirmed = $true; $script:failConnect = $true
Reject { Invoke-PrototypeOperation $request $statePath } 'Synthetic reconnect failure'
Check ((Get-FileHash $file).Hash -eq $savedHash) 'Reconnect failure rewrote settings.'
$script:failConnect = $false
$result = Invoke-PrototypeOperation $request $statePath
Check ($result.Success -and $script:connection -eq 'Connected') 'Reconnect retry failed.'
$request.Action = 'LaunchDiscord'
Reject { Invoke-PrototypeOperation $request $statePath } 'Verify the Proton'
$request.RoutingConfirmed = $true; $script:connection = 'Disconnected'
Reject { Invoke-PrototypeOperation $request $statePath } 'no longer Protected'
$script:connection = 'Connected'
$result = Invoke-PrototypeOperation $request $statePath
Check ($result.Success -and $script:events[-1] -eq 'LaunchDiscord') 'Verified launch failed.'
Check ((Get-FileHash $file).Hash -eq $savedHash) 'Launch rewrote settings.'
[IO.File]::WriteAllText($file, $original)
Reject { Invoke-PrototypeOperation $request $statePath } 'rules or Discord files changed'
Write-Output 'PASS: UI disconnect retains Proton, restart requires opt-in, offline rules use exact backups, reconnect retries preserve rules, and launch requires Protected plus routing confirmation.'
