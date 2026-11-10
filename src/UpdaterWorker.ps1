#requires -Version 5.1
[CmdletBinding()]
param([string]$RequestPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Update-DiscordRules.ps1')

function Assert-DesktopAppsExited {
    param([string[]]$Roots)
    $currentSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $mainApps = @(Get-Process -Name 'ProtonVPN.Client','ProtonVPN','Discord','DiscordPTB','DiscordCanary' -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $currentSession -and -not (Test-ProcessHasExited $_) })
    if ($mainApps.Count -gt 0) { throw 'Discord or Proton VPN is running. Close both apps before refreshing.' }
    Assert-AppsClosed $Roots
}

function Get-ProtonLaunchPath {
    param([string]$InstallRoot = (Join-Path $env:ProgramFiles 'Proton\VPN'))
    $launcher = Join-Path $InstallRoot 'ProtonVPN.Launcher.exe'
    if (Test-Path -LiteralPath $launcher -PathType Leaf) { return [IO.Path]::GetFullPath($launcher) }
    return Get-ProtonClientPath -BaseFolder $InstallRoot
}

function Get-DiscordLaunchPath {
    param([object[]]$Inventory)
    foreach ($name in @('Discord.exe', 'DiscordPTB.exe', 'DiscordCanary.exe')) {
        $candidates = @($Inventory | Where-Object { [IO.Path]::GetFileName($_.Path) -ieq $name })
        if ($candidates.Count -eq 0) { continue }
        $ordered = $candidates | Sort-Object -Property @{ Expression = {
            $folder = [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($_.Path))
            $version = [version]'0.0'
            if ($folder -match '^app-(.+)$') {
                $parsed = $null
                if ([version]::TryParse($Matches[1], [ref]$parsed)) { $version = $parsed }
            }
            $version
        }; Descending = $true }, Path
        return @($ordered)[0].Path
    }
    throw 'No Discord application executable was found.'
}

function Start-DesktopClient {
    param([string]$Path)
    # Use the app's directory so it does not keep the updater folder locked.
    Start-Process -FilePath $Path -WorkingDirectory ([IO.Path]::GetDirectoryName($Path)) -WindowStyle Normal -ErrorAction Stop
}

function Invoke-InterfaceOperation {
    param([object]$Request)
    if ($Request.Action -notin @('Close','Refresh','LaunchProton','LaunchDiscord')) { throw 'Unknown operation.' }
    if ($Request.Action -eq 'LaunchProton') {
        $message = 'Proton VPN is already open. Check that it is connected before launching Discord.'
        if (@(Get-ProtonAppProcesses).Count -eq 0) {
            Start-DesktopClient (Get-ProtonLaunchPath)
            $message = 'Proton VPN launch requested. Check that it is connected before launching Discord.'
        }
        return [pscustomobject]@{ Success = $true; Action = 'LaunchProton'; Message = $message }
    }
    $requestedRoots = @($Request.Roots | ForEach-Object { [string]$_ })
    $roots = @()
    if ($requestedRoots.Count -gt 0) { $roots = @(Resolve-DiscordRoots $requestedRoots) }
    if ($Request.Action -eq 'LaunchDiscord') {
        if (@(Get-ProtonAppProcesses).Count -eq 0) {
            throw 'Open Proton VPN and connect before retrying Launch Discord.'
        }
        if ($roots.Count -eq 0) { throw 'No refreshed Discord installation was found.' }
        $inventory = @(Get-DiscordInventory $roots)
        $client = Get-DiscordLaunchPath $inventory
        if ($client -ine [string]$Request.DiscordPath) {
            throw 'The Discord executable changed after Refresh. Reopen the updater to close both apps and refresh the rules again.'
        }
        $mainFiles = @($inventory | Where-Object { [IO.Path]::GetFileName($_.Path) -match '^Discord(PTB|Canary)?\.exe$' })
        $message = 'Discord is already open.'
        if (@(Get-DiscordAppProcesses $roots $mainFiles).Count -eq 0) {
            Start-DesktopClient $client
            $message = 'Discord launch requested.'
        }
        return [pscustomobject]@{ Success = $true; Action = 'LaunchDiscord'; Message = $message }
    }
    if ($Request.Action -eq 'Close') {
        $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
        foreach ($process in @(Get-Process -Name 'Discord','DiscordPTB','DiscordCanary' -ErrorAction SilentlyContinue)) {
            if ($process.SessionId -ne $session) { continue }
            $path = $null
            try { $path = $process.Path } catch { }
            if (-not $path -or -not (Test-ManagedPath $path $roots)) {
                throw 'The running Discord installation could not be verified. Select its installation folder, or close Discord manually.'
            }
        }
        $inventory = @()
        if ($roots.Count -gt 0) { $inventory = @(Get-DiscordInventory $roots) }
        Close-DiscordAndProton $roots $inventory
        Assert-DesktopAppsExited $roots
        return [pscustomobject]@{
            Success = $true; Action = 'Close'; Message = 'Both desktop apps have exited. You can now refresh the rules.'
            Total = 0; Added = @(); Removed = @(); BackupPath = $null
        }
    }
    # The UI is not the write guard: recheck the real processes here and inside
    # the atomic writer, even if the button was enabled a moment earlier.
    Assert-DesktopAppsExited $roots
    if ($roots.Count -eq 0) { throw 'No Discord installation is selected or detected.' }
    if ([string]::IsNullOrWhiteSpace([string]$Request.SettingsPath)) { throw 'Select a Proton account settings file.' }
    $inventory = @(Get-DiscordInventory $roots)
    $discordPath = Get-DiscordLaunchPath $inventory
    $snapshot = Read-ProtonSettings -Path ([string]$Request.SettingsPath) -EnableSplitTunneling
    $plan = New-DiscordRulePlan $snapshot $roots $inventory
    Assert-DesktopAppsExited $roots
    $backup = Write-DiscordRulePlan $snapshot $plan $roots $inventory
    foreach ($path in $plan.Added) { Write-Host ('ADD ' + $path) }
    foreach ($path in $plan.Removed) { Write-Host ('REMOVE ' + $path) }
    $message = 'Rules refreshed.'
    if ($plan.EnabledSplitTunneling) { $message = 'Saved Split tunneling ON in Include mode and refreshed Discord rules.' }
    if (-not $plan.Changed) { $message = 'The rules are already current.' }
    return [pscustomobject]@{
        Success = $true; Action = 'Refresh'; Message = $message
        Total = $plan.DesiredPaths.Count; Added = @($plan.Added); Removed = @($plan.Removed); BackupPath = $backup
        EnabledSplitTunneling = $plan.EnabledSplitTunneling
        DiscordPath = $discordPath
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $resultPath = $null
    try {
        $requestFile = (Get-Item -LiteralPath $RequestPath -ErrorAction Stop).FullName
        $resultPath = Join-Path ([IO.Path]::GetDirectoryName($requestFile)) 'result.json'
        $request = [IO.File]::ReadAllText($requestFile) | ConvertFrom-Json
        $result = Invoke-InterfaceOperation $request
    } catch {
        $message = $_.Exception.Message
        if ($_.FullyQualifiedErrorId -match 'ConvertFromJson|Json' -or $message -match 'Invalid JSON|Conversion from JSON') {
            $message = 'The selected settings file contains invalid or unsupported JSON. No rules could be refreshed.'
        }
        $result = [pscustomobject]@{ Success = $false; Action = 'Error'; Message = $message }
    }
    if ($resultPath) {
        $temporary = $resultPath + '.tmp'
        [IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $result -Depth 20), (New-Object Text.UTF8Encoding($false)))
        [IO.File]::Move($temporary,$resultPath)
    }
    if (-not $result.Success) { Write-Error $result.Message -ErrorAction Continue; exit 1 }
}
