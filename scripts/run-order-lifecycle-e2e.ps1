<#
.SYNOPSIS
    Runs the End-to-End order lifecycle test suite against an isolated PostgreSQL container.
.DESCRIPTION
    Preflight checks:
      1. Verifies Docker daemon is running and reachable.
      2. Verifies .NET 9 SDK is installed.
      3. Verifies isolated test environment (no live payment gateway keys / production db connection).
    Execution:
      Runs MealMate.ApiTests filtering to OrderLifecycleEndToEndTests with TRX and console logging.
    Reporting:
      Extracts and reports authoritative test counts (Total, Passed, Failed, Skipped).
      Returns exit code 0 on full pass, non-zero (1) on failure.
#>

[CmdletBinding()]
param(
    [switch]$Detailed = $false
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " MealMate End-to-End Order Lifecycle Test Runner" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 1. Preflight: Docker check
Write-Host "`n[PREFLIGHT] Checking Docker daemon..." -ForegroundColor Yellow
try {
    $dockerInfo = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[FAIL] Docker is not running or not responsive. Please start Docker Desktop first."
        exit 1
    }
    Write-Host "[OK] Docker daemon is running and healthy." -ForegroundColor Green
} catch {
    Write-Error "[FAIL] Docker CLI not found or failed to execute: $_"
    exit 1
}

# 2. Preflight: .NET SDK check
Write-Host "[PREFLIGHT] Checking .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "[OK] .NET SDK version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Error "[FAIL] dotnet CLI not found in PATH."
    exit 1
}

# 3. Preflight: Environment isolation & safety check
Write-Host "[PREFLIGHT] Checking test safety and environment isolation..." -ForegroundColor Yellow
$liveKeyDetected = $false
if ($env:MYFATOORAH_API_KEY -and $env:MYFATOORAH_API_KEY -notmatch "^test_") {
    $liveKeyDetected = $true
}
if ($env:MEALMATE_DB_CONNECTION -and $env:MEALMATE_DB_CONNECTION -notmatch "localhost|127\.0\.0\.1|testcontainers") {
    Write-Error "[SECURITY ALERT] MEALMATE_DB_CONNECTION points to a non-local database! Execution halted."
    exit 1
}
if ($liveKeyDetected) {
    Write-Error "[SECURITY ALERT] Non-test MYFATOORAH_API_KEY detected! Execution halted."
    exit 1
}
Write-Host "[OK] Environment isolated. PostgreSQL Testcontainers & test spies will be used exclusively." -ForegroundColor Green

# 4. Resolve test project
$testProjectCandidates = @(
    (Join-Path $PSScriptRoot "..\tests\MealMate.ApiTests\MealMate.ApiTests.csproj"),
    (Join-Path $PSScriptRoot "..\backend\tests\MealMate.ApiTests\MealMate.ApiTests.csproj"),
    (Join-Path $PSScriptRoot "tests\MealMate.ApiTests\MealMate.ApiTests.csproj")
)

$testProject = $null
foreach ($candidate in $testProjectCandidates) {
    if (Test-Path $candidate) {
        $testProject = (Resolve-Path $candidate).Path
        break
    }
}

if (-not $testProject) {
    Write-Error "[FAIL] Could not locate MealMate.ApiTests.csproj in candidate paths."
    exit 1
}

Write-Host "`n[TARGET] Test project: $testProject" -ForegroundColor Cyan

# 5. Target test execution
Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " Executing End-to-End Tests via dotnet test..." -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$resultsDir = Join-Path ([System.IO.Path]::GetDirectoryName($testProject)) "TestResults"
if (Test-Path $resultsDir) {
    Get-ChildItem -Path $resultsDir -Filter "*.trx" -ErrorAction SilentlyContinue | Remove-Item -Force
}

$trxFileName = "order-lifecycle-e2e-$([System.DateTime]::UtcNow.ToString('yyyyMMddHHmmss')).trx"

$testArgs = @(
    "test",
    $testProject,
    "--filter", "FullyQualifiedName~OrderLifecycleEndToEndTests",
    "--logger", "console;verbosity=normal",
    "--logger", "trx;LogFileName=$trxFileName"
)

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath "dotnet" -ArgumentList $testArgs -NoNewWindow -PassThru -Wait
$stopwatch.Stop()

$durationSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 2)

# 6. Parse and report test results
$trxPath = Join-Path $resultsDir $trxFileName
$totalCount = 0
$passedCount = 0
$failedCount = 0
$skippedCount = 0

if (Test-Path $trxPath) {
    try {
        [xml]$trxXml = Get-Content -Path $trxPath -Raw
        $counters = $trxXml.TestRun.ResultSummary.Counters
        if ($counters) {
            $totalCount = [int]$counters.total
            $passedCount = [int]$counters.passed
            $failedCount = [int]$counters.failed
            $skippedCount = [int]$counters.notExecuted
        }

        Write-Host "`n================================================================" -ForegroundColor Cyan
        Write-Host " Test Execution Summary" -ForegroundColor Cyan
        Write-Host "================================================================" -ForegroundColor Cyan
        Write-Host " Total Tests : $totalCount" -ForegroundColor White
        Write-Host " Passed      : $passedCount" -ForegroundColor Green
        Write-Host " Failed      : $failedCount" -ForegroundColor $(if ($failedCount -eq 0) { "Green" } else { "Red" })
        Write-Host " Skipped     : $skippedCount" -ForegroundColor $(if ($skippedCount -eq 0) { "Green" } else { "Yellow" })
        Write-Host " Duration    : ${durationSeconds}s" -ForegroundColor White
        Write-Host " Exit Code   : $($process.ExitCode)" -ForegroundColor $(if ($process.ExitCode -eq 0) { "Green" } else { "Red" })
        Write-Host "================================================================" -ForegroundColor Cyan

        if ($Detailed) {
            Write-Host "`nDetailed Test Breakdown:" -ForegroundColor Cyan
            $unitTestResults = $trxXml.TestRun.Results.UnitTestResult
            foreach ($res in $unitTestResults) {
                $color = if ($res.outcome -eq "Passed") { "Green" } else { "Red" }
                Write-Host " - [$($res.outcome)] $($res.testName) ($($res.duration))" -ForegroundColor $color
            }
        }
    } catch {
        Write-Warning "Could not parse TRX result file: $_"
    }
} else {
    Write-Warning "TRX result file not found at $trxPath. Relying on process exit code."
    Write-Host "`nExit Code: $($process.ExitCode)" -ForegroundColor $(if ($process.ExitCode -eq 0) { "Green" } else { "Red" })
}

if ($process.ExitCode -ne 0 -or $failedCount -gt 0) {
    Write-Host "`n[RESULT: FAIL] One or more End-to-End lifecycle tests failed ($failedCount failed / $totalCount total)." -ForegroundColor Red
    exit 1
} elseif ($totalCount -eq 0 -and $process.ExitCode -eq 0) {
    Write-Host "`n[RESULT: WARNING] 0 tests were executed. Check test filter." -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "`n[RESULT: PASS] All End-to-End order lifecycle tests completed successfully ($passedCount passed / $totalCount total)!" -ForegroundColor Green
    exit 0
}
