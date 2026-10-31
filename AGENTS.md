# Agent instructions

These instructions apply to the entire Democracy Enforcer 5000 repository.
Read [README.md](README.md), [the user guide](docs/USER-GUIDE.md), and
[validation notes](docs/VALIDATION.md) before changing app behavior.

## Download the project

Use Git from a parent directory where a new checkout can be created:

```powershell
git clone https://github.com/Billy364/Anti-Janjanator-5000-repo.git Democracy-Enforcer-5000
Set-Location .\Democracy-Enforcer-5000
git status --short
```

For an existing checkout, inspect its status and current branch before fetching
updates. Preserve local work; do not reset or overwrite someone else's changes.

Without Git, open [the repository](https://github.com/Billy364/Anti-Janjanator-5000-repo),
choose **Code > Download ZIP**, extract it, and open PowerShell in the extracted
folder containing `build.ps1`. A source ZIP has no Git history and does not
include a precompiled executable. GitHub Desktop can also clone the repository
using the same URL. GitHub CLI is not required.

## Required environment

- Use 64-bit Windows with Windows PowerShell 5.1 (`powershell.exe`).
- The build uses the .NET Framework 4.x compiler at
  `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- Git is required only for cloning and version control.
- The app uses Windows Forms, System.Drawing, and System.Web.Extensions.
  No NuGet restore, npm install, Visual Studio project, or external library is
  needed. There is no `.csproj`; use the supplied script rather than `dotnet build`.
- Linux, macOS, and WSL alone cannot run this Windows build or its UI tests.
  Report that limitation if a Windows environment is unavailable.

Check the environment from Windows PowerShell:

```powershell
$PSVersionTable.PSVersion
[Environment]::Is64BitOperatingSystem
Test-Path (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe')
```

The final two commands should return `True`. The commands below explicitly use
Windows PowerShell so they also work when issued from a PowerShell 7 shell.

## Compile and package

From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
```

The script compiles the C# interface for x64, embeds the header artwork and ICO,
copies the required workers and documentation, and creates a portable ZIP.
For the current v11 source, outputs are:

- `dist\Democracy-Enforcer-5000\Democracy Enforcer 5000.exe`
- `dist\Democracy-Enforcer-5000\Open Democracy Enforcer 5000.cmd`
- `dist\Democracy-Enforcer-5000\UpdaterWorker.ps1`
- `dist\Democracy-Enforcer-5000\Update-DiscordRules.ps1`
- `dist\Democracy-Enforcer-5000-v11.zip`

Distribute the generated ZIP or the entire app folder, not just the EXE: the
PowerShell workers must stay beside it. The build is unsigned. The
`-ExecutionPolicy Bypass` argument applies to that PowerShell process; do not
change machine-wide execution policy as part of the build.

To try the app, only when requested, open the EXE from the generated folder.
Building and testing do not require closing the user's real Discord or Proton
apps. If compilation cannot overwrite its output EXE, close that running copy
of Democracy Enforcer 5000 before rebuilding.

## Verify changes

For changes affecting code, assets, or build scripts, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
```

The runner rebuilds the app, tests the first-launch transaction/form, checks UI
button policy, tests sparse settings and process-scan races, renders the forms
with fixture backends, and runs the synthetic worker regressions. It writes
unique run folders under `artifacts\tests-<id>\`, including `preview.png`.
UI rendering needs a Windows environment capable of creating Windows Forms
controls and a tray icon. Inspect the preview after interface/layout changes.

Worker tests simulate process discovery and shutdown and use temporary settings;
they must not close real apps or write real Proton account files. They cover
running-app guards, helper processes, a late restart, exact backups, preservation
of unrelated settings, no-op refresh, close without an account selection, other
Windows sessions, and the exited-process lookup race.

If a sandbox denies atomic `File.Replace` on test fixtures, use the environment's
normal permission mechanism for that fixture directory. Do not weaken the app's
atomic-write behavior or redirect tests at live settings. Report failures and
environment limitations accurately. Documentation-only edits need checks of the
documented paths and commands; they do not require another full regression run.

## Where to edit

| Path | Responsibility |
| --- | --- |
| `src/UpdaterWindow.cs` | Windows Forms UI, app identity, process inspection, worker bridge |
| `src/FirstLaunchSetup.cs` | First-launch preferences, transactional backups, marker, setup form |
| `src/UpdaterWorker.ps1` | Separate Close and Refresh operations |
| `src/Update-DiscordRules.ps1` | Installation discovery, settings validation, rule planning, atomic writes |
| `assets/HeaderArtwork.png`, `assets/AppIcon.ico` | Embedded artwork and icons |
| `build.ps1` | Compiler arguments, portable folder contents, ZIP name |
| `scripts/Test.ps1`, `tests/` | Test runner, fixture renderer, regression coverage |

Edit source files, not generated copies in `dist/`. Keep changes focused.
When renaming the app, also check the launcher, assembly metadata, tray text,
header fixture assertions, build/test paths, and documentation. When changing
release versions, keep the assembly version, `VERSION.txt`, ZIP name,
`CHANGELOG.md`, and documented output paths consistent.

## Preserve these behaviors

- Keep Close and Refresh separate. Refresh requires two clear UI polls, a
  complete scan, and a compatible selected account. The GUI's Refresh action
  enables Split tunneling in Include mode if needed, together with its rule update.
- Keep independent process checks in the worker and immediately before writing;
  the enabled UI button alone is not a write guard.
- Target verified Discord paths and the current Windows session. An unreadable
  process path or incomplete scan must not permit a partial refresh.
- Preserve unrelated Included apps and settings, detect concurrent changes, and
  create an exact backup for each actual change. A no-op must not write a backup.
- Proton can omit default preference keys. Treat missing enabled/mode values as
  off/Standard. The GUI worker opts into enabling Include mode and creating an
  omitted/null app list. Commit activation and rules in one atomic replacement
  with one exact backup. The legacy CLI reader remains strict unless explicitly
  opted in. Reject stale process inspections from before Close and ignore
  processes that exit during image lookup.
- Leave the Proton background service running. After successful GUI Refresh
  (including an already-current result), request a Proton launch, preferring its
  stable launcher and otherwise the newest versioned client. Use the Proton
  executable's directory as its working directory. Close and failed refreshes
  must not reopen apps; Discord stays closed. Avoid duplicate Proton launches.
  Report launch failure separately from saved-settings success. Respect Proton's
  own auto-connect setting; do not claim these tests verify VPN traffic routing.
- First successful launch per Windows user may configure the selected account
  and global startup preference as described in `docs/FIRST-LAUNCH.md`. Preserve
  unrelated values and exclusions, require Proton's exit, back up changed files,
  and write the persistent completion marker only after successful verification.
  Before the first completion, the user must select an account, exit Proton,
  and explicitly click the setup button. Disable it until two clear checks.
  Cancel/X, errors, missing accounts, and unverified completion must exit or
  remain at setup, never open the main updater. Later launches skip completed
  setup. Clicking Refresh may re-enable Include mode; do not reapply unrelated
  startup, protocol, or location preferences during normal Refresh.
- Never commit account settings, credentials, backups, request/result records,
  logs, or personal filesystem paths. Keep `dist/` and `artifacts/` ignored.

Before handing off, check `git diff --check` and `git status --short`. State what
changed, which verification ran, where the build is located, and any remaining
limitations. Commit or publish only within the user's requested scope.
