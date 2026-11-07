#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$RepositoryUrl = 'https://github.com/Billy364/Anti-Janjanator-5000-repo.git',
    [string]$Branch = 'main',
    [string]$InstallDirectory,
    [string]$ShortcutDirectory = ([Environment]::GetFolderPath('Desktop'))
)

# Works both as a file and when its contents are passed to Invoke-Expression.
function Install-AntiJanjanator {
    param([string]$SourceUrl, [string]$SourceBranch, [string]$Destination, [string]$DesktopDirectory)
    $ErrorActionPreference = 'Stop'
    if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) {
        throw 'Anti-Janjanator 5000 requires 64-bit Windows.'
    }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Install .NET Framework 4.x with its C# compiler first.' }
    $git = (Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    $winget = (Get-Command winget.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (-not $git) {
        if (-not $winget) {
            throw 'Git and WinGet were not found. Install Git for Windows from https://git-scm.com/install/windows, then rerun this script in a new PowerShell window.'
        }
        Write-Host 'Installing Git for Windows with WinGet...'
        & $winget install --id Git.Git --exact --source winget --no-upgrade --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -ne 0) {
            throw "WinGet failed to install Git (exit code $LASTEXITCODE). Install Git for Windows manually, then rerun this script."
        }

        # The installer can update the saved PATH without updating this PowerShell session.
        $savedPath = @(
            [Environment]::GetEnvironmentVariable('Path', 'Machine')
            [Environment]::GetEnvironmentVariable('Path', 'User')
        )
        $originalPath = $env:Path
        try {
            $env:Path = (@($originalPath) + $savedPath) -join ';'
            $git = (Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
        } finally {
            $env:Path = $originalPath
        }
        if (-not $git) {
            throw 'WinGet completed, but Git was not found on PATH. Open a new PowerShell window and rerun this script.'
        }
    }
    $powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if ([string]::IsNullOrWhiteSpace($SourceBranch) -or $SourceBranch.StartsWith('-')) { throw 'Invalid branch name.' }
    if ([string]::IsNullOrWhiteSpace($Destination)) {
        # Windows stores redirected/localized Downloads here under its known-folder ID.
        $downloadsFolder = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders' -ErrorAction Stop).'{374DE290-123F-4565-9164-39C4925E467B}'
        if ([string]::IsNullOrWhiteSpace($downloadsFolder)) { throw 'Windows Downloads folder could not be resolved. Specify -InstallDirectory.' }
        $Destination = Join-Path ([Environment]::ExpandEnvironmentVariables($downloadsFolder)) 'Anti-Janjanator-5000'
    }
    $destinationPath = [IO.Path]::GetFullPath($Destination)
    $desktopPath = [IO.Path]::GetFullPath($DesktopDirectory)
    if (-not (Test-Path -LiteralPath $desktopPath -PathType Container)) { throw 'Desktop folder was not found.' }
    if (Test-Path -LiteralPath $destinationPath) {
        # Never adopt or overwrite an unrelated directory or someone else's work.
        if (-not (Test-Path -LiteralPath (Join-Path $destinationPath '.git') -PathType Container)) { throw 'Installation folder exists but is not a Git checkout. Choose another -InstallDirectory.' }
        $originUrl = & $git -C $destinationPath remote get-url origin
        if ($LASTEXITCODE -ne 0 -or $originUrl -cne $SourceUrl) { throw 'Installation checkout has a different origin. Choose another -InstallDirectory.' }
        $currentBranch = & $git -C $destinationPath branch --show-current
        if ($LASTEXITCODE -ne 0 -or $currentBranch -cne $SourceBranch) { throw 'Installation checkout is on a different branch. Choose another -InstallDirectory.' }
        $changes = & $git -C $destinationPath status --porcelain
        if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Installation checkout has local changes. Preserve them before updating.' }
        & $git -C $destinationPath fetch origin $SourceBranch
        if ($LASTEXITCODE -ne 0) { throw 'Repository update failed.' }
        & $git -C $destinationPath merge --ff-only FETCH_HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Checkout cannot be fast-forwarded. Local history was preserved.' }
    } else {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destinationPath))
        & $git clone --single-branch --branch $SourceBranch -- $SourceUrl $destinationPath
        if ($LASTEXITCODE -ne 0) { throw 'Git clone failed. No shortcut was created.' }
    }
    $build = Join-Path $destinationPath 'build.ps1'
    if (-not (Test-Path -LiteralPath $build -PathType Leaf)) { throw 'Repository build.ps1 is missing.' }
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $build
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close any running copy of this installation before retrying. No shortcut was created.' }
    $appFolder = Join-Path $destinationPath 'dist\Anti-Janjanator-5000'
    $app = Join-Path $appFolder 'Anti-Janjanator 5000.exe'
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Expected Anti-Janjanator 5000 executable is missing.' }
    $shortcutPath = Join-Path $desktopPath 'Anti-Janjanator 5000.lnk'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        try {
            $shortcut.TargetPath = $app
            $shortcut.WorkingDirectory = $appFolder
            $shortcut.IconLocation = $app + ',0'
            $shortcut.Description = 'Launch Anti-Janjanator 5000'
            $shortcut.Save()
        } finally { if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) } }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    Write-Output "Built app: $app"
    Write-Output "Desktop shortcut: $shortcutPath"
    Write-Output 'Open the Desktop shortcut to launch Anti-Janjanator 5000 when ready. Installation does not launch the app or change Proton settings.'
}

Install-AntiJanjanator -SourceUrl $RepositoryUrl -SourceBranch $Branch -Destination $InstallDirectory -DesktopDirectory $ShortcutDirectory
