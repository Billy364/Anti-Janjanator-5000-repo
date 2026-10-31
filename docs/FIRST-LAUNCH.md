# First-launch Proton VPN setup

The first launch of v8 or later runs setup once per Windows user, including for
users upgrading from v7. One saved account is selected automatically; if several
are saved, select the one to configure. Other account files are left unchanged.
Windows startup is a shared Proton preference for that Windows user.

Exit Proton VPN from its system-tray icon. Setup waits for two clear checks of
the desktop client's exit and rechecks in the settings writer. Discord can stay
open. Click **Apply setup and open updater** after the button unlocks; it does
not run merely because the account is selected or Proton exits. Setup is required
before the main interface can open. Exit/X closes the program instead of skipping.
After setup succeeds, the original updater opens with the same two action
buttons and interface. Use its normal close/refresh workflow to update Discord's
executable list, then reopen Proton and reconnect before opening Discord again.

## Preferences saved

| Preference | First-launch value |
| --- | --- |
| Start Proton with Windows | On |
| Connect when Proton opens | On |
| Default connection | Fastest country |
| Excluded locations | Add Brazil; retain existing exclusions |
| Proton Protocols | On |
| Protocol | Proton WireGuard (`ProTunUdp`, not legacy `WireGuardUdp`) |
| Split tunneling | On, Include mode |
| Included apps | Preserve the list; initialize an empty list if omitted |

Proton loads these preferences and registers its native Windows startup entry
when you next open Proton. Setup does not start Proton, reconnect an active VPN,
stop its background service, or register the updater itself to start at boot.

Proton still controls account entitlements and feature availability. For
example, unsupported plans cannot use excluded locations or split tunneling
simply because their requested values are saved. Setup does not alter the plan,
decrypt account data, change feature flags, or claim the live VPN uses a protocol
before Proton applies it. Unknown or unfinished settings formats stop setup.

## Remembering completion

Successful setup writes:

```text
%LOCALAPPDATA%\DiscordProtonUpdater\FirstLaunchSetup.json
```

The marker stores a setup version and completion time, with no account contents.
It belongs to the Windows user, not the extracted app folder. Later launches
skip the setup window, even if the user manually changes Proton preferences or
switches accounts. An explicit Refresh enables Include mode if needed, while
leaving startup, connection location, and protocol preferences unchanged. App
updates do not reset the marker. A lock prevents two copies from
performing setup at once.

If no account is detected, sign in to Proton once or use **Browse account
settings** to choose the account file. If you cannot complete setup or encounter
a write error, the main updater stays blocked. Resolve the error and click
**Retry setup**, or exit the program and retry on a later launch. Closing the
window or returning an unverified success result cannot bypass the completion
record check. Setup controls are locked while files are being written.

## Backups and interrupted setup

Every changed account/global settings file gets an exact original backup under
`DiscordRuleBackups` beside the selected account file. Names begin with
`first-setup-account-` or `first-setup-global-`. Backup paths appear in the
updater's activity log after successful setup. Already-correct files need no
write or backup.

All candidates are prepared before writes. Each replacement verifies Proton's
exit and checks both files for external edits. A failed transaction attempts to
restore files it already changed. If Proton restarts or another program changes
those files, rollback refuses to overwrite that newer state; the error directs
the user to review the backups. Completion is recorded only after both files
match the intended result. This is guarded per-file atomic replacement, not a
filesystem transaction across both files.

To restore, exit Proton, copy the relevant exact backup over its original
`UserSettings...json` or `GlobalSettings.json`, then reopen Proton. A full backup
also restores unrelated preferences and session data from that time, so keep it
private and use the file belonging to the intended account. The setup marker
remains in place, so restoration is not immediately overwritten on next launch.

## Schema references and verification

The preference encoding was checked against the local Proton VPN 5.1.9 layout
and these official Proton Windows source files at commit
`d2a4f8bc92a0fd296943a7cdd15f4f870c8a87f9`:

- [User settings](https://github.com/ProtonVPN/win-app/blob/d2a4f8bc92a0fd296943a7cdd15f4f870c8a87f9/src/Client/Settings/ProtonVPN.Client.Settings/UserSettings.cs)
- [Global settings](https://github.com/ProtonVPN/win-app/blob/d2a4f8bc92a0fd296943a7cdd15f4f870c8a87f9/src/Client/Settings/ProtonVPN.Client.Settings/GlobalSettings.cs)
- [Startup handler](https://github.com/ProtonVPN/win-app/blob/d2a4f8bc92a0fd296943a7cdd15f4f870c8a87f9/src/Client/ProtonVPN.Client/Handlers/AutoStartupHandler.cs)
- [Startup activator](https://github.com/ProtonVPN/win-app/blob/d2a4f8bc92a0fd296943a7cdd15f4f870c8a87f9/src/Client/ProtonVPN.Client/Services/Bootstrapping/Activators/AppStartupActivator.cs)

Regression tests use synthetic files, process guards, and accounts. They verify
the saved preferences, exact backups, preservation, once-only behavior, failures,
rollback, concurrency, and the first-launch form's required wait/account/button
flow. Cancellation, errors, and missing completion records cannot unlock startup.
They do not change live Proton settings or verify startup at a Windows reboot,
Proton account entitlements, or VPN traffic routing.
