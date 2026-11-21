#requires -Version 5.1
param([string]$ModulePath)
. $ModulePath
$script:status = 'Protected'
$script:controls = @('ConnectionCardDisconnectButton')
$script:invocations = @()
$script:offscreen = $false
$script:hasStatus = $true
function Get-ProtonUiWindow { [pscustomobject]@{ Fixture=$true } }
function Find-ProtonUiControl {
    param([object]$Window, [string]$AutomationId)
    if ($AutomationId -eq 'ProtectionTitle' -and $script:hasStatus) {
        return [pscustomobject]@{ Current=[pscustomobject]@{ Name=$script:status } }
    }
    if ($script:controls -contains $AutomationId) {
        return [pscustomobject]@{ Current=[pscustomobject]@{ IsEnabled=$true; IsOffscreen=$script:offscreen } }
    }
}
function Invoke-ProtonUiButton {
    param([string]$AutomationId)
    $script:invocations += $AutomationId
    if ($AutomationId -in @('ConnectionCardDisconnectButton','ConnectionCardCancelButton')) {
        $script:status = 'Unprotected'; $script:controls = @('ConnectionCardConnectButton')
    } elseif ($AutomationId -eq 'ConnectionCardConnectButton') {
        $script:status = 'Protected'; $script:controls = @('ConnectionCardDisconnectButton')
    }
}
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Reject([scriptblock]$Action) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Check $failed 'An ambiguous, localized, hidden, or signed-out state was accepted.'
}
Check ((Get-ProtonUiState) -eq 'Connected') 'Protected state was not recognized.'
Disconnect-ProtonUi
Check ($script:invocations[-1] -eq 'ConnectionCardDisconnectButton' -and (Get-ProtonUiState) -eq 'Disconnected') 'Disconnect did not use the verified action.'
$count = $script:invocations.Count
Disconnect-ProtonUi
Check ($script:invocations.Count -eq $count) 'Already-disconnected state was invoked twice.'
$script:controls = @('ConnectionCardCancelButton')
Disconnect-ProtonUi
Check ($script:invocations[-1] -eq 'ConnectionCardCancelButton') 'Connecting state did not use Cancel.'
Connect-ProtonUi
Check ($script:invocations[-1] -eq 'ConnectionCardConnectButton' -and (Get-ProtonUiState) -eq 'Connected') 'Connect was not acknowledged.'
$script:status = 'Translated status'
Reject { Get-ProtonUiState }
$script:status = 'Protected'; $script:offscreen = $true
Reject { Get-ProtonUiState }
$script:offscreen = $false; $script:hasStatus = $false
Reject { Get-ProtonUiState }
Write-Output 'PASS: UI state recognition requires visible matching controls, uses Cancel while connecting, handles idempotent disconnect, and rejects unknown/signed-out states.'
