param(
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $backendRoot 'tests/MealMate.ApiTests/MealMate.ApiTests.csproj'
$resultsPath = Join-Path $backendRoot 'TestResults/family-management-e2e'
$trxPath = Join-Path $resultsPath 'family-management-e2e.trx'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker CLI is required for the PostgreSQL HTTP test.'
}
docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker is installed but its daemon is unavailable.'
}

New-Item -ItemType Directory -Force -Path $resultsPath | Out-Null
Remove-Item -LiteralPath $trxPath -Force -ErrorAction SilentlyContinue

dotnet test $projectPath `
    --configuration $Configuration `
    --filter 'FullyQualifiedName~FamilyManagementFlowPostgresHttpTests' `
    --logger 'trx;LogFileName=family-management-e2e.trx' `
    --results-directory $resultsPath
if ($LASTEXITCODE -ne 0) {
    throw "Family management PostgreSQL HTTP tests failed (exit $LASTEXITCODE)."
}

if (-not (Test-Path -LiteralPath $trxPath)) {
    throw "The test run produced no TRX report at $trxPath."
}

[xml] $trx = Get-Content -LiteralPath $trxPath
$counters = $trx.TestRun.ResultSummary.Counters
$total = [int] $counters.total
$passed = [int] $counters.passed
$failed = [int] $counters.failed
$errorCount = [int] $counters.error
$aborted = [int] $counters.aborted
$notExecuted = [int] $counters.notExecuted
if ($total -le 0 -or $passed -ne $total -or $failed -ne 0 -or $errorCount -ne 0 -or $aborted -ne 0 -or $notExecuted -ne 0) {
    throw "TRX verification failed: total=$total passed=$passed failed=$failed."
}

Write-Host "Family management E2E passed: total=$total passed=$passed failed=$failed"
