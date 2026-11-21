#requires -Version 5.1
param([string]$WorkerPath, [string]$RunFolder)
$ErrorActionPreference = 'Stop'
[void][IO.Directory]::CreateDirectory($RunFolder)
$requestFile = Join-Path $RunFolder 'request.json'
# Unknown actions are rejected before any UI or process operations. Use the
# real executable entry point to check loading, request paths, and result IO.
[IO.File]::WriteAllText($requestFile, '{"Action":"InvalidFixtureAction"}')
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $powershell -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File $WorkerPath -RequestPath $requestFile
if ($LASTEXITCODE -ne 0) { throw 'Prototype worker could not run.' }
$resultFile = Join-Path $RunFolder 'result.json'
if (-not (Test-Path -LiteralPath $resultFile)) { throw 'Prototype entry point did not write a result.' }
$result = [IO.File]::ReadAllText($resultFile) | ConvertFrom-Json
if ($result.Success -or $result.Message -cne 'Unknown prototype action.') { throw 'Unexpected entry-point result.' }
Write-Output 'PASS: The real prototype worker entry point retains its request path and returns an error result before any app operations.'
