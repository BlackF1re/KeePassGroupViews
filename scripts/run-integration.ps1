$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path '.\KeePass\KeePass.exe').Path
$env:KPGV_TEST_DB = Join-Path (Get-Location).Path 'integration-fixture.kdbx'
foreach ($phase in @('setup', 'verify')) {
    $env:KPGV_TEST_PHASE = $phase
    $env:KPGV_TEST_RESULT = Join-Path (Get-Location).Path "integration-$phase.txt"
    Remove-Item -ErrorAction SilentlyContinue $env:KPGV_TEST_RESULT
    Write-Host "Running real KeePass integration phase: $phase"
    $p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
    if (-not $p.WaitForExit(65000)) {
        $p.Refresh()
        Write-Host "KeePass PID $($p.Id), window: $($p.MainWindowTitle), visible handle: $($p.MainWindowHandle)"
        if (Test-Path $env:KPGV_TEST_RESULT) { Get-Content $env:KPGV_TEST_RESULT | Write-Host }
        Stop-Process -Id $p.Id -Force
        throw "KeePass integration test '$phase' timed out."
    }
    if (!(Test-Path $env:KPGV_TEST_RESULT)) {
        throw "Integration plugin did not produce a result for '$phase' (KeePass exit $($p.ExitCode))."
    }
    $lines = @(Get-Content $env:KPGV_TEST_RESULT)
    $lines | ForEach-Object { Write-Host $_ }
    if ($lines.Count -lt 2 -or $lines[0] -ne 'PASS') {
        throw "KeePass integration phase '$phase' failed."
    }
}
Write-Host 'Real KeePass UI integration tests passed.'
