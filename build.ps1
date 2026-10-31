#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The 64-bit .NET Framework C# compiler is required.' }
$destination = Join-Path $PSScriptRoot 'dist\Democracy-Enforcer-5000'
[void][IO.Directory]::CreateDirectory($destination)
$executable = Join-Path $destination 'Democracy Enforcer 5000.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$executable" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll `
    "/win32icon:$PSScriptRoot\assets\AppIcon.ico" `
    "/resource:$PSScriptRoot\assets\HeaderArtwork.png,HeaderArtwork.png" `
    "/resource:$PSScriptRoot\assets\AppIcon.ico,AppIcon.ico" `
    "$PSScriptRoot\src\UpdaterWindow.cs" "$PSScriptRoot\src\FirstLaunchSetup.cs"
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed ($LASTEXITCODE). Close any running copy of this build before rebuilding." }
Copy-Item -LiteralPath "$PSScriptRoot\src\UpdaterWindow.cs", "$PSScriptRoot\src\FirstLaunchSetup.cs", "$PSScriptRoot\src\UpdaterWorker.ps1", "$PSScriptRoot\src\Update-DiscordRules.ps1", "$PSScriptRoot\VERSION.txt" -Destination $destination
Copy-Item -LiteralPath "$PSScriptRoot\scripts\Open Democracy Enforcer 5000.cmd" -Destination $destination
Copy-Item -LiteralPath "$PSScriptRoot\docs\USER-GUIDE.md" -Destination (Join-Path $destination 'README.md')
Copy-Item -LiteralPath "$PSScriptRoot\docs\VALIDATION.md" -Destination $destination
Copy-Item -LiteralPath "$PSScriptRoot\docs\FIRST-LAUNCH.md" -Destination $destination
Copy-Item -LiteralPath "$PSScriptRoot\docs\TROUBLESHOOTING.md" -Destination $destination
[void][IO.Directory]::CreateDirectory((Join-Path $destination 'assets'))
Copy-Item -LiteralPath "$PSScriptRoot\assets\HeaderArtwork.png", "$PSScriptRoot\assets\AppIcon.ico" -Destination (Join-Path $destination 'assets')
$archive = Join-Path $PSScriptRoot 'dist\Democracy-Enforcer-5000-v12.zip'
Compress-Archive -LiteralPath $destination -DestinationPath $archive -Force
Write-Output "Built: $executable"
Write-Output "Packaged: $archive"
