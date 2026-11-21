#requires -Version 5.1
[CmdletBinding()]
param([string]$RequestPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'UpdaterWorker.ps1')
. (Join-Path $PSScriptRoot 'Proton-Uia.ps1')

function Assert-PrototypeDiscordClosed {
    param([string[]]$Roots)
    $inventory = @(Get-DiscordInventory $Roots)
    if (@(Get-DiscordAppProcesses $Roots $inventory).Count -gt 0) {
        throw 'Discord or its updater is running. Exit it before retrying this step.'
    }
}

function Assert-PrototypeRulesCurrent {
    param([object]$State)
    $inventory = @(Get-DiscordInventory $State.Roots)
    $snapshot = Read-ProtonSettings -Path $State.SettingsPath -EnableSplitTunneling
    $plan = New-DiscordRulePlan $snapshot $State.Roots $inventory
    if ($plan.Changed -or (Get-DiscordLaunchPath $inventory) -ine $State.DiscordPath) {
        throw 'Saved rules or Discord files changed. Reopen the prototype and start a new run.'
    }
}

function Save-PrototypeState {
    param([object]$State, [string]$Path)
    $temporary = $Path + '.tmp'
    [IO.File]::WriteAllText($temporary, (ConvertTo-Json $State -Depth 12), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $Path) }
}

function Invoke-PrototypeOperation {
    param([object]$Request, [string]$StatePath)
    if ($Request.Action -notin @('Disconnect','Refresh','Reconnect','LaunchDiscord')) { throw 'Unknown prototype action.' }
    $state = $null
    if (Test-Path -LiteralPath $StatePath) { $state = [IO.File]::ReadAllText($StatePath) | ConvertFrom-Json }
    if ($Request.Action -eq 'Disconnect') {
        if (-not $Request.AccountConfirmed) { throw 'Confirm that the selected account matches the account signed into Proton.' }
        [void](Read-ProtonSettings -Path $Request.SettingsPath -EnableSplitTunneling)
        $roots = @(Resolve-DiscordRoots $Request.Roots)
        $inventory = @(Get-DiscordInventory $roots)
        # Validate the UI before closing Discord, so an unsupported client fails early.
        Show-ProtonUi
        $initialState = Get-ProtonUiState
        $findUpdaters = { Get-DiscordAppProcesses $roots $inventory -UpdatersOnly }
        if (-not (Wait-AppGroupExit $findUpdaters 30)) { throw 'Discord is updating. Wait for it to finish and retry.' }
        $findDiscord = {
            if (@(& $findUpdaters).Count -gt 0) { throw 'Discord started updating. Wait and retry.' }
            Get-DiscordAppProcesses $roots $inventory | Where-Object { $_.ProcessName -notin @('Update','updater_launcher') }
        }
        Close-AppGroup 'Discord' $findDiscord
        Disconnect-ProtonUi
        Assert-PrototypeDiscordClosed $roots
        $state = [pscustomobject]@{
            SettingsPath = [IO.Path]::GetFullPath($Request.SettingsPath)
            Roots = $roots; InitialState = $initialState
            Disconnected = $true; RulesSaved = $false; DiscordPath = $null; Connected = $false
        }
        Save-PrototypeState $state $StatePath
        return [pscustomobject]@{ Success=$true; Message='Discord is closed. Proton acknowledged Disconnected through its interface.' }
    }
    if ($null -eq $state -or -not $state.Disconnected) { throw 'Complete step 1 before continuing.' }
    if (-not [string]::Equals($state.SettingsPath, [IO.Path]::GetFullPath($Request.SettingsPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Account selection changed. Start a new prototype run.'
    }
    Assert-PrototypeDiscordClosed $state.Roots
    if ($Request.Action -eq 'Refresh') {
        if (-not $Request.AllowGracefulRestart) { throw 'Select the explicit graceful-restart option before refreshing.' }
        if (@(Get-ProtonAppProcesses).Count -gt 0) {
            Show-ProtonUi
            Exit-ProtonUiGracefully
        }
        # Reuse the original independently guarded atomic writer, only after exit.
        $result = Invoke-InterfaceOperation ([pscustomobject]@{
            Action='Refresh'; SettingsPath=$state.SettingsPath; Roots=$state.Roots
        })
        $state.DiscordPath = $result.DiscordPath
        $state.RulesSaved = $true
        $state.Connected = $false
        Save-PrototypeState $state $StatePath
        return [pscustomobject]@{ Success=$true; Message='Rules saved after Proton exited through its own menu. Click Reconnect to reload them.'
            BackupPath=$result.BackupPath }
    }
    if (-not $state.RulesSaved) { throw 'Refresh the rules before reconnecting or launching Discord.' }
    Assert-PrototypeRulesCurrent $state
    if ($Request.Action -eq 'Reconnect') {
        if (-not $Request.DefaultConnectionConfirmed) { throw 'Confirm reconnecting using Proton connection-card preferences.' }
        Show-ProtonUi
        Connect-ProtonUi
        Assert-PrototypeRulesCurrent $state
        Assert-PrototypeDiscordClosed $state.Roots
        $state.Connected = $true
        Save-PrototypeState $state $StatePath
        return [pscustomobject]@{ Success=$true; Message='Proton acknowledged Protected. Verify the account, server, and desired routing before launching Discord.' }
    }
    if (-not $state.Connected -or -not $Request.RoutingConfirmed) { throw 'Verify the Proton connection and desired routing before launching Discord.' }
    if ((Get-ProtonUiState) -ne 'Connected') { throw 'Proton is no longer Protected. Reconnect and verify it before retrying.' }
    Assert-PrototypeRulesCurrent $state
    Start-DesktopClient $state.DiscordPath
    return [pscustomobject]@{ Success=$true; Message='Process completed. Discord launch requested.' }
}

if ($MyInvocation.InvocationName -ne '.') {
    $mutex = $null; $owned = $false; $resultPath = $null
    try {
        $requestFile = (Get-Item -LiteralPath $RequestPath).FullName
        $folder = [IO.Path]::GetDirectoryName($requestFile)
        $resultPath = Join-Path $folder 'result.json'
        $request = [IO.File]::ReadAllText($requestFile) | ConvertFrom-Json
        $mutex = New-Object Threading.Mutex($false, 'Local\AntiJanjanator.ProtonUiaPrototype')
        try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
        if (-not $owned) { throw 'Another prototype action is running. Wait and retry.' }
        $statePath = Join-Path ([IO.Path]::GetDirectoryName($folder)) 'state.json'
        $result = Invoke-PrototypeOperation $request $statePath
    } catch { $result = [pscustomobject]@{ Success=$false; Message=$_.Exception.Message } }
    finally {
        if ($owned) { $mutex.ReleaseMutex() }
        if ($null -ne $mutex) { $mutex.Dispose() }
    }
    if ($resultPath) {
        [IO.File]::WriteAllText($resultPath, (ConvertTo-Json $result -Depth 12), (New-Object Text.UTF8Encoding($false)))
    }
}
