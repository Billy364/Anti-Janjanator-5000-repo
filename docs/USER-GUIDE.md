# Anti-Janjanator 5000

Open **Anti-Janjanator 5000.exe**, or double-click **Open Anti-Janjanator 5000.cmd**. Extract
the ZIP first and keep all files together. This is a portable 64-bit Windows
desktop app; no installer is needed. The C# and PowerShell source is included.
The executable is locally built and unsigned.

The supplied artwork is embedded in the header and used for the application,
title-bar, and system-tray icon. Double-click the tray icon, or right-click it
and choose **Open Anti-Janjanator 5000**, to bring the window back. **Exit** closes the
updater; an operation already in progress must finish first. The tray icon is
removed when the updater exits.

## Use

On the first launch, a setup window configures Proton VPN for the selected
account: start with Windows and auto-connect, Fastest country excluding Brazil,
Proton Protocols with Proton WireGuard, and Split tunneling in Include mode where
supported. Exit Proton from its tray icon and choose an account if needed, then
click **Apply setup and open updater**. The button is disabled until Proton's
exit is verified. Successful setup opens the usual interface. It does not close
Discord or reconnect the VPN. Reopen Proton afterward so it applies the settings
and registers startup with Windows.

The completed setup is remembered per Windows user across portable-folder moves
and app updates. Later launches skip completed setup. There is no skip option:
Exit/X closes the program, and failed setup must be retried before continuing.
Normal Refresh may enable Include mode again but leaves other setup preferences alone. Details
and restoration instructions are in FIRST-LAUNCH.md.

1. Check the two status cards. They update automatically while the window is open.
2. If more than one Proton account is listed, choose the settings file for the
   account you use. The saved time can help identify it. If Split tunneling is off
   or uses Exclude mode, Refresh will enable Include mode automatically.
3. Click **1. Close both apps**. It closes Discord (including its helpers) and
   the Proton desktop client. It works without selecting a Proton account.
4. Once both apps are verified as exited, **2. Refresh split tunneling rules**
   becomes available. Click it to enable Include mode if needed and update the
   selected account's Included apps in one backed-up settings change.
5. Open Proton VPN yourself and reconnect, then reopen Discord.

Newly signed-in accounts may omit default settings. This is supported: the app
recognizes off/Exclude mode rather than treating omitted keys as an unknown
format. Refresh enables Include mode and creates the list if needed. See
TROUBLESHOOTING.md if a previous version showed Unsupported Proton settings
format or the status remains unverified after shutdown.

The buttons stay disabled during an operation. Refresh also stays disabled when
process status is uncertain, a Discord scan is incomplete, or no compatible
account is selected. Two consecutive clear status checks are required before
unlocking it. The write operation independently rechecks that the apps are gone;
an app restarting after a button lights up still blocks the write.

Closing Discord ends active calls. The close action requests a normal exit and
then ends remaining verified app processes if necessary. A busy Discord updater
is allowed up to 30 seconds to finish; it is not forcibly terminated. The close
action does not refresh settings, and the refresh action does not reopen apps.

Proton's status refers to its desktop client. Its background VPN service is left
running; this tool does not verify the connection's routing or automatically
connect/disconnect the VPN. Reconnect Proton after refreshing.

## Discovery and settings

The standard Discord, PTB, and Canary folders are detected for the current
Windows user. A running custom installation in a folder named Discord,
DiscordPTB, or DiscordCanary is remembered after the app closes. You can also
use **Choose Discord folder** or **Browse settings file**. Linked folders and
unverifiable installation paths are rejected rather than partially scanned.

Every executable found inside the detected installation folders is enabled in
the selected account's Included apps; missing old paths are removed. Other
Included apps and other preferences are preserved. Unknown settings layouts or
concurrent file changes stop the write. The matching settings layout has been
checked against the local Proton VPN 5.1.9 installation; this is an unofficial
utility and compatibility with every Proton version is not guaranteed.

An exact backup is created for each actual change under **DiscordRuleBackups**
beside the selected Proton settings file. The backup path and executable changes
appear in Activity. An already-current list does not cause a write or backup.

For restoration, exit Proton, copy the printed backup over its original
UserSettings account file, then reopen and reconnect Proton. Restoring a full
backup also restores the other preferences from that time.

## Files and privacy

The worker uses the Windows PowerShell 5.1 included with Windows, plus the
included scripts. The interface uses .NET Framework's Windows Forms. The app
runs locally, does not upload anything, and installs no scheduled task or service.
First-time setup changes Proton's own startup preference; Proton registers its
Windows startup entry when next opened. The updater itself does not run at boot.
Normally it should run as the same Windows user/privilege level as the two apps.
If process paths are inaccessible, close the affected app manually.

Small operation request/result records are stored in:

```text
%LOCALAPPDATA%\DiscordProtonUpdater\Runs
```

These contain local paths, results, and backup locations; keep them private.
The distributable ZIP contains no account settings, personal paths, or backups.
Close this updater window before deleting its own extracted folder.

The original header PNG and the multi-size ICO are included in `assets` for
rebuilding from source. Both are also embedded in the executable, so they do not
need to be loaded from an external image path at runtime.

See VALIDATION.md for testing scope. The application deliberately separates the
close and refresh actions; the previous all-in-one command is not used by its UI.
