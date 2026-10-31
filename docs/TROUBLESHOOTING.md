# Settings and process status troubleshooting

## Unsupported Proton settings format on a newly signed-in account

Proton settings are saved locally on each PC. Signing in to the same account
does not make two PCs' settings files identical. Proton can omit preferences
that still use defaults. Earlier updater versions required all three split
tunneling keys to exist, so a legitimate new account file could be rejected.

Version 9 and later recognize those omitted defaults.
A missing enabled flag means off; a missing mode means Standard/Exclude. These
defaults are defined in [Proton's official source](https://github.com/ProtonVPN/win-app/blob/d2a4f8bc92a0fd296943a7cdd15f4f870c8a87f9/src/Client/Settings/ProtonVPN.Client.Settings.Contracts/DefaultSettings.cs).

1. Close the previous updater and extract the complete v10 ZIP into a new folder.
2. Run the EXE. If setup is pending, exit Proton from its tray, select the intended
   account, then click **Apply setup and open updater** when it unlocks. Setup
   must finish; Exit/X closes the program, and errors must be resolved/retried.
3. In the main interface, select the intended account and use **Close both apps**.
4. Once both cards show Exited, click **Refresh**. Version 10 automatically turns
   Split tunneling on in Include mode and updates Discord's rules together.
5. Reopen Proton and reconnect before opening Discord again.

Refresh can create an absent/null Included-app list, enable Split tunneling,
and switch Exclude mode to Include mode. It commits these together with the
Discord rules in one atomic replacement and exact backup. Explicit unrelated
Included apps and other app/IP lists/preferences are preserved.
Proton's account/feature restrictions still apply.

First-launch setup runs only until one successful completion per Windows user.
Switching to another Proton account later does not automatically reapply those
preferences. Clicking Refresh enables Include mode for the newly selected
account without reapplying startup, protocol, or excluded-country preferences.

## Running or unverified after Close succeeded

Version 9 discards scans started before the close operation and ignores process
records that exit while their path is being read. It also queries executable
paths using limited process-query rights before trying module inspection.
See [Microsoft's API documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew).

If a fresh scan still reports a running app, it may have reopened after the
shutdown check; the successful log records the earlier check, not a promise that
another program cannot reopen it. Quit it from its tray icon and retry. If an
actual live process cannot be verified, Refresh remains locked and the warning
names the process. Run the updater as the same Windows user/privilege level as
the apps, or close the inaccessible process manually.

For a continuing error, share the updater's VERSION.txt version, Proton's
version, and the exact account-detail/process-warning text. Do not share full
UserSettings/GlobalSettings files or backups: they can contain session data.
