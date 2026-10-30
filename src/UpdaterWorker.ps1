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

function Invoke-InterfaceOperation {
    param([object]$Request)
    if ($Request.Action -notin @('Close','Refresh')) { throw 'Unknown operation.' }
    $requestedRoots = @($Request.Roots | ForEach-Object { [string]$_ })
    $roots = @()
    if ($requestedRoots.Count -gt 0) { $roots = @(Resolve-DiscordRoots $requestedRoots) }
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
    $snapshot = Read-ProtonSettings ([string]$Request.SettingsPath)
    $plan = New-DiscordRulePlan $snapshot $roots $inventory
    Assert-DesktopAppsExited $roots
    $backup = Write-DiscordRulePlan $snapshot $plan $roots $inventory
    foreach ($path in $plan.Added) { Write-Host ('ADD ' + $path) }
    foreach ($path in $plan.Removed) { Write-Host ('REMOVE ' + $path) }
    $message = 'Rules refreshed. Open Proton VPN and reconnect before reopening Discord.'
    if (-not $plan.Changed) { $message = 'The rules are already current. Open Proton VPN and reconnect before reopening Discord.' }
    return [pscustomobject]@{
        Success = $true; Action = 'Refresh'; Message = $message
        Total = $plan.DesiredPaths.Count; Added = @($plan.Added); Removed = @($plan.Removed); BackupPath = $backup
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
