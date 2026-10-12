param(
    [ValidateSet('All', 'Api', 'Unit')]
    [string]$Suite = 'All',
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path -Parent $PSScriptRoot
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $backendRoot ('TestResults/voice-backend-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null

# Local fixtures only. No production accounts, calls, credentials or provider sends.
$suites = @(
    @{ Name = 'Api'; Project = 'tests/MealMate.ApiTests/MealMate.ApiTests.csproj'; Filter = 'FullyQualifiedName~VoiceCallV2LifecycleApiTests|FullyQualifiedName~VoiceCallV2TransportTests|FullyQualifiedName~VoiceCallV2WireContractApiTests|FullyQualifiedName~VoiceCallV2InitiationApiTests|FullyQualifiedName~VoiceCallDeviceSessionApiTests|FullyQualifiedName~VoiceCallV2ExpiryApiTests|FullyQualifiedName~VoiceCallLeaseRecoveryApiTests|FullyQualifiedName~VoiceCallV2HangupApiTests' },
    @{ Name = 'Unit'; Project = 'tests/MealMate.UnitTests/MealMate.UnitTests.csproj'; Filter = 'FullyQualifiedName~VoiceCallSignalingTests|FullyQualifiedName~VoiceCallV2PushTransportTests' }
)

Push-Location $backendRoot
try {
    foreach ($entry in $suites) {
        if ($Suite -ne 'All' -and $Suite -ne $entry.Name) { continue }
        Write-Host ('Testing voice backend: ' + $entry.Name)
        & dotnet test $entry.Project --artifacts-path (Join-Path $ResultsDirectory 'build') --filter $entry.Filter --logger ('trx;LogFileName=voice-' + $entry.Name.ToLowerInvariant() + '.trx') --results-directory $ResultsDirectory --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw ('Voice backend ' + $entry.Name + ' tests failed; inspect ' + $ResultsDirectory) }
        $trxPath = Join-Path $ResultsDirectory ('voice-' + $entry.Name.ToLowerInvariant() + '.trx')
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $counts = $trx.TestRun.ResultSummary.Counters
        if ([int]$counts.total -eq 0 -or [int]$counts.passed -ne [int]$counts.total) {
            throw ('Not all selected tests executed and passed; inspect ' + $trxPath)
        }
    }
    Write-Host ('Results: ' + $ResultsDirectory)
    Write-Host 'Scope: local API lifecycle, device authorization, SignalR relay, TURN credential generation and push payloads. Live TURN connectivity, provider delivery and audible media are not exercised.'
}
finally {
    Pop-Location
}
