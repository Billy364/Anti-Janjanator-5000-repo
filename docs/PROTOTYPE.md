# Anti-Janjanator 5000 — UI Automation prototype 2

This experimental build uses Proton's Disconnect, Cancel connection, Exit, and
Connect controls through Windows UI Automation. It is based on v20 and targets
the English Proton VPN 5.1.9 Home screen.

Extract the entire ZIP and open **Anti-Janjanator 5000.exe**. Keep the included
PowerShell files beside it. Windows PowerShell 5.1 and .NET Framework are used.
The normal first-launch setup still runs if this Windows user has not completed
it. That setup requires Proton to exit before applying its preferences.

## Try the workflow

Open Proton, sign in, and go to Home. Select the corresponding saved account
in Janjanator, just as in the main version. The existing first-time setup is
remembered per Windows user across these prototype builds.

1. Click **Close Discord + Disconnect and Close Proton**. This single action
   closes verified Discord processes, invokes Proton's Disconnect or Cancel,
   waits for Disconnected, invokes its Exit menu item, and verifies app exit.
2. Click **Refresh split tunneling rules** after the app-exit checks pass.
   The original atomic writer updates the selected account with exact backups.
3. After the five-second wait, click **Reconnect Proton VPN**. Proton opens,
   its connection card is invoked when needed, and the prototype waits for
   Protected before the next step can proceed.
4. After the twelve-second wait, check the connection in Proton and click
   **Launch Discord**. The worker rechecks Protected and the saved rules.
   Process completed appears and the updater closes after 2.5 seconds.

The one-time setup already configures startup, auto-connect, the default
connection, protocol preferences, and split tunneling. Each normal run uses
the numbered buttons. Completed steps stay greyed out.

Disconnection can allow direct connectivity depending on Kill Switch settings.
The connection card follows Proton's current connection preferences, which can
select a different server after restart. Check the signed-in account and
desired routing in Proton before opening Discord.

Errors appear in Activity and keep the failed step available for retry.
Resolve any Proton confirmation dialog manually if it requests one.
A 150-second worker deadline bounds an unresponsive UI Automation provider.
After a timeout, observe Proton before retrying because its state may have
changed. English status recognition, a unique verified client in the current
Windows session, and visible enabled controls are required.

Per-run state and requests are stored under
%LOCALAPPDATA%\DiscordProtonUpdater\PrototypeRuns. Treat these files as private.
Each instance has its own run directory. A session-wide mutex serializes
prototype actions. Settings writes retain the original process, inventory,
concurrent-edit, and backup checks.

## Why step 1 disconnects and exits Proton

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
verification, and recovery from partially applied changes. This build disconnects and exits Proton in the first button.
