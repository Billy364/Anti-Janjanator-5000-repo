#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
& (Join-Path $repository 'build.ps1')
$run = Join-Path $repository ('artifacts\tests-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
[void][IO.Directory]::CreateDirectory($run)
$app = Join-Path $repository 'dist\Anti-Janjanator-5000\Anti-Janjanator 5000.exe'
Copy-Item -LiteralPath $app -Destination $run
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($name in @('GuiPolicyTests','HeaderPreview','FirstLaunchSetupTests')) {
    $test = Join-Path $run ($name + '.exe')
    & $compiler /nologo /target:exe /platform:x64 "/out:$test" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/reference:$app" (Join-Path $repository "tests\$name.cs")
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile $name." }
    if ($name -eq 'HeaderPreview') { & $test (Join-Path $run 'preview.png') }
    elseif ($name -eq 'FirstLaunchSetupTests') { & $test (Join-Path $run 'first-launch') }
    else { & $test }
    if ($LASTEXITCODE -ne 0) { throw "$name failed." }
}
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repository 'tests\Test-GuiWorker.ps1') -WorkerPath (Join-Path $repository 'src\UpdaterWorker.ps1') -RunFolder (Join-Path $run 'worker')
if ($LASTEXITCODE -ne 0) { throw 'Worker regression tests failed.' }
Write-Output "All checks passed. Artifacts: $run"
