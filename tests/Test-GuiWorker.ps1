#requires -Version 5.1
param([string]$WorkerPath,[string]$RunFolder)
. $WorkerPath
[void][IO.Directory]::CreateDirectory($RunFolder)
$script:fakeProcesses = @()
$script:stopped = @()
$script:readCount = 0
$script:injectOnRead = $false
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
function global:Start-Process { [CmdletBinding()]param([string]$FilePath,[string]$WorkingDirectory,[string]$WindowStyle) throw 'GUI operations must leave the real apps closed.' }
function Wait-AppGroupExit { param([scriptblock]$FindProcesses,[int]$TimeoutSeconds) return (@(& $FindProcesses).Count -eq 0) }
function Read-ProtonSettings {
    param([string]$Path)
    $script:readCount++
    $snapshot = & $script:originalReader $Path
    if ($script:injectOnRead) { $script:fakeProcesses = @((New-FakeProcess 99 $protonPath)); $script:injectOnRead = $false }
    return $snapshot
}
function Assert-True { param([bool]$Condition,[string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Rejected { param([scriptblock]$Action,[string]$Text) $errorText=''; try { & $Action } catch { $errorText=$_.Exception.Message }; Assert-True ($errorText -like ('*'+$Text+'*')) ('Expected rejection: '+$Text+'; got '+$errorText) }
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
Assert-True ((Get-FileHash -LiteralPath $result.BackupPath).Hash -eq $originalHash) 'Backup differs from original.'
$after = & $script:originalReader $file
Assert-True (@($after.Apps | Where-Object { $_.AppFilePath -eq 'C:\Other\Browser.exe' -and -not $_.IsActive }).Count -eq 1) 'Other app changed.'
Assert-True (([IO.File]::ReadAllText($file) | ConvertFrom-Json).KeepMe -eq 'same') 'Other preference changed.'
Write-Output 'PASS: Refresh includes main/helper executables, removes stale paths, preserves other apps/preferences, and makes an exact backup.'
$hash=(Get-FileHash -LiteralPath $file).Hash
$repeat=Invoke-InterfaceOperation $request
Assert-True ($repeat.Success -and $null -eq $repeat.BackupPath -and (Get-FileHash -LiteralPath $file).Hash -eq $hash) 'Repeat refresh was not a no-op.'
Write-Output 'PASS: Repeated refresh is a no-op and neither operation reopens an app.'
$script:fakeProcesses=@((New-FakeProcess 10 $main),(New-FakeProcess 11 $helper),(New-FakeProcess 12 $protonPath))
$readsBefore=$script:readCount
$closed=Invoke-InterfaceOperation ([pscustomobject]@{Action='Close';SettingsPath=$null;Roots=@($root)})
Assert-True ($closed.Success -and $script:fakeProcesses.Count -eq 0 -and $script:stopped.Count -eq 3) 'Close did not exit all expected app processes.'
Assert-True ($script:readCount -eq $readsBefore -and (Get-FileHash -LiteralPath $file).Hash -eq $hash) 'Close depended on account selection or changed settings.'
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
