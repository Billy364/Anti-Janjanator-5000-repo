# Proposal to disconnect Proton VPN through Windows UI Automation

## Objective

Prototype a Janjanator workflow that asks the running Proton VPN client to disconnect through its own interface, replacing termination of the Proton desktop process during normal refresh. Evaluate whether applying split tunneling changes through Proton's interface and reconnecting causes the service to apply the updated policies reliably.

This is an investigation and prototype handoff. The current evidence establishes that UI Automation can disconnect this installed client. Whether disconnecting and reconnecting reloads externally edited settings or updates the required traffic policies remains unverified.

## Baseline and observed result

- Janjanator upstream `main` was pulled at `e12602c`, version 20.
- The tested Proton client process was `ProtonVPN.Client.exe` in `C:\Program Files\Proton\VPN\v5.1.9`.
- On November 20, 2026, the live interface exposed `ConnectionCardDisconnectButton` through Windows UI Automation.
- `InvokePattern.Invoke()` on that button succeeded. The `ProtectionTitle` control subsequently reported `Unprotected`.
- The standalone `Disconnect-ProtonVPN.ps1` in this repository root performed that experiment. It remains an exploratory script, with English status matching and limited recovery handling.
- We have not yet tested reconnection, policy reload, traffic routing, kill switch interaction, or settings edits while the client is running.

Proton publishes its Windows client source. Its connection card declares the Disconnect automation ID and binds the button to `DisconnectCommand`. The view model calls `_connectionManager.DisconnectAsync(VpnTriggerDimension.ConnectionCard)`. The same view declares `ConnectionCardConnectButton` and `ConnectionCardCancelButton`. These identifiers were checked against public `master`, which may differ from the installed version. Validate each identifier against the live client before using it.

- [Connection card XAML](https://github.com/ProtonVPN/win-app/blob/master/src/Client/ProtonVPN.Client/UI/Main/Home/Card/ConnectionCardComponentView.xaml)
- [Connection card view model](https://github.com/ProtonVPN/win-app/blob/master/src/Client/ProtonVPN.Client/UI/Main/Home/Card/ConnectionCardComponentViewModel.cs)
- [Official Windows client repository](https://github.com/ProtonVPN/win-app)

## Why investigate this

Exiting the desktop process does not establish that the tunnel or service disconnected. Janjanator intentionally keeps the Proton background service running. Invoking the client's normal Disconnect action gives Proton an opportunity to perform its own tunnel and policy teardown. That is the mechanism to test.

The proposed benefit depends on how Proton loads settings and rebuilds routing/filter rules. A disconnected client may retain a cached settings model. Editing its JSON while it remains open could be ignored or overwritten. Inspect that code path and experimentally verify reload behavior before permitting writes with a live client.

## Current Janjanator integration points

- `src/Update-DiscordRules.ps1` contains `Close-DiscordAndProton`, `Assert-AppsClosed`, settings discovery, validation, and guarded writes.
- `src/UpdaterWorker.ps1` accepts `Close`, `Refresh`, `LaunchProton`, and `LaunchDiscord`. Refresh currently rejects a running Proton desktop client.
- `src/UpdaterWindow.cs` drives process polling, button enablement, waits, and worker results.
- `src/FirstLaunchSetup.cs` performs the separate first-launch settings transaction. Its exit requirement needs its own investigation.

The existing running-client guards encode a real consistency assumption. Replacing termination with Disconnect requires a coordinated change to the UI, worker, and write guards after that assumption has been evaluated. Removing only the process guard would expose the existing write path to concurrent client writes.

## Proposed prototype sequence

1. Capture the initial Proton connection state and selected Janjanator account. Close Discord using the existing verified shutdown logic.
2. Locate the current user's Proton window. If it is hidden in the tray, use the installed launcher to show the existing client. Handle missing client, login screens, modal dialogs, and multiple candidate windows explicitly.
3. Invoke Disconnect if connected. If connecting, investigate the separate Cancel action. Wait for a verified disconnected state with a deadline, then check it again immediately before applying changes.
4. Apply the desired Include mode and Discord paths using a settings mechanism shown to be safe with a running client. Prefer evaluating Proton's own settings UI first. If external JSON editing is retained, prove how cached settings are refreshed and preserve the existing concurrency and backup guarantees.
5. Verify that the intended account and rules are represented in Proton's live model, then reconnect through an explicit user action during the prototype. Keep reconnection and rule-save results separate so retries do not rewrite settings unnecessarily.
6. Verify the connected state and actual routing before launching Discord. Preserve the user's selected connection intent. The connection card's Connect action can use the default connection intent, so it must be evaluated before claiming it reconnects to the previous server.

Retain the current first-launch flow until equivalent guarantees have been demonstrated. Surface failure at the exact failed step and keep Discord closed if policy application or reconnection is unresolved. Automatic fallback to killing Proton would obscure whether the new mechanism works. Expose any fallback as a deliberate user choice in the prototype.

## Sample scripts

These examples target Windows PowerShell 5.1 with .NET Framework UI Automation. Run them in the interactive Windows session and at a privilege level compatible with Proton. They require an open, signed-in Proton window. Discovery reads control identifiers and types only, avoiding account details, IP addresses, and other interface text.

Save the following shared functions as `Proton-Uia.ps1` in an experimental directory. Dot-source this file from the subsequent examples. The examples are proposed code, apart from the already tested root disconnect script. They have not been executed as a suite.

```powershell
#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-ProtonWindow {
    $sessionId = (Get-Process -Id $PID).SessionId
    $processes = @(Get-Process -Name 'ProtonVPN.Client' -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $sessionId })
    if ($processes.Count -ne 1) {
        throw 'Expected one Proton client in this Windows session.'
    }
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ProcessIdProperty,
        [int]$processes[0].Id)
    $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll(
        [Windows.Automation.TreeScope]::Children, $condition)
    $matches = @($windows | Where-Object {
        $_.Current.Name -eq 'Proton VPN'
    })
    if ($matches.Count -ne 1) {
        throw 'Open the Proton VPN main window and resolve any modal dialogs.'
    }
    return $matches[0]
}

function Find-ProtonControl($Window, [string]$AutomationId) {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    $matches = $Window.FindAll(
        [Windows.Automation.TreeScope]::Descendants, $condition)
    if ($matches.Count -gt 1) { throw "Ambiguous control: $AutomationId" }
    if ($matches.Count -eq 1) { return $matches[0] }
}

function Invoke-ProtonButton([string]$AutomationId) {
    $window = Get-ProtonWindow
    $button = Find-ProtonControl $window $AutomationId
    if ($null -eq $button) { throw "Control is unavailable: $AutomationId" }
    if ($button.Current.ControlType -ne [Windows.Automation.ControlType]::Button) {
        throw "Unexpected control type: $AutomationId"
    }
    if (-not $button.Current.IsEnabled) { throw "Control is disabled: $AutomationId" }
    $pattern = $null
    if (-not $button.TryGetCurrentPattern(
        [Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        throw "InvokePattern is unavailable: $AutomationId"
    }
    ([Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Wait-ProtonStatus([string[]]$ExpectedNames, [int]$TimeoutSeconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        # Reacquire controls because navigation can invalidate old elements.
        $window = Get-ProtonWindow
        $status = Find-ProtonControl $window 'ProtectionTitle'
        if ($null -ne $status -and $status.Current.Name -in $ExpectedNames) {
            return $status.Current.Name
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Proton did not reach the expected status before the deadline.'
}
```

### Inspect available controls

```powershell
. "$PSScriptRoot\Proton-Uia.ps1"
$window = Get-ProtonWindow
$controls = $window.FindAll(
    [Windows.Automation.TreeScope]::Descendants,
    [Windows.Automation.Condition]::TrueCondition)
foreach ($control in $controls) {
    if ($control.Current.AutomationId) {
        [pscustomobject]@{
            AutomationId = $control.Current.AutomationId
            ControlType = $control.Current.ControlType.ProgrammaticName
            Enabled = $control.Current.IsEnabled
        }
    }
}
```

### Disconnect and wait for acknowledgement

```powershell
. "$PSScriptRoot\Proton-Uia.ps1"
$window = Get-ProtonWindow
$status = Find-ProtonControl $window 'ProtectionTitle'
if ($null -eq $status) { throw 'Cannot determine the current connection state.' }
if ($status.Current.Name -ne 'Unprotected') {
    Invoke-ProtonButton 'ConnectionCardDisconnectButton'
}
$result = Wait-ProtonStatus -ExpectedNames @('Unprotected')
Write-Output "Disconnect acknowledged by Proton: $result"
```

This English-language example fails if the client is connecting, a status is unfamiliar, or Disconnect is unavailable. Add explicit handling based on observed states. A translated status label must be mapped and tested before supporting another language.

### Connect through the connection card

Run this separately only when ready to initiate a connection. It can use Proton's configured default connection, which may differ from the previous server. Inspect that behavior before integration.

```powershell
. "$PSScriptRoot\Proton-Uia.ps1"
Invoke-ProtonButton 'ConnectionCardConnectButton'
$result = Wait-ProtonStatus -ExpectedNames @('Protected') -TimeoutSeconds 60
Write-Output "Connection acknowledged by Proton: $result"
```

`Protected` and `Unprotected` are client UI acknowledgements. Independently validate service state and traffic behavior when evaluating policy application. Invocation can complete before a tunnel transition does. A timeout or UI Automation exception leaves the outcome uncertain, so reobserve state before retrying an action.

## Evaluation work and acceptance criteria

Trace the installed version's connection manager, client settings cache, settings persistence, service communication, and split tunneling policy update paths. Record the commit or release examined. Check whether disconnect actually tears down the relevant filters, whether reconnect reloads the rules, and whether Proton offers an existing supported mechanism to apply them.

Compare the current process-exit workflow against the proposed disconnect workflow using a disposable account/settings fixture where possible, followed by an explicitly controlled live test. Include changed rules, already-current rules, missing preference keys, simultaneous settings edits, and a second Janjanator instance. Verify exact backups, rollback, preservation of unrelated rules, and the selected account.

Exercise connected, disconnected, connecting, reconnecting, tray-hidden, minimized, modal, signed-out, elevated, localized, and updated-client cases. Verify behavior when a control disappears after discovery or Proton changes state between checks. Bound waits and serialize actions. Keep blocking UI Automation calls off the Janjanator UI thread, with a worker timeout strategy for an unresponsive provider.

For policy validation, use distinguishable test traffic from an included application and an unrelated application before and after changing a rule. Observe routing and the Proton connection independently. Include kill switch and split tunneling mode combinations, and account feature availability. Keep real Discord closed until the experiment establishes the desired route. Record any interval of direct connectivity during disconnection.

The prototype is successful when the agent can demonstrate reliable policy application without terminating the Proton client, preserve settings and connection intent, distinguish saved rules from applied policies, and recover safely from failed or interrupted steps. If live-client settings cannot be updated safely, document that finding and propose a graceful exit/relaunch sequence after UI disconnection for evaluation.

## Requested handoff deliverables

1. Source-backed explanation of Proton's settings and policy lifecycle for the tested version.
2. A standalone prototype and observed results for disconnect, rule application, and reconnect.
3. An integration plan covering UI states, worker actions, write guards, first-launch behavior, retries, and concurrency.
4. Focused regressions for the changed assumptions and a record of live routing tests and remaining uncertainties.

Keep the initial experiment independent of the production refresh flow until these questions are resolved.
