using MealMate.Application.Common.Abstractions;

namespace MealMate.ApiTests.Support;

/// <summary>Deterministic scanner used only by opt-in API test fixtures.</summary>
public sealed class SupportEvidenceTestScanner : IEvidenceMalwareScanner
{
    private int _verdict = (int)EvidenceScanVerdict.Clean;
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);
    public void SetVerdict(EvidenceScanVerdict verdict) => Volatile.Write(ref _verdict, (int)verdict);
    public void Reset()
    {
        Volatile.Write(ref _verdict, (int)EvidenceScanVerdict.Clean);
        Volatile.Write(ref _calls, 0);
    }

    public Task<EvidenceScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _calls);
        var verdict = (EvidenceScanVerdict)Volatile.Read(ref _verdict);
        return Task.FromResult(verdict switch
        {
            EvidenceScanVerdict.Clean => new EvidenceScanResult(verdict, "test-only", FailureCode: "clean"),
            EvidenceScanVerdict.Rejected => new EvidenceScanResult(verdict, "test-only", "Controlled rejection.", "malware_found"),
            EvidenceScanVerdict.Unavailable => new EvidenceScanResult(verdict, "test-only", "Controlled unavailable response.", "scanner_unavailable"),
            _ => new EvidenceScanResult(EvidenceScanVerdict.Unavailable, "test-only", "Unsupported fixture verdict.", "fixture_invalid"),
        });
    }
}
