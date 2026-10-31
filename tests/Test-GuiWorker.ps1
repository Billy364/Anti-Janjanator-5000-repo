#requires -Version 5.1
param([string]$WorkerPath,[string]$RunFolder)
. $WorkerPath
[void][IO.Directory]::CreateDirectory($RunFolder)
$script:fakeProcesses = @()
$script:stopped = @()
$script:readCount = 0
$script:injectOnRead = $false
$script:launches = @(); $script:failLaunch = $false; $script:failResolve = $false; $script:launchAccount = $null
$script:originalLaunchResolver = ${function:Get-ProtonLaunchPath}
$script:originalReader = ${function:Read-ProtonSettings}
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
function global:Get-Process {
    [CmdletBinding()]param([string[]]$Name)
    $script:fakeProcesses | Where-Object { $Name -contains $_.ProcessName }
}
function global:Stop-Process {
    [CmdletBinding()]param([object]$InputObject,[switch]$Force)
    $script:stopped += $InputObject.Id
    $script:fakeProcesses = @($script:fakeProcesses | Where-Object { $_.Id -ne $InputObject.Id })
}
function Get-ProtonLaunchPath { if($script:failResolve) { throw 'Synthetic Proton installation lookup failed.' }; return $protonPath }
function global:Start-Process {
    [CmdletBinding()]param([string]$FilePath,[string]$WorkingDirectory,[string]$WindowStyle)
    if($FilePath -cne $protonPath -or $WorkingDirectory -cne [IO.Path]::GetDirectoryName($protonPath) -or $WindowStyle -ne 'Normal') { throw 'Unexpected app launch or inherited working directory.' }
    if($script:launchAccount) {
        $saved=[IO.File]::ReadAllText($script:launchAccount)|ConvertFrom-Json
        if($saved.IsSplitTunnelingEnabled -cne 'true' -or $saved.SplitTunnelingMode -cne 'Inverse' -or @($saved.SplitTunnelingInverseAppsList|ConvertFrom-Json).Count -eq 0) { throw 'Proton was launched before the rules and activation were saved.' }
    }
    $script:launches += [pscustomobject]@{Path=$FilePath;WorkingDirectory=$WorkingDirectory;WindowStyle=$WindowStyle}
    if($script:failLaunch) { throw 'Synthetic Proton launch failure.' }
}
function Wait-AppGroupExit { param([scriptblock]$FindProcesses,[int]$TimeoutSeconds) return (@(& $FindProcesses).Count -eq 0) }
function Read-ProtonSettings {
    param([string]$Path,[switch]$EnableSplitTunneling)
    $script:readCount++
    $snapshot = & $script:originalReader -Path $Path -EnableSplitTunneling:$EnableSplitTunneling
    if ($script:injectOnRead) { $script:fakeProcesses = @((New-FakeProcess 99 $protonPath)); $script:injectOnRead = $false }
    return $snapshot
}
function Assert-True { param([bool]$Condition,[string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Rejected { param([scriptblock]$Action,[string]$Text) $launchCount=$script:launches.Count; $errorText=''; try { & $Action } catch { $errorText=$_.Exception.Message }; Assert-True ($errorText -like ('*'+$Text+'*')) ('Expected rejection: '+$Text+'; got '+$errorText); Assert-True ($script:launches.Count -eq $launchCount) 'Rejected operation launched an app.' }
function New-FakeProcess {
    param([int]$Id,[string]$Path)
    $process = [pscustomobject]@{ Id=$Id; Path=$Path; ProcessName=[IO.Path]::GetFileNameWithoutExtension($Path); SessionId=$session }
    $process | Add-Member ScriptMethod CloseMainWindow { return $false }
    return $process
}
$root = Join-Path $RunFolder 'Discord'
[void][IO.Directory]::CreateDirectory((Join-Path $root 'app-1.0.2\modules'))
$main = Join-Path $root 'app-1.0.2\Discord.exe'
$helper = Join-Path $root 'app-1.0.2\modules\extra_helper.exe'
foreach ($file in @($main,$helper)) { [IO.File]::WriteAllText($file,'offline fixture; never executed') }
$protonPath = Join-Path $env:ProgramFiles 'Proton\VPN\v5.1.9\ProtonVPN.Client.exe'
$entries = @(
    [pscustomobject]@{ AppFilePath='C:\Other\Browser.exe';AlternateAppFilePaths=@();IsActive=$false },
    [pscustomobject]@{ AppFilePath=(Join-Path $root 'app-1.0.1\Discord.exe');AlternateAppFilePaths=@();IsActive=$true }
)
$settings = [ordered]@{ KeepMe='same'; SplitTunnelingInverseAppsList=(ConvertTo-Json -InputObject $entries -Compress); IsSplitTunnelingEnabled='true'; SplitTunnelingMode='Inverse' }
$file = Join-Path $RunFolder 'UserSettings.fixture.json'
[IO.File]::WriteAllText($file,(ConvertTo-Json $settings))
$request = [pscustomobject]@{ Action='Refresh'; SettingsPath=$file; Roots=@($root) }
$originalHash = (Get-FileHash -LiteralPath $file).Hash
foreach ($path in @($main,$protonPath)) {
    $script:fakeProcesses=@((New-FakeProcess 1 $path))
    Assert-Rejected { Invoke-InterfaceOperation $request } 'running'
    Assert-True ((Get-FileHash -LiteralPath $file).Hash -eq $originalHash) 'Running-app rejection wrote settings.'
}
Write-Output 'PASS: Direct refresh refuses either running desktop app, independent of the UI button.'
$script:fakeProcesses=@((New-FakeProcess 2 $helper))
Assert-Rejected { Invoke-InterfaceOperation $request } 'running'
Write-Output 'PASS: A standalone helper with an inventory-derived name blocks refresh.'
$script:fakeProcesses=@();$script:injectOnRead=$true
Assert-Rejected { Invoke-InterfaceOperation $request } 'running'
Assert-True ((Get-FileHash -LiteralPath $file).Hash -eq $originalHash) 'Late relaunch was not blocked.'
Write-Output 'PASS: An app relaunching after the settings read blocks the write.'
$script:fakeProcesses=@()
$result=Invoke-InterfaceOperation $request
Assert-True ($result.Success -and $result.Total -eq 2 -and $result.Added.Count -eq 2 -and $result.Removed.Count -eq 1) 'Unexpected refresh result.'
Assert-True ($result.ProtonLaunchStatus -eq 'Started' -and $script:launches.Count -eq 1) 'Successful refresh did not request one Proton launch.'
Assert-True ((Get-FileHash -LiteralPath $result.BackupPath).Hash -eq $originalHash) 'Backup differs from original.'
$after = & $script:originalReader $file
Assert-True (@($after.Apps | Where-Object { $_.AppFilePath -eq 'C:\Other\Browser.exe' -and -not $_.IsActive }).Count -eq 1) 'Other app changed.'
Assert-True (([IO.File]::ReadAllText($file) | ConvertFrom-Json).KeepMe -eq 'same') 'Other preference changed.'
Write-Output 'PASS: Refresh includes main/helper executables, removes stale paths, preserves other apps/preferences, and makes an exact backup.'
$hash=(Get-FileHash -LiteralPath $file).Hash
$launchCount=$script:launches.Count
$repeat=Invoke-InterfaceOperation $request
Assert-True ($repeat.Success -and $null -eq $repeat.BackupPath -and (Get-FileHash -LiteralPath $file).Hash -eq $hash) 'Repeat refresh was not a no-op.'
Assert-True ($repeat.ProtonLaunchStatus -eq 'Started' -and $script:launches.Count -eq ($launchCount+1)) 'Already-current rules did not launch Proton.'
Write-Output 'PASS: Successful and already-current refreshes request Proton once; no-op rules still cause no settings write/backup.'
$script:fakeProcesses=@((New-FakeProcess 10 $main),(New-FakeProcess 11 $helper),(New-FakeProcess 12 $protonPath))
$readsBefore=$script:readCount
$launchCount=$script:launches.Count
$closed=Invoke-InterfaceOperation ([pscustomobject]@{Action='Close';SettingsPath=$null;Roots=@($root)})
Assert-True ($closed.Success -and $script:fakeProcesses.Count -eq 0 -and $script:stopped.Count -eq 3) 'Close did not exit all expected app processes.'
Assert-True ($script:readCount -eq $readsBefore -and (Get-FileHash -LiteralPath $file).Hash -eq $hash) 'Close depended on account selection or changed settings.'
Assert-True ($script:launches.Count -eq $launchCount) 'Close reopened Proton.'
Write-Output 'PASS: Close exits both apps and helpers without an account selection, and does not refresh settings.'
$other=New-FakeProcess 20 $protonPath;$other.SessionId=$session+1;$script:fakeProcesses=@($other)
$differentSession=Invoke-InterfaceOperation $request
Assert-True ($differentSession.Success) 'Another Windows session blocked the current user account.'
Write-Output 'PASS: Another Windows session is not targeted or treated as the current user app.'
$goneDiscord = New-FakeProcess 30 $main; $goneDiscord.Path=$null; $goneDiscord | Add-Member NoteProperty HasExited $true
$goneProton = New-FakeProcess 31 $protonPath; $goneProton.Path=$null; $goneProton | Add-Member NoteProperty HasExited $true
$script:fakeProcesses=@($goneDiscord,$goneProton)
Assert-True (@(Get-DiscordAppProcesses @($root) @(Get-DiscordInventory @($root))).Count -eq 0) 'Exited Discord record caused an image-path error.'
Assert-True (@(Get-ProtonAppProcesses).Count -eq 0) 'Exited Proton record caused an image-path error.'
Assert-DesktopAppsExited @($root)
Write-Output 'PASS: Processes that exit between enumeration and image lookup are treated as exited, not as access failures.'

$script:fakeProcesses=@()
foreach ($kind in @('missing','null')) {
    $sparseFile=Join-Path $RunFolder ('UserSettings.'+$kind+'.json')
    $sparseText='{"KeepMe"  :  "keep\u0020exact","IsSplitTunnelingEnabled":"true","SplitTunnelingMode":"Inverse"}'
    if($kind -eq 'null') { $sparseText=$sparseText.Insert($sparseText.Length-1, ',"SplitTunnelingInverseAppsList":null') }
    [IO.File]::WriteAllText($sparseFile,$sparseText,(New-Object Text.UTF8Encoding($true)))
    $before=[IO.File]::ReadAllBytes($sparseFile)
    $sparseResult=Invoke-InterfaceOperation ([pscustomobject]@{Action='Refresh';SettingsPath=$sparseFile;Roots=@($root)})
    Assert-True ($sparseResult.Success -and $sparseResult.Total -eq 2) 'Omitted/null list refresh failed.'
    Assert-True ([Convert]::ToBase64String([IO.File]::ReadAllBytes($sparseResult.BackupPath)) -ceq [Convert]::ToBase64String($before)) 'Sparse settings backup differs.'
    $newText=[IO.File]::ReadAllText($sparseFile)
    Assert-True ($newText.Contains('"KeepMe"  :  "keep\u0020exact"') -and [IO.File]::ReadAllBytes($sparseFile)[0] -eq 239) 'Sparse refresh changed unrelated bytes or BOM.'
    $second=Invoke-InterfaceOperation ([pscustomobject]@{Action='Refresh';SettingsPath=$sparseFile;Roots=@($root)})
    Assert-True ($second.Success -and $null -eq $second.BackupPath) 'Second sparse refresh was not a no-op.'
}
Write-Output 'PASS: Missing/null Included app lists are created with exact backups, preserved bytes/BOM, and a repeat no-op.'

foreach ($minimal in @('{"IsUserSettingsMigrationDone":"true"}','{"IsSplitTunnelingEnabled":"true"}')) {
    $minimalFile=Join-Path $RunFolder 'UserSettings.defaults.json';[IO.File]::WriteAllText($minimalFile,$minimal)
    Assert-Rejected { & $script:originalReader $minimalFile } 'Enable Split tunneling in Include mode'
    Assert-True ([IO.File]::ReadAllText($minimalFile) -ceq $minimal) 'Default off/Standard mode was silently enabled.'
}
Write-Output 'PASS: Omitted off/Standard defaults produce setup guidance without changing VPN mode.'

$duplicateFile=Join-Path $RunFolder 'UserSettings.duplicate.json'
[IO.File]::WriteAllText($duplicateFile,'{"IsSplitTunnelingEnabled":"true","SplitTunnelingMode":"Inverse","SplitTunnelingInverseAppsList":"[]","SplitTunnelingInverseAppsList":"[]"}')
Assert-Rejected { & $script:originalReader $duplicateFile } 'Duplicate or ambiguous'
Write-Output 'PASS: Duplicate settings properties remain rejected.'

# The GUI opts into activation; the reader's legacy CLI default stays read-only
# with respect to the VPN mode. Flags and rules must be committed together.
foreach ($activationMode in @('missing','off','exclude','null')) {
    $activationFile=Join-Path $RunFolder ('UserSettings.auto-'+$activationMode+'.json')
    $activationSettings=[ordered]@{
        IsUserSettingsMigrationDone='true'; IsAutoConnectEnabled='false'; VpnProtocol='Smart'
        SplitTunnelingStandardAppsList='[{"AppFilePath":"C:\\Other\\Keep.exe","AlternateAppFilePaths":[],"IsActive":true}]'
        SplitTunnelingInverseIpAddressesList='[{"IpAddress":"192.0.2.1","IsActive":false}]'
    }
    if ($activationMode -ne 'missing') {
        $activationSettings['IsSplitTunnelingEnabled']=if($activationMode -eq 'null'){$null}elseif($activationMode -eq 'exclude'){'true'}else{'false'}
        $activationSettings['SplitTunnelingMode']=if($activationMode -eq 'null'){$null}else{'Standard'}
        $activationSettings['SplitTunnelingInverseAppsList']=if($activationMode -eq 'null'){$null}else{'[{"AppFilePath":"C:\\Other\\Browser.exe","AlternateAppFilePaths":[],"IsActive":false}]'}
    }
    $activationText=ConvertTo-Json $activationSettings -Compress
    $activationText=$activationText.Insert($activationText.Length-1,',"OpaqueKeep"  :  "keep\u0020exact"')
    [IO.File]::WriteAllText($activationFile,$activationText,(New-Object Text.UTF8Encoding($true)))
    $requestAuto=[pscustomobject]@{Action='Refresh';SettingsPath=$activationFile;Roots=@($root)}
    $before=[IO.File]::ReadAllBytes($activationFile)
    $backupCount=@(Get-ChildItem -LiteralPath (Join-Path $RunFolder 'DiscordRuleBackups') -File).Count
    $enabledResult=Invoke-InterfaceOperation $requestAuto
    $afterAuto=[IO.File]::ReadAllText($activationFile)|ConvertFrom-Json
    Assert-True ($enabledResult.Success -and $enabledResult.EnabledSplitTunneling -and $afterAuto.IsSplitTunnelingEnabled -ceq 'true' -and $afterAuto.SplitTunnelingMode -ceq 'Inverse') 'Refresh did not activate Include mode.'
    Assert-True ($afterAuto.IsAutoConnectEnabled -ceq 'false' -and $afterAuto.VpnProtocol -ceq 'Smart' -and $afterAuto.SplitTunnelingStandardAppsList -ceq $activationSettings.SplitTunnelingStandardAppsList -and $afterAuto.SplitTunnelingInverseIpAddressesList -ceq $activationSettings.SplitTunnelingInverseIpAddressesList) 'Activation changed an unrelated preference/list.'
    Assert-True ([IO.File]::ReadAllText($activationFile).Contains('"OpaqueKeep"  :  "keep\u0020exact"') -and [IO.File]::ReadAllBytes($activationFile)[0] -eq 239) 'Activation rewrote unrelated bytes/BOM.'
    Assert-True ([Convert]::ToBase64String([IO.File]::ReadAllBytes($enabledResult.BackupPath)) -ceq [Convert]::ToBase64String($before) -and @(Get-ChildItem -LiteralPath (Join-Path $RunFolder 'DiscordRuleBackups') -File).Count -eq ($backupCount+1)) 'Activation/rules did not use one exact backup.'
    $repeatAuto=Invoke-InterfaceOperation $requestAuto
    Assert-True ($null -eq $repeatAuto.BackupPath -and -not $repeatAuto.EnabledSplitTunneling) 'Repeated activation did not become a no-op.'
}
Write-Output 'PASS: GUI Refresh enables missing/off/Exclude/null settings with rules in one write, preserves preferences/lists/opaque bytes/BOM, and uses one exact backup.'

# Leave the rule list current and switch only its activation state off.
$flagOnly=[IO.File]::ReadAllText($activationFile)|ConvertFrom-Json
$flagOnly.IsSplitTunnelingEnabled='false';$flagOnly.SplitTunnelingMode='Standard'
[IO.File]::WriteAllText($activationFile,(ConvertTo-Json $flagOnly -Depth 30))
$flagBefore=[IO.File]::ReadAllBytes($activationFile)
$flagResult=Invoke-InterfaceOperation $requestAuto
Assert-True ($flagResult.EnabledSplitTunneling -and $flagResult.Added.Count -eq 0 -and $flagResult.Removed.Count -eq 0 -and $null -ne $flagResult.BackupPath) 'Current rules prevented activation-only write.'
Assert-True ([Convert]::ToBase64String([IO.File]::ReadAllBytes($flagResult.BackupPath)) -ceq [Convert]::ToBase64String($flagBefore)) 'Activation-only backup is not exact.'
Write-Output 'PASS: Already-current Discord rules still allow a backed-up activation-only change.'

$guardFile=Join-Path $RunFolder 'UserSettings.guarded.json'
$guardText='{"IsUserSettingsMigrationDone":"true","IsSplitTunnelingEnabled":"false","SplitTunnelingMode":"Standard"}'
$guardRequest=[pscustomobject]@{Action='Refresh';SettingsPath=$guardFile;Roots=@($root)}
foreach($path in @($main,$protonPath)) {
    [IO.File]::WriteAllText($guardFile,$guardText);$script:fakeProcesses=@((New-FakeProcess 77 $path))
    Assert-Rejected { Invoke-InterfaceOperation $guardRequest } 'running'
    Assert-True ([IO.File]::ReadAllText($guardFile) -ceq $guardText) 'Running-app rejection partially enabled split tunneling.'
}
$script:fakeProcesses=@();$script:injectOnRead=$true
Assert-Rejected { Invoke-InterfaceOperation $guardRequest } 'running'
Assert-True ([IO.File]::ReadAllText($guardFile) -ceq $guardText) 'Late restart partially enabled split tunneling.'
$script:fakeProcesses=@()
$guardSnapshot=& $script:originalReader -Path $guardFile -EnableSplitTunneling
$guardInventory=@(Get-DiscordInventory @($root));$guardPlan=New-DiscordRulePlan $guardSnapshot @($root) $guardInventory
$newerText=$guardText.Insert($guardText.Length-1,',"NewerPreference":"preserve"');[IO.File]::WriteAllText($guardFile,$newerText)
Assert-Rejected { Write-DiscordRulePlan $guardSnapshot $guardPlan @($root) $guardInventory } 'settings changed'
Assert-True ([IO.File]::ReadAllText($guardFile) -ceq $newerText) 'Concurrent edit was overwritten during activation.'
Write-Output 'PASS: Running apps, late restarts, and concurrent settings edits prevent both activation and rule writes.'

foreach($invalid in @('{"unrelated":"file"}','{"IsSplitTunnelingEnabled":true}','{"IsSplitTunnelingEnabled":"false","SplitTunnelingInverseAppsList":"[42]"}')) {
    [IO.File]::WriteAllText($guardFile,$invalid)
    Assert-Rejected { Invoke-InterfaceOperation $guardRequest } 'Unsupported'
    Assert-True ([IO.File]::ReadAllText($guardFile) -ceq $invalid) 'Unsupported settings partially activated split tunneling.'
}
Write-Output 'PASS: Automatic activation does not bypass unknown-format or invalid-list checks.'

# Launch failures happen after the saved settings transaction. Keep success and
# its backup, but report that the user must open Proton manually.
$launchFile=Join-Path $RunFolder 'UserSettings.launch.json';$script:launchAccount=$launchFile
[IO.File]::WriteAllText($launchFile,'{"IsUserSettingsMigrationDone":"true","IsAutoConnectEnabled":"false"}')
$launchRequest=[pscustomobject]@{Action='Refresh';SettingsPath=$launchFile;Roots=@($root)}
$script:failLaunch=$true
$failedLaunch=Invoke-InterfaceOperation $launchRequest
Assert-True ($failedLaunch.Success -and $failedLaunch.ProtonLaunchStatus -eq 'Failed' -and $failedLaunch.ProtonLaunchWarning -like '*Synthetic Proton launch failure*' -and $null -ne $failedLaunch.BackupPath) 'Launch failure incorrectly reported a failed settings transaction or lost its warning/backup.'
$saved= & $script:originalReader $launchFile
Assert-True ($saved.Apps.Count -eq 2 -and ([IO.File]::ReadAllText($launchFile)|ConvertFrom-Json).IsAutoConnectEnabled -ceq 'false') 'Launch failure rolled back rules or altered auto-connect.'
$script:failLaunch=$false;$script:failResolve=$true;$launchCount=$script:launches.Count
$missingInstall=Invoke-InterfaceOperation $launchRequest
Assert-True ($missingInstall.Success -and $missingInstall.ProtonLaunchStatus -eq 'Failed' -and $missingInstall.ProtonLaunchWarning -like '*lookup failed*' -and $script:launches.Count -eq $launchCount -and $null -eq $missingInstall.BackupPath) 'Missing installation was not reported independently of no-op settings success.'
$script:failResolve=$false
$successLaunch=Invoke-InterfaceOperation $launchRequest
Assert-True ($successLaunch.Success -and $successLaunch.ProtonLaunchStatus -eq 'Started') 'Launch could not recover after a failure.'
Write-Output 'PASS: Launch is requested only after saved activation/rules; launch/lookup failures keep successful settings and backups, and a later retry can launch.'

$script:fakeProcesses=@((New-FakeProcess 88 $protonPath));$launchCount=$script:launches.Count
$alreadyRunning=Start-ProtonAfterRefresh
Assert-True ($alreadyRunning -eq 'AlreadyRunning' -and $script:launches.Count -eq $launchCount) 'An existing Proton client was launched twice.'
$script:fakeProcesses=@();$script:launchAccount=$null
Write-Output 'PASS: A client that has already restarted is not launched again.'

$installRoot=Join-Path $RunFolder 'Proton install'
foreach($version in @('v5.1.8','v5.1.9')) {
    $folder=Join-Path $installRoot $version;[void][IO.Directory]::CreateDirectory($folder)
    [IO.File]::WriteAllText((Join-Path $folder 'ProtonVPN.Client.exe'),'synthetic fixture; never executed')
}
$fallback=& $script:originalLaunchResolver -InstallRoot $installRoot
Assert-True ($fallback -ceq (Join-Path $installRoot 'v5.1.9\ProtonVPN.Client.exe')) 'Versioned client fallback did not select the newest install.'
$launcher=Join-Path $installRoot 'ProtonVPN.Launcher.exe';[IO.File]::WriteAllText($launcher,'synthetic fixture; never executed')
$preferred=& $script:originalLaunchResolver -InstallRoot $installRoot
Assert-True ($preferred -ceq $launcher) 'Stable installed Proton launcher was not preferred.'
Write-Output 'PASS: Launch discovery prefers the stable Proton launcher and falls back to the newest versioned client; working-directory checks prevent portable-folder locks.'
