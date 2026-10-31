## Unreleased

- Rename the app to Democracy Enforcer 5000, including the executable, launcher, window, tray, portable package, and website. Preserve existing setup completion records and account settings.

# Changelog

## v11

- Launch Proton VPN immediately after successful GUI Refresh, including when
  rules are already current. Close and failed refreshes do not reopen it.
- Prefer the stable installed launcher, with the newest versioned client as a
  fallback. Use Proton's own folder as the working directory to avoid locking
  the portable updater folder. Do not launch another copy if Proton restarted.
- Keep saved-settings success and backups when app launch fails, and report a
  separate launch warning with manual-open guidance.
- Keep the refresh/connection status visible when Proton starts. Added mocked
  launch, lookup/failure/recovery, working-directory, and UI regression checks.

## v10

- Made first-launch setup mandatory. Exit Proton, select the account, and click
  Apply setup and open updater. The button stays disabled until exit is verified.
- Removed the skip option. Exit/X, missing accounts, failed setup, or an absent
  completion record cannot open the main interface. Added retry/gate coverage.
- GUI Refresh automatically enables Split tunneling in Include mode, including
  for a newly selected account after first-launch setup has completed.
- Activation flags and Discord rules share one guarded atomic write and exact
  backup. Unrelated app lists/preferences are retained; repeated refresh is a no-op.
- Added activation tests for omitted/off/Exclude/null settings, mode-only writes,
  preservation, restart/concurrent-edit guards, and unsupported schemas.

## v9

- Fixed the unsupported-format message for accounts whose default Proton
  preference keys have not been saved yet. Show off/Exclude mode guidance.
- Allow Refresh to create an omitted/null saved Included-app list when Include
  mode is explicitly enabled, with exact backups and unrelated bytes preserved.
- Accept omitted feature flags and null default toggles during first-time setup.
- Ignore processes that exit during image lookup and use a limited-rights native
  query before falling back to module inspection. Name unreadable processes.
- Discard stale inspections from before Close and require fresh clear checks.
- Added sparse-settings, exit-race, native path lookup, and delayed-scan tests.

## v8

- Added first-launch setup for starting Proton with Windows, auto-connect,
  Fastest country excluding Brazil, Proton Protocols using Proton WireGuard,
  and Split tunneling in Include mode where Proton supports it.
- Wait for Proton's desktop client to exit and require an account selection on
  PCs with multiple saved accounts. The usual updater opens afterward.
- Back up changed account/global settings, preserve unrelated values, detect
  concurrent changes, and roll back partial writes when safe.
- Record successful setup per Windows user outside the portable app folder;
  subsequent launches leave the preferences alone, including after app updates.
- Added first-launch transaction and form regression tests using synthetic data.

## v7

- Named the app Anti-Janjanator 5000 in its interface, window title, executable,
  tray tooltip/menu, and Windows product metadata.
- Added a repository layout, repeatable build/package script, and test runner.

## v6

- Added proportional header artwork and matching embedded application/tray icons.
- Added a tray menu to open the window or exit the app.

## v5

- Added a Windows Forms interface with live application status.
- Separated closing the apps from refreshing the selected Proton account.
- Gated refresh on verified app exit, ready Include-mode settings, and a complete
  executable scan; retained backups and pre-write validation.
