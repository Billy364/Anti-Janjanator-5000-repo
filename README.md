# Anti-Janjanator 5000 — Proton UI Automation prototype

This branch implements the UI Automation experiment described in
[the supplied proposal](docs/proposals/PROPOSAL-Proton-UI-Automation.md).

The familiar four-button interface closes Discord, disconnects Proton through
its UI, and exits Proton through its menu in step 1. The next buttons refresh
the rules, reconnect Proton, and launch Discord after a Protected check. It
targets the English Proton VPN 5.1.9 Home screen.

Read [the prototype guide](docs/PROTOTYPE.md) before trying it. It explains the
settings-cache finding, required graceful restart, recovery, and live validation
that remains to be done. Normal first-launch setup is retained.

## Download and build

Clone the experimental branch with Git for Windows.

~~~powershell
git clone --branch prototype/proton-ui-automation https://github.com/Billy364/Anti-Janjanator-5000-repo.git Anti-Janjanator-5000-Prototype
Set-Location .\Anti-Janjanator-5000-Prototype
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
~~~

Requires 64-bit Windows, Windows PowerShell 5.1, and the .NET Framework 4.x
compiler. The build uses the Windows UI Automation assemblies.

The portable outputs are
`dist\Anti-Janjanator-5000-Proton-UIA-Prototype\Anti-Janjanator 5000.exe` and
`dist\Anti-Janjanator-5000-Proton-UIA-Prototype-2.zip`.
Extract the complete ZIP and keep the workers beside the executable.

## Verify

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1
~~~

Tests use synthetic settings and mocked controls. The fixture form is rendered
under `artifacts/`. Read [validation notes](docs/VALIDATION.md) for coverage.

## Implementation

- `src/PrototypeWindow.cs` contains the bounded worker adapter and prototype
  entry point, reusing the main application's UpdaterForm.
- `src/PrototypeWorker.ps1` serializes actions, records per-run state, and
  retains the original guarded writer for offline rule updates.
- `src/Proton-Uia.ps1` discovers the verified client and invokes its named UI
  controls with state checks and deadlines.
- `src/UpdaterWindow.cs`, `src/UpdaterWorker.ps1`, and
  `src/Update-DiscordRules.ps1` retain the v20 baseline logic for reuse and tests.
- `src/FirstLaunchSetup.cs` retains first-launch setup.

The [prototype guide](docs/PROTOTYPE.md) includes pinned source references and
the live evaluation plan. Account settings, run records, and backups belong
outside the repository and distributable.
