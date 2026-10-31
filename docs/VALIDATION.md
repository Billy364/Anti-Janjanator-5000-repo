# Interface validation — October 30, 2026

V10 passed the complete runner with mandatory setup and automatic activation
coverage. Setup waits for two clear Proton exit checks, requires an account and
explicit click, and rejects startup after Cancel/X, no account, errors, or a
missing completion marker. An explicit successful retry unlocks startup; later
launches skip verified completion. The setup form and main activation message
were rendered with fixture data and visually checked.

GUI Refresh tests enable Include mode from missing/off/Exclude/null preferences,
preserve unrelated settings/app/IP lists and opaque bytes/BOM, create one exact
backup for flags and rules together, and perform a no-op on repeat. A flag-only
change is written even when Discord rules are already current. Running apps,
late restarts, concurrent settings edits, and unsupported schemas prevent both
activation and rule changes. The legacy non-opted-in CLI reader remains strict.
All tests use synthetic write targets; actual Proton account restrictions and
live VPN routing remain outside this validation.

V9 passed the complete runner with additional sparse-settings and process-race
coverage. Account-reader fixtures check omitted off/Standard defaults, disabled
and Exclude modes, missing/null/empty Include lists, and unsupported types/values.
The worker creates absent/null lists with byte-exact backups, retained BOM and
unrelated text, then performs a no-op on repeat. It rejects duplicate properties
and never enables off/Standard mode during normal Refresh. First-time setup also
accepts omitted global feature flags and null default toggles.

Process tests cover exit during image lookup, unreadable live processes, other
Windows sessions, and a native limited-rights query for the test process. A form
test holds a pre-Close scan until shutdown has completed, then verifies its old
result is discarded and two fresh clear polls unlock Refresh. The original
policy, first-launch, image/icon, and worker tests also pass. All write targets
are synthetic; the friend's actual settings file was not available, so these
tests do not establish the exact missing key or restart cause on that PC.

V8 adds one-time Proton preference setup. The complete build/test runner passed:
the original UI policy and eight worker regression groups, plus first-launch
tests for requested preferences, preserved exclusions/Included apps/opaque data,
BOM preservation, exact account/global backups, completion and later manual
changes, running-app rejection, concurrent edits, rollback/retry, marker failure,
invalid schemas, omitted defaults, existing Brazil exclusions, simultaneous
instances, and account isolation. First-launch form tests verify waiting for
Proton exit and requiring an explicit choice on a multi-account PC. Both forms
were rendered with fixture data and visually checked.

All first-launch write targets and completion records were synthetic fixtures.
Live account settings and startup registration were not changed by these tests.
The saved startup preference is registered by Proton on its next normal launch.
Account entitlements, feature rollout availability, startup across an actual
reboot, and live VPN routing are not established by this test suite. See
FIRST-LAUNCH.md for the settings format references and limitations.

V7 renames the app to Anti-Janjanator 5000, including the executable, window
title, header, tray tooltip/menu, launcher, and Windows product/title metadata.
The form was rendered with a fixture backend to check the new title fits.
Embedded artwork and app/tray icons are retained. No backend logic changed.

V6 adds the supplied proportional header artwork and matching embedded app/tray
icons. The ICO contains 16, 24, 32, 48, 64, 128, and 256 pixel images, preserving
transparency and aspect ratio. The executable's native icon, the form icon, and
the tray icon all use that ICO. The source PNG is included unchanged.

The updated form was rendered from its own controls with a fixture backend and
visually checked: the header artwork fits beside the title and does not overlap
the status cards or actions. Resource loading, proportional zoom, the form icon,
and the tray icon/menu were verified. This render involved no real process or
account operations. Tray resources are disposed when the form is disposed.
The broader functional results below are from the v5 core, which is unchanged.

The Windows Forms application was built with the Windows .NET Framework C#
compiler for 64-bit Windows. Source and the PowerShell worker are included.

Verified:

- Button policy: initial/unknown state, either app running, both running, helper
  or unreadable state, two consecutive clear polls, busy operations, missing
  account, disabled Include mode, and incomplete scans.
- The worker independently refuses refresh while either main app or an
  inventory-derived helper is running.
- A simulated app relaunch after reading settings prevents the write.
- Refresh adds the current main/helper executables, removes a stale path,
  preserves unrelated apps/preferences, and creates an exact backup.
- Repeating refresh performs no write/backup. Neither action reopens an app.
- Close operates without an account selection and does not edit settings.
- Other Windows sessions are not targeted or treated as this user's apps.
- Exited process records with unreadable image paths do not produce a false
  shutdown error; the enumeration/image-lookup race has regression coverage.

Worker tests used synthetic settings and simulated processes in Windows
PowerShell 5.1. The policy tests use the same compiled classes as the application.
No live account settings were used as write targets in those automated tests.

The repository's build and test runner also passed on Windows PowerShell 5.1:
compilation, UI policy checks, fixture rendering, and all eight worker regression
groups. The committed screenshot uses example account/process data. Desktop
tray interactions and VPN traffic routing are outside these automated checks.

The currently installed Proton 5.1.9 settings layout is recognized. This does
not establish compatibility with every Proton release or verify VPN traffic
routing. The UI's status is for desktop clients and Discord helpers, not a
promise that the background VPN service has stopped or the tunnel disconnected.
