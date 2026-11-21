#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# All lookups stay inside a verified Proton client in this Windows session.
# Only the known connection status is returned, never arbitrary interface text.
function Get-ProtonUiWindow {
    $clients = @(Get-ProtonAppProcesses)
    if ($clients.Count -ne 1) { throw 'Expected one Proton VPN client in this Windows session.' }
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$clients[0].Id)
    $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll(
        [Windows.Automation.TreeScope]::Children, $condition)
    $matches = @($windows | Where-Object {
        $_.Current.ControlType -eq [Windows.Automation.ControlType]::Window -and
        $_.Current.Name -eq 'Proton VPN' -and -not $_.Current.IsOffscreen
    })
    if ($matches.Count -ne 1) { throw 'Open the Proton VPN main window and resolve any dialogs, then retry.' }
    return $matches[0]
}

function Find-ProtonUiControl {
    param([object]$Window, [string]$AutomationId)
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
    $matches = $Window.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($matches.Count -gt 1) { throw ('Ambiguous Proton control: ' + $AutomationId) }
    if ($matches.Count -eq 1) { return $matches[0] }
}

function Test-ProtonUiButton {
    param([object]$Window, [string]$AutomationId)
    $button = Find-ProtonUiControl $Window $AutomationId
    return $null -ne $button -and $button.Current.IsEnabled -and -not $button.Current.IsOffscreen
}

function Invoke-ProtonUiButton {
    param([string]$AutomationId)
    $window = Get-ProtonUiWindow
    $button = Find-ProtonUiControl $window $AutomationId
    if ($null -eq $button -or -not $button.Current.IsEnabled -or $button.Current.IsOffscreen) {
        throw ('Proton control is unavailable. Resolve any dialogs and retry: ' + $AutomationId)
    }
    if ($button.Current.ControlType -notin @([Windows.Automation.ControlType]::Button, [Windows.Automation.ControlType]::MenuItem)) {
        throw ('Unexpected Proton control type: ' + $AutomationId)
    }
    $pattern = $null
    if ($button.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        ([Windows.Automation.InvokePattern]$pattern).Invoke()
    } elseif ($AutomationId -eq 'TitleBarMenuButton' -and
        $button.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) {
        ([Windows.Automation.ExpandCollapsePattern]$pattern).Expand()
    } else { throw ('Proton control has no supported action: ' + $AutomationId) }
}

function Get-ProtonUiState {
    $window = Get-ProtonUiWindow
    $status = Find-ProtonUiControl $window 'ProtectionTitle'
    if ($null -eq $status) { throw 'Open Proton Home and sign in before retrying.' }
    if (Test-ProtonUiButton $window 'ConnectionCardCancelButton') { return 'Connecting' }
    if ($status.Current.Name -eq 'Protected' -and (Test-ProtonUiButton $window 'ConnectionCardDisconnectButton')) {
        return 'Connected'
    }
    if ($status.Current.Name -eq 'Unprotected' -and (Test-ProtonUiButton $window 'ConnectionCardConnectButton')) {
        return 'Disconnected'
    }
    throw 'Unknown Proton connection state. This prototype requires the English Proton 5.1.9 Home screen.'
}

function Show-ProtonUi {
    # The installed launcher brings a tray-hidden client forward or starts it.
    Start-DesktopClient (Get-ProtonLaunchPath)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        try { [void](Get-ProtonUiWindow); return } catch { }
        Start-Sleep -Milliseconds 250
    } while ($timer.Elapsed.TotalSeconds -lt 20)
    throw 'Proton main window could not be opened. Open it manually, resolve dialogs, and retry.'
}

function Wait-ProtonUiState {
    param([string]$Expected, [int]$TimeoutSeconds = 30)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        # Reacquire each time. UI navigation can invalidate automation elements.
        $state = Get-ProtonUiState
        if ($state -eq $Expected) { return }
        Start-Sleep -Milliseconds 250
    } while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw ('Proton did not reach ' + $Expected + '. Observe its Home screen before retrying.')
}

function Disconnect-ProtonUi {
    $state = Get-ProtonUiState
    if ($state -eq 'Connected') { Invoke-ProtonUiButton 'ConnectionCardDisconnectButton' }
    elseif ($state -eq 'Connecting') { Invoke-ProtonUiButton 'ConnectionCardCancelButton' }
    Wait-ProtonUiState 'Disconnected'
}
