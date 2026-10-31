#requires -Version 5.1
<#
.SYNOPSIS
Refresh Discord executable paths in Proton VPN's Windows Include list.
.DESCRIPTION
Preview is the default. -Apply writes only while Proton's client and Discord
are closed, creates an exact backup, and replaces the settings file atomically.
-CloseApps closes Discord and Proton's client before scanning and writing.
No VPN services are stopped. No scheduled task is installed.
#>
[CmdletBinding()]
param(
    [string]$SettingsPath,
    [string[]]$DiscordRoot,
    [switch]$Apply,
    [switch]$OpenProton,
    [switch]$CloseApps,
    [switch]$ChooseAccount,
    [switch]$Diagnose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RefreshStage = 'Starting'

function Set-RefreshStage {
    param([string]$Stage)
    $script:RefreshStage = $Stage
    Write-Host ('--- ' + $Stage + ' ---')
}

function Find-RunningDiscordRoots {
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $roots = @()
    foreach ($process in @(Get-Process -Name 'Discord','DiscordPTB','DiscordCanary' -ErrorAction SilentlyContinue)) {
        if ($process.SessionId -ne $session) { continue }
        $path = $null
        try { $path = $process.Path } catch { }
        if (-not $path) { continue }
        try {
            $folder = Get-Item -LiteralPath ([IO.Path]::GetDirectoryName($path)) -Force -ErrorAction Stop
            while ($null -ne $folder) {
                if ($folder.Name -match '^Discord(PTB|Canary)?$') {
                    $roots += ConvertTo-NormalPath $folder.FullName
                    break
                }
                $folder = $folder.Parent
            }
        } catch { }
    }
    return @($roots | Sort-Object -Unique)
}

function ConvertTo-NormalPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ($Path -notmatch '^[A-Za-z]:[\\/]') {
        throw 'Only absolute local Windows paths are supported.'
    }
    $normalized = [regex]::Replace($Path.Replace('/', '\'), '\\+', '\')
    return [IO.Path]::GetFullPath($normalized).TrimEnd('\')
}

function Test-ManagedPath {
    param([string]$Path, [string[]]$Roots)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    try { $normalized = ConvertTo-NormalPath $Path } catch { return $false }
    foreach ($root in $Roots) {
        if ($normalized.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Resolve-DiscordRoots {
    param([string[]]$RequestedRoots)
    if (-not $RequestedRoots -or $RequestedRoots.Count -eq 0) {
        $RequestedRoots = @('Discord', 'DiscordPTB', 'DiscordCanary') |
            ForEach-Object { Join-Path $env:LOCALAPPDATA $_ } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Container }
        $RequestedRoots = @($RequestedRoots) + @(Find-RunningDiscordRoots)
        $RequestedRoots = @($RequestedRoots | Sort-Object -Unique)
    }
    $resolved = @()
    foreach ($candidate in $RequestedRoots) {
        $item = Get-Item -LiteralPath $candidate -Force
        if (-not $item.PSIsContainer -or $item.Name -notmatch '^Discord(PTB|Canary)?$') {
            throw 'Each DiscordRoot must be an installation folder named Discord, DiscordPTB, or DiscordCanary.'
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Linked installation folders are not supported; choose the real Discord folder.'
        }
        $resolved += ConvertTo-NormalPath $item.FullName
    }
    if ($resolved.Count -eq 0) { throw 'No Discord installation was found in the standard folders or from running Discord. Run Diagnose Proton settings.cmd or use -DiscordRoot.' }
    return @($resolved | Sort-Object -Unique)
}

function Get-DiscordInventory {
    param([string[]]$Roots)
    $files = @()
    foreach ($root in $Roots) {
        $queue = New-Object 'System.Collections.Generic.Queue[string]'
        $queue.Enqueue($root)
        while ($queue.Count -gt 0) {
            $folder = $queue.Dequeue()
            foreach ($item in @(Get-ChildItem -LiteralPath $folder -Force -ErrorAction Stop)) {
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Linked files/folders found under $root. Scan stopped to avoid incomplete rules."
                }
                if ($item.PSIsContainer) {
                    $queue.Enqueue($item.FullName)
                } elseif ($item.Extension -ieq '.exe') {
                    $files += [pscustomobject]@{
                        Path = ConvertTo-NormalPath $item.FullName
                        Length = $item.Length
                        Modified = $item.LastWriteTimeUtc.Ticks
                    }
                }
            }
        }
        $mainFiles = @($files | Where-Object {
            (Test-ManagedPath $_.Path @($root)) -and
            ([IO.Path]::GetFileName($_.Path) -match '^Discord(PTB|Canary)?\.exe$')
        })
        if ($mainFiles.Count -eq 0) {
            throw "No Discord application executable found under $root. No rules will be removed."
        }
    }
    return @($files | Sort-Object Path -Unique)
}

function Get-InventorySignature {
    param([object[]]$Inventory)
    return (($Inventory | ForEach-Object {
        $_.Path.ToLowerInvariant() + '|' + $_.Length + '|' + $_.Modified
    }) -join "`n")
}

function Read-ProtonSettings {
    param([string]$Path, [switch]$EnableSplitTunneling)
    $fullPath = (Get-Item -LiteralPath $Path -Force).FullName
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
    $encoding = New-Object Text.UTF8Encoding($false, $true)
    $text = $encoding.GetString($bytes)
    if ($hasBom) { $text = $text.Substring(1) }
    try { $settings = $text | ConvertFrom-Json } catch { throw 'Settings JSON could not be read. Let Proton finish saving and retry.' }
    if ($null -eq $settings -or $settings -isnot [pscustomobject]) { throw 'Unsupported Proton settings format.' }
    # Proton omits keys whose values still use defaults. Missing flags are off,
    # the default mode is Standard, and an omitted app list has no saved entries.
    $enabled = 'false'; $mode = 'Standard'; $appsJson = '[]'
    foreach ($property in $settings.PSObject.Properties) {
        if ($null -ne $property.Value -and $property.Value -isnot [string]) { throw 'Unsupported Proton preference encoding.' }
    }
    $entries = [regex]::Matches($text, '(?<key>"(?:\\.|[^"\\])*")\s*:\s*(?<value>"(?:\\.|[^"\\])*"|null)')
    $keys = @{}
    foreach ($entry in $entries) {
        $key = $entry.Groups['key'].Value | ConvertFrom-Json
        if ($keys.ContainsKey($key)) { throw 'Duplicate or ambiguous Proton settings property.' }
        $keys[$key] = $entry
    }
    if ($entries.Count -ne @($settings.PSObject.Properties).Count) { throw 'Unsupported Proton settings structure.' }
    $recognized = @('IsUserSettingsMigrationDone','VpnProtocol','IsSplitTunnelingEnabled','SplitTunnelingMode','SplitTunnelingInverseAppsList') | Where-Object { $keys.ContainsKey($_) }
    if (@($recognized).Count -eq 0) { throw 'Unsupported Proton account settings format.' }
    if ($null -ne $settings.PSObject.Properties['IsSplitTunnelingEnabled'] -and $null -ne $settings.IsSplitTunnelingEnabled) { $enabled = [string]$settings.IsSplitTunnelingEnabled }
    if ($null -ne $settings.PSObject.Properties['SplitTunnelingMode'] -and $null -ne $settings.SplitTunnelingMode) { $mode = [string]$settings.SplitTunnelingMode }
    if ($enabled -notin @('true','false') -or $mode -notin @('Standard','Inverse')) { throw 'Unsupported Proton split tunneling values.' }
    if (-not $EnableSplitTunneling -and ($enabled -ine 'true' -or $mode -ine 'Inverse')) {
        throw 'Enable Split tunneling in Include mode in Proton first. The updater does not change your VPN mode.'
    }
    if ($null -ne $settings.PSObject.Properties['SplitTunnelingInverseAppsList'] -and $null -ne $settings.SplitTunnelingInverseAppsList) { $appsJson = $settings.SplitTunnelingInverseAppsList }
    if ($appsJson.Trim() -notmatch '^\[') {
        throw 'Unsupported Included apps representation; expected a JSON array stored as a string.'
    }
    $valueMatch = $null
    if ($keys.ContainsKey('SplitTunnelingInverseAppsList')) { $valueMatch = $keys['SplitTunnelingInverseAppsList'].Groups['value'] }
    # Windows PowerShell 5.1 emits a JSON array as one pipeline object; newer
    # PowerShell enumerates it. Assign first so both produce the same app list.
    try { $decodedApps = $appsJson | ConvertFrom-Json } catch { throw 'Included apps JSON is invalid. Reapply Include mode in Proton.' }
    $apps = @()
    if ($null -ne $decodedApps) { $apps = @($decodedApps) }
    foreach ($app in $apps) {
        foreach ($field in @('AppFilePath', 'AlternateAppFilePaths', 'IsActive')) {
            if ($null -eq $app -or $null -eq $app.PSObject.Properties[$field]) {
                throw "Unsupported app entry: missing $field."
            }
        }
        if ($app.AppFilePath -isnot [string] -or $app.IsActive -isnot [bool] -or
            $app.AlternateAppFilePaths -isnot [array]) {
            throw 'Unsupported app entry types. No settings were changed.'
        }
    }
    return [pscustomobject]@{
        Path = $fullPath; Bytes = $bytes; Text = $text; HasBom = $hasBom
        ValueMatch = $valueMatch; Apps = $apps; PropertyMatches = $keys
        EnableSplitTunneling = [bool]$EnableSplitTunneling
        SplitTunnelingEnabled = $enabled; SplitTunnelingMode = $mode
    }
}

function New-DiscordRulePlan {
    param([object]$Snapshot, [string[]]$Roots, [object[]]$Inventory)
    $preserved = @()
    $managed = @()
    foreach ($app in $Snapshot.Apps) {
        if (Test-ManagedPath $app.AppFilePath $Roots) {
            foreach ($alternate in $app.AlternateAppFilePaths) {
                if (-not (Test-ManagedPath $alternate $Roots)) {
                    throw 'A Discord entry contains an alternate path outside the managed folders. Refusing to discard another app.'
                }
            }
            $managed += $app
        } else {
            $preserved += $app
        }
    }
    $desiredPaths = @($Inventory | ForEach-Object { $_.Path })
    $newApps = @($preserved)
    foreach ($path in $desiredPaths) {
        $existing = @($managed | Where-Object {
            (ConvertTo-NormalPath $_.AppFilePath).Equals($path, [StringComparison]::OrdinalIgnoreCase)
        })
        if ($existing.Count -gt 1) { throw "Duplicate existing Discord rules for $path. Resolve them in Proton first." }
        if ($existing.Count -eq 1) {
            $entry = $existing[0] | ConvertTo-Json -Depth 30 -Compress | ConvertFrom-Json
            $entry.IsActive = $true
            $entry.AlternateAppFilePaths = @($entry.AlternateAppFilePaths | Where-Object {
                $alternatePath = ConvertTo-NormalPath $_
                @($desiredPaths | Where-Object { $_ -ieq $alternatePath }).Count -gt 0
            })
            $newApps += $entry
        } else {
            $newApps += [pscustomobject]@{
                AppFilePath = $path
                AlternateAppFilePaths = @()
                IsActive = $true
            }
        }
    }
    $oldCanonical = ConvertTo-Json -InputObject @($Snapshot.Apps) -Depth 30 -Compress
    $newCanonical = ConvertTo-Json -InputObject @($newApps) -Depth 30 -Compress
    $activated = $Snapshot.EnableSplitTunneling -and ($Snapshot.SplitTunnelingEnabled -cne 'true' -or $Snapshot.SplitTunnelingMode -cne 'Inverse')
    $changed = $oldCanonical -cne $newCanonical -or $activated
    # The GUI explicitly requests automatic activation. Commit its flag/mode
    # and app list in one atomic replacement and one exact original backup.
    $updates = [ordered]@{ SplitTunnelingInverseAppsList = $newCanonical }
    if ($Snapshot.EnableSplitTunneling) { $updates['IsSplitTunnelingEnabled'] = 'true'; $updates['SplitTunnelingMode'] = 'Inverse' }
    $replacements = @(); $additions = @()
    foreach ($key in $updates.Keys) {
        $encodedValue = ConvertTo-Json -InputObject ([string]$updates[$key]) -Compress
        if ($Snapshot.PropertyMatches.ContainsKey($key)) {
            $span = $Snapshot.PropertyMatches[$key].Groups['value']
            $replacements += [pscustomobject]@{ Index = $span.Index; Length = $span.Length; Value = $encodedValue }
        } else { $additions += ((ConvertTo-Json -InputObject $key -Compress) + ':' + $encodedValue) }
    }
    $newText = $Snapshot.Text
    foreach ($replacement in @($replacements | Sort-Object Index -Descending)) {
        $newText = $newText.Substring(0, $replacement.Index) + $replacement.Value + $newText.Substring($replacement.Index + $replacement.Length)
    }
    if ($additions.Count -gt 0) { $newText = $newText.Insert($newText.LastIndexOf('}'), ',' + ($additions -join ',')) }
    $encoding = New-Object Text.UTF8Encoding($false)
    [byte[]]$newBytes = $encoding.GetBytes($newText)
    if ($Snapshot.HasBom) { $newBytes = [byte[]](@(239, 187, 191) + $newBytes) }
    $oldPaths = @($managed | ForEach-Object { ConvertTo-NormalPath $_.AppFilePath })
    return [pscustomobject]@{
        Changed = $changed; Bytes = $newBytes; DesiredPaths = $desiredPaths
        EnabledSplitTunneling = $activated
        Added = @($desiredPaths | Where-Object { $oldPaths -inotcontains $_ })
        Removed = @($oldPaths | Where-Object { $desiredPaths -inotcontains $_ })
        PreservedCount = $preserved.Count
        Reactivated = @($managed | Where-Object {
            -not $_.IsActive -and $desiredPaths -icontains (ConvertTo-NormalPath $_.AppFilePath)
        }).Count
    }
}

function Test-ProcessHasExited {
    param([object]$Process)
    try { return [bool]$Process.HasExited } catch { return $false }
}

function Get-DiscordAppProcesses {
    param([string[]]$Roots, [object[]]$Inventory, [switch]$UpdatersOnly)
    $names = @($Inventory | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Path) } | Sort-Object -Unique)
    if ($UpdatersOnly) { $names = @($names | Where-Object { $_ -in @('Update', 'updater_launcher') }) }
    if ($names.Count -eq 0) { return }
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    foreach ($process in @(Get-Process -Name $names -ErrorAction SilentlyContinue)) {
        if (Test-ProcessHasExited $process) { continue }
        if ($process.SessionId -ne $session) { continue }
        $path = $null
        try { $path = $process.Path } catch { }
        if (-not $path) {
            # A process can finish between enumeration and reading its image.
            # That is a successful exit, not a permission/verification failure.
            try { if ($process.HasExited) { continue } } catch { }
            if ($process.ProcessName -match '^Discord(PTB|Canary)?$' -or
                $process.ProcessName -in @('Update', 'updater_launcher')) {
                throw 'Cannot verify the executable path of a running Discord app/updater. No unidentified processes will be stopped.'
            }
            continue
        }
        if (Test-ManagedPath $path $Roots) { $process }
    }
}

function Get-ProtonAppProcesses {
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $installationRoot = ConvertTo-NormalPath (Join-Path $env:ProgramFiles 'Proton\VPN')
    foreach ($process in @(Get-Process -Name 'ProtonVPN.Client', 'ProtonVPN' -ErrorAction SilentlyContinue)) {
        if (Test-ProcessHasExited $process) { continue }
        if ($process.SessionId -ne $session) { continue }
        $path = $null
        try { $path = $process.Path } catch { }
        if (-not $path) { try { if ($process.HasExited) { continue } } catch { } }
        if (-not $path -or -not (Test-ManagedPath $path @($installationRoot))) {
            throw 'Cannot verify the Proton client installation path. Exit that client manually and rerun.'
        }
        $process
    }
}

function Wait-AppGroupExit {
    param([scriptblock]$FindProcesses, [int]$TimeoutSeconds)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (@(& $FindProcesses).Count -eq 0) { return $true }
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 250
    } while ($true)
}

function Close-AppGroup {
    param([string]$Label, [scriptblock]$FindProcesses)
    $processes = @(& $FindProcesses)
    if ($processes.Count -eq 0) { Write-Host ('No running ' + $Label + ' process was found.'); return }
    Write-Host ('Closing ' + $Label + '...')
    foreach ($process in $processes) {
        Write-Host ('  PID ' + $process.Id + ': ' + $process.Path)
        try { [void]$process.CloseMainWindow() } catch { }
    }
    if (Wait-AppGroupExit $FindProcesses 5) { Write-Host ($Label + ' exited.'); return }
    # Tray apps can ignore a window-close request. Stop only the freshly
    # rediscovered processes whose image paths were verified by the finder.
    Write-Host ('Ending remaining ' + $Label + ' processes...')
    foreach ($process in @(& $FindProcesses)) {
        try { Stop-Process -InputObject $process -Force -ErrorAction Stop }
        catch {
            if (@(& $FindProcesses | Where-Object { $_.Id -eq $process.Id }).Count -gt 0) {
                throw "Could not close $Label. No settings will be written. $($_.Exception.Message)"
            }
        }
    }
    if (-not (Wait-AppGroupExit $FindProcesses 10)) {
        throw "$Label is still running or relaunched itself. No settings will be written."
    }
    Write-Host ($Label + ' exited; verified no matching process remains.')
}

function Close-DiscordAndProton {
    param([string[]]$Roots, [object[]]$Inventory)
    # Validate both selections before requesting any app to exit.
    [void]@(Get-ProtonAppProcesses)
    [void]@(Get-DiscordAppProcesses $Roots $Inventory)
    $findUpdaters = { Get-DiscordAppProcesses $Roots $Inventory -UpdatersOnly }
    if (@(& $findUpdaters).Count -gt 0) {
        Write-Host 'Waiting for the Discord updater to finish (up to 30 seconds)...'
        if (-not (Wait-AppGroupExit $findUpdaters 30)) {
            throw 'Discord updater is still running. Wait for it to finish and rerun; it was not terminated.'
        }
    }
    $findDiscord = {
        Get-DiscordAppProcesses $Roots $Inventory | Where-Object {
            $_.ProcessName -notin @('Update', 'updater_launcher')
        }
    }
    Close-AppGroup 'Discord' $findDiscord
    # An updater that appeared during shutdown must finish before touching
    # Proton. Do not force-terminate an installation that is in progress.
    if (@(& $findUpdaters).Count -gt 0) {
        throw 'Discord started updating while closing. Wait for the updater to finish and rerun.'
    }
    Close-AppGroup 'Proton VPN client' { Get-ProtonAppProcesses }
    Assert-AppsClosed $Roots
    Write-Host 'Shutdown verified. Discord and the Proton client are closed before writing settings.'
}

function Assert-AppsClosed {
    param([string[]]$Roots)
    $currentSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $clients = @(Get-Process -Name 'ProtonVPN.Client', 'ProtonVPN' -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $currentSession -and -not (Test-ProcessHasExited $_) })
    if ($clients.Count -gt 0) {
        throw 'Proton VPN is open. Use -Apply -CloseApps, or exit it from its tray icon and rerun.'
    }
    $names = @('Discord', 'DiscordPTB', 'DiscordCanary', 'Update', 'updater_launcher',
        'DiscordHookHelper', 'DiscordHookHelper64', 'DiscordSystemHelper', 'audio_effects_helper', 'gpu_encoder_helper')
    if ($Roots.Count -gt 0) {
        $names += @(Get-DiscordInventory $Roots | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Path) })
    }
    $names = @($names | Sort-Object -Unique)
    $candidates = @(Get-Process -Name $names -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $currentSession })
    foreach ($process in $candidates) {
        if (Test-ProcessHasExited $process) { continue }
        $path = $null
        try { $path = $process.Path } catch { }
        if ((Test-ManagedPath $path $Roots) -or
            ($process.ProcessName -match '^Discord(PTB|Canary)?$')) {
            throw 'Discord or its updater is running. Quit Discord from its tray icon and wait for its updater to finish.'
        }
        if (-not $path) {
            throw 'Cannot verify the path of a possible Discord helper or updater. Close that process manually or wait for it to finish.'
        }
    }
}

function Write-DiscordRulePlan {
    param([object]$Snapshot, [object]$Plan, [string[]]$Roots, [object[]]$Inventory)
    if (-not $Plan.Changed) { return $null }
    Assert-AppsClosed $Roots
    if ((Get-InventorySignature $Inventory) -cne
        (Get-InventorySignature @(Get-DiscordInventory $Roots))) {
        throw 'Discord files changed during the scan. Wait for the update to finish and rerun.'
    }
    $currentBytes = [IO.File]::ReadAllBytes($Snapshot.Path)
    if ([Convert]::ToBase64String($currentBytes) -cne [Convert]::ToBase64String($Snapshot.Bytes)) {
        throw 'Proton settings changed during the scan. Rerun to preserve the newer settings.'
    }
    $folder = [IO.Path]::GetDirectoryName($Snapshot.Path)
    $backupFolder = Join-Path $folder 'DiscordRuleBackups'
    [void][IO.Directory]::CreateDirectory($backupFolder)
    $id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
    $backup = Join-Path $backupFolder ('settings-' + $id + '.json')
    $temporary = Join-Path $folder ('.discord-rules-' + $id + '.tmp')
    try {
        [IO.File]::WriteAllBytes($temporary, $Plan.Bytes)
        # Reparse the candidate before the atomic replacement.
        $verified = Read-ProtonSettings $temporary
        if ($verified.Apps.Count -lt $Plan.DesiredPaths.Count) { throw 'Candidate validation failed.' }
        Assert-AppsClosed $Roots
        if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($Snapshot.Path)) -cne
            [Convert]::ToBase64String($Snapshot.Bytes)) {
            throw 'Proton settings changed before replacement. Rerun.'
        }
        [IO.File]::Replace($temporary, $Snapshot.Path, $backup)
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
    return $backup
}

function Get-ProtonClientPath {
    param([string]$baseFolder = (Join-Path $env:ProgramFiles 'Proton\VPN'))
    $versions = @(Get-ChildItem -LiteralPath $baseFolder -Directory | Where-Object {
        $_.Name -match '^v\d+(\.\d+){1,3}$' -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'ProtonVPN.Client.exe') -PathType Leaf)
    } | Sort-Object { [version]$_.Name.Substring(1) } -Descending)
    if ($versions.Count -eq 0) { throw 'Cannot locate ProtonVPN.Client.exe. Open Proton manually.' }
    return Join-Path $versions[0].FullName 'ProtonVPN.Client.exe'
}

function Get-ProtonStorageFolders {
    # Prefer the current layout. Alternate named Storage folders are checked
    # only if the current folder contains no account files; no drive-wide scan.
    return @(
        (Join-Path $env:LOCALAPPDATA 'Proton\Proton VPN\Storage'),
        (Join-Path $env:LOCALAPPDATA 'Proton\VPN\Storage'),
        (Join-Path $env:LOCALAPPDATA 'ProtonVPN\Storage')
    )
}

function Find-ProtonSettingsFiles {
    $folders = @(Get-ProtonStorageFolders)
    $fallbackFiles = @()
    for ($folderIndex = 0; $folderIndex -lt $folders.Count; $folderIndex++) {
        $folder = $folders[$folderIndex]
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
        $files = @(Get-ChildItem -LiteralPath $folder -Filter 'UserSettings*.json' -File -ErrorAction Stop)
        if ($folderIndex -eq 0 -and $files.Count -gt 0) { return $files }
        $fallbackFiles += $files
    }
    return @($fallbackFiles | Sort-Object FullName -Unique)
}

function Resolve-ProtonSettingsPath {
    param([string]$ExplicitPath, [switch]$AllowSelection)
    if ($ExplicitPath) { return (Get-Item -LiteralPath $ExplicitPath -Force -ErrorAction Stop).FullName }
    $candidates = @(Find-ProtonSettingsFiles | Sort-Object LastWriteTimeUtc -Descending)
    if ($candidates.Count -eq 0) {
        $locations = @(Get-ProtonStorageFolders) -join '; '
        throw ("No Proton account settings files were found. Open Proton VPN, sign in, enable Include mode, and Apply. " +
            "Run Diagnose Proton settings.cmd if this continues. Checked: " + $locations)
    }
    if ($candidates.Count -eq 1) { return $candidates[0].FullName }
    if (-not $AllowSelection) {
        throw ("Found $($candidates.Count) Proton account settings files. Run with -ChooseAccount to select one, " +
            'or use -SettingsPath. No account was chosen automatically.')
    }
    Write-Host ("Found $($candidates.Count) Proton account settings files.")
    Write-Host 'Choose the account file you use. Newest saved files are listed first.'
    Write-Host 'If unsure: cancel, open Proton, Apply your Include settings, and rerun; compare the saved time.'
    for ($index = 0; $index -lt $candidates.Count; $index++) {
        $candidate = $candidates[$index]
        $status = 'Needs Include setup or unsupported format'
        try { [void](Read-ProtonSettings $candidate.FullName); $status = 'Include mode ready' } catch { }
        Write-Host ('[{0}] {1} | Saved {2} | {3}' -f ($index + 1), $candidate.Name,
            $candidate.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $status)
        Write-Host ('    ' + $candidate.DirectoryName)
    }
    try { $answer = Read-Host ('Enter 1-' + $candidates.Count + ', or press Enter to cancel') }
    catch { throw 'Account selection needs an interactive console. Use -SettingsPath to select the account file explicitly.' }
    if ([string]::IsNullOrWhiteSpace($answer)) { throw 'Account selection cancelled. No apps were closed and no settings were changed.' }
    [int]$number = 0
    if (-not [int]::TryParse($answer, [ref]$number) -or $number -lt 1 -or $number -gt $candidates.Count) {
        throw 'Invalid account selection. Rerun and enter one of the displayed numbers.'
    }
    return $candidates[$number - 1].FullName
}

function Show-ProtonSettingsDiagnostic {
    Write-Host 'Proton settings diagnostic - read only; no apps will be closed.'
    Write-Host ('Windows user: ' + $env:USERNAME)
    Write-Host ('Local app data: ' + $env:LOCALAPPDATA)
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    Write-Host ('Script Windows session: ' + $session)
    Write-Host 'Running Discord / Proton processes in this Windows session:'
    $visibleProcesses = @(Get-Process -Name 'Discord','DiscordPTB','DiscordCanary','ProtonVPN.Client','ProtonVPN' -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $session })
    if ($visibleProcesses.Count -eq 0) { Write-Host '  None found.' }
    foreach ($process in $visibleProcesses) {
        $path = $null
        try { $path = $process.Path } catch { }
        if (-not $path) { $path = 'Image path unreadable; automatic shutdown cannot safely target this process.' }
        Write-Host ('  {0} | PID {1} | {2}' -f $process.ProcessName,$process.Id,$path)
    }
    try { Write-Host ('Proton client: ' + (Get-ProtonClientPath)) }
    catch { Write-Host 'Proton client: not found in the standard installation folder.' }
    foreach ($folder in @(Get-ProtonStorageFolders)) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
            Write-Host ('Storage folder not found: ' + $folder)
            continue
        }
        $files = @(Get-ChildItem -LiteralPath $folder -Filter 'UserSettings*.json' -File -ErrorAction Stop)
        Write-Host ('Storage: ' + $folder)
        Write-Host ('Account settings files found: ' + $files.Count)
        foreach ($file in $files) {
            $status = 'Include mode ready'
            try { [void](Read-ProtonSettings $file.FullName) }
            catch {
                if ($_.Exception.Message -like '*Include mode*') { $status = 'Include mode is not enabled' }
                else { $status = 'Unsupported or unreadable settings layout' }
            }
            Write-Host ('  {0} | Saved {1} | {2}' -f $file.Name,
                $file.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $status)
        }
        if ($files.Count -eq 0) {
            Write-Host '  JSON filenames in this folder (contents are not shown):'
            Get-ChildItem -LiteralPath $folder -Filter '*.json' -File | ForEach-Object { Write-Host ('  ' + $_.Name) }
        }
    }
    Write-Host 'Discord installation folders and executable scan:'
    try {
        $roots = @(Resolve-DiscordRoots)
        foreach ($root in $roots) { Write-Host ('  Folder: ' + $root) }
        $inventory = @(Get-DiscordInventory $roots)
        Write-Host ('Discord executables found: ' + $inventory.Count)
        foreach ($file in $inventory) { Write-Host ('  ' + $file.Path) }
    } catch { Write-Host ('Discord scan stopped: ' + $_.Exception.Message) }
    Write-Host 'This output contains file paths/identifiers; it does not show passwords, tokens, or settings contents.'
}

function Invoke-DiscordUpdater {
    param([string]$RequestedSettingsPath, [string[]]$RequestedRoots, [switch]$WriteSettings,
        [switch]$LaunchProton, [switch]$ExitApps, [switch]$SelectAccount)
    if ($LaunchProton -and -not $WriteSettings) { throw '-OpenProton requires -Apply.' }
    if ($ExitApps -and -not $WriteSettings) { throw '-CloseApps requires -Apply; preview never closes applications.' }
    Set-RefreshStage 'Selecting Proton account (apps stay open until selection is complete)'
    $RequestedSettingsPath = Resolve-ProtonSettingsPath $RequestedSettingsPath -AllowSelection:$SelectAccount
    Write-Host ('Selected settings file: ' + $RequestedSettingsPath)
    Set-RefreshStage 'Checking Discord installation and Include mode'
    $roots = @(Resolve-DiscordRoots $RequestedRoots)
    foreach ($root in $roots) { Write-Host ('Discord scan folder: ' + $root) }
    $inventory = @(Get-DiscordInventory $roots)
    $snapshot = Read-ProtonSettings $RequestedSettingsPath
    $plan = New-DiscordRulePlan $snapshot $roots $inventory
    $client = $null
    if ($LaunchProton) { $client = Get-ProtonClientPath }
    if ($ExitApps) {
        Set-RefreshStage 'Closing Discord and Proton client'
        try { Close-DiscordAndProton $roots $inventory }
        catch {
            $shutdownError = $_
            # Restore the client if shutdown partially succeeded. Discord stays
            # closed so it cannot resume before the user reconnects the VPN.
            try {
                if ($LaunchProton -and @(Get-ProtonAppProcesses).Count -eq 0) {
                    Start-Process -FilePath $client -WorkingDirectory ([IO.Path]::GetDirectoryName($client)) -WindowStyle Hidden
                    Write-Host 'Refresh cancelled. Proton reopened; reconnect before reopening Discord.'
                }
            } catch { Write-Warning 'Could not reopen Proton automatically. Open it manually.' }
            throw $shutdownError
        }
    }
    try {
        if ($WriteSettings) {
            Set-RefreshStage 'Refreshing Included apps and reopening Proton'
            Assert-AppsClosed $roots
            # Shutdown may have saved newer preferences or an updater may have
            # created a new app folder. Build the final plan after app exit.
            $inventory = @(Get-DiscordInventory $roots)
            $snapshot = Read-ProtonSettings $RequestedSettingsPath
            $plan = New-DiscordRulePlan $snapshot $roots $inventory
        }
    Write-Host ('Discord executables: ' + $plan.DesiredPaths.Count)
    Write-Host ('Other entries preserved: ' + $plan.PreservedCount)
    foreach ($path in $plan.Added) { Write-Host ('ADD    ' + $path) }
    foreach ($path in $plan.Removed) { Write-Host ('REMOVE ' + $path) }
    if ($plan.Reactivated -gt 0) { Write-Host ('Enable current Discord entries: ' + $plan.Reactivated) }
    if (-not $WriteSettings) {
        if ($plan.Changed) { Write-Host 'Preview only. No settings changed.' }
        else { Write-Host 'Already current. No settings changed.' }
        return
    }
    Assert-AppsClosed $roots
    $backup = Write-DiscordRulePlan $snapshot $plan $roots $inventory
    if ($backup) {
        Write-Host 'Discord Included apps refreshed.'
        Write-Host ('Exact backup: ' + $backup)
    } else { Write-Host 'Already current. No backup or write needed.' }
        Write-Host ('Refresh completed. Discord executable rules: ' + $plan.DesiredPaths.Count)
        if (-not $LaunchProton) { Write-Host 'Open Proton and reconnect to load the refreshed rules.' }
    } finally {
        if ($LaunchProton -and @(Get-ProtonAppProcesses).Count -eq 0) {
            Start-Process -FilePath $client -WorkingDirectory ([IO.Path]::GetDirectoryName($client)) -WindowStyle Hidden
            Write-Host 'Proton reopened. Reconnect the VPN and verify the Included apps before reopening Discord.'
        }
    }
}

# Dot-sourcing exposes the functions for offline tests; it does not run the updater.
if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($Diagnose) {
            if ($Apply -or $CloseApps -or $OpenProton) { throw '-Diagnose cannot be combined with app-close, apply, or reopen options.' }
            Show-ProtonSettingsDiagnostic
            exit 0
        }
        Invoke-DiscordUpdater -RequestedSettingsPath $SettingsPath -RequestedRoots $DiscordRoot `
            -WriteSettings:$Apply -LaunchProton:$OpenProton -ExitApps:$CloseApps -SelectAccount:$ChooseAccount
    } catch {
        Write-Host ('REFRESH FAILED during: ' + $script:RefreshStage)
        Write-Error $_.Exception.Message -ErrorAction Continue
        exit 1
    }
}
