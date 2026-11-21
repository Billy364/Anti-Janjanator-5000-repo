# Anti-Janjanator 5000 — UI Automation prototype 1

This experimental build uses Proton's Disconnect, Cancel connection, Exit, and
Connect controls through Windows UI Automation. It is based on v20 and targets
the English Proton VPN 5.1.9 Home screen.

Extract the entire ZIP and open **Anti-Janjanator 5000.exe**. Keep the included
PowerShell files beside it. Windows PowerShell 5.1 and .NET Framework are used.
The normal first-launch setup still runs if this Windows user has not completed
it. That setup requires Proton to exit before applying its preferences.

## Try the workflow

1. Open Proton, sign in, and go to Home. Select the corresponding saved account
   in Janjanator. Check the account confirmation after comparing them yourself.
2. Click **Close Discord + disconnect VPN**. The prototype closes verified
   Discord processes, invokes Disconnect or Cancel connection, and waits for
   Proton to acknowledge Disconnected.
3. Select **Allow Proton to exit through its menu for the rule refresh**.
   Click **Refresh rules (graceful restart)**. The prototype invokes Proton's
   Exit menu item, waits for the client to exit, and uses the original guarded
   settings writer with exact backups. Resolve a Proton confirmation dialog
   manually if it requests one, then retry.
4. Select the connection-preferences confirmation and click **Reconnect Proton
   VPN**. The prototype starts Proton, invokes Connect when needed, and waits
   for the Protected status and an available Disconnect control.
5. Verify the signed-in account, selected server, and desired traffic routing
   in Proton and with your own test traffic. Select the verification checkbox
   and click **Launch Discord**. This step rechecks Protected and the saved
   rules before opening the refreshed executable.
6. The prototype displays **Process completed** and closes after 2.5 seconds.

Disconnection can allow direct connectivity depending on Kill Switch settings.
The connection card follows Proton's current connection preferences. A restart
can select a different server. Account matching and routing verification require
your confirmation in this prototype.

Errors appear in Activity and keep the current step available for retry. A
150-second worker deadline bounds an unresponsive UI Automation provider. After
a timeout, observe Proton before retrying because its state may have changed.
English status recognition, a unique verified client in the current Windows
session, and visible enabled controls are required. Resolve login screens,
modals, minimized windows, or unsupported versions before retrying.

Per-run state and requests are stored under
`%LOCALAPPDATA%\DiscordProtonUpdater\PrototypeRuns`. Treat these files as private.
Each instance has its own run directory. A session-wide mutex serializes
prototype actions. Settings writes retain the original process, inventory,
concurrent-edit, and backup checks.

## Why this prototype uses the graceful-restart choice

The inspected source is Proton's v5.1.9 tag,
commit `bd8380fe92c2b07f07ee6e48858307f12780fcb9`. The settings cache retains
both serialized and typed values. Its user cache is reset when UserId changes.
The inspected code does not establish that disconnect/reconnect reloads
externally edited settings. Keeping the client closed during the JSON
transaction therefore remains the consistency guard for this experiment.

The title-bar menu exposes `KebabMenuExitItem`. Its command invokes Proton's
exit-with-confirmation flow. The connection card exposes separate Connect,
Cancel, and Disconnect commands. Disconnect clears the current connection
intent and sends a disconnection request to the service. Connect uses the
connection card's current intent.

- [Settings cache](https://github.com/ProtonVPN/win-app/blob/bd8380fe92c2b07f07ee6e48858307f12780fcb9/src/Client/Settings/ProtonVPN.Client.Settings/Caching/SettingsCacheBase.cs)
- [User cache reset](https://github.com/ProtonVPN/win-app/blob/bd8380fe92c2b07f07ee6e48858307f12780fcb9/src/Client/Settings/ProtonVPN.Client.Settings/Caching/UserSettingsCache.cs)
- [Exit menu control](https://github.com/ProtonVPN/win-app/blob/bd8380fe92c2b07f07ee6e48858307f12780fcb9/src/Client/ProtonVPN.Client/UI/Main/Components/TitleBarMenuComponent.xaml)
- [Connection card commands](https://github.com/ProtonVPN/win-app/blob/bd8380fe92c2b07f07ee6e48858307f12780fcb9/src/Client/ProtonVPN.Client/UI/Main/Home/Card/ConnectionCardComponentViewModel.cs)
- [Connection manager](https://github.com/ProtonVPN/win-app/blob/bd8380fe92c2b07f07ee6e48858307f12780fcb9/src/Client/Logic/Connection/ProtonVPN.Client.Logic.Connection/ConnectionManager.cs)

## Evaluation status

The automation identifiers and cache behavior were examined in the pinned
source. The proposal reports a prior live disconnect experiment by its author.
This implementation is exercised with synthetic settings and mocked UI actions.
Live invocation, reconnection, routing, Kill Switch behavior, feature
entitlements, account matching, elevated clients, and localization still need
controlled validation.

For a controlled evaluation, record the initial account/server and connection
state, run the sequence, inspect the rule list in Proton, and compare
distinguishable traffic from an included application and an unrelated one.
Repeat for changed and already-current rules, denied UI access, modal dialogs,
interrupted actions, a second prototype instance, and an active Kill Switch.
Keep Discord closed until the desired route is established.

An eventual workflow that retains the client throughout the rule update needs
an adapter for Proton's own settings UI, account identification, exact rule
verification, and recovery from partially applied changes. This build provides
the explicit graceful-restart path described in the proposal.
