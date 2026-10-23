using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Persistence.Common;

public sealed class SupportEvidenceScanReceiptStore(IServiceScopeFactory scopeFactory) : ISupportEvidenceScanReceiptStore
{
    public async Task<EvidenceScanResult> GetOrScanAsync(Guid uploadFileRecordId, string sha256, long sizeBytes,
        string contentType, int validationVersion, Func<CancellationToken, Task<EvidenceScanResult>> scan,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            var key = $"evidence-scan:{uploadFileRecordId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }

        var receipts = db.Set<SupportEvidenceScanReceipt>();
        var matching = await receipts.AsNoTracking().Where(x => x.UploadFileRecordId == uploadFileRecordId
                && x.Sha256 == sha256 && x.SizeBytes == sizeBytes && x.ContentType == contentType
                && x.ValidationVersion == validationVersion)
            .OrderByDescending(x => x.ScannedAtUtc).ToListAsync(cancellationToken);
        var rejected = matching.FirstOrDefault(x => x.Verdict == nameof(EvidenceScanVerdict.Rejected));
        if (rejected is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(EvidenceScanVerdict.Rejected, rejected.Provider, "Evidence was rejected by the scanner.", rejected.FailureCode);
        }
        var clean = matching.FirstOrDefault(x => x.Verdict == nameof(EvidenceScanVerdict.Clean));
        if (clean is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(EvidenceScanVerdict.Clean, clean.Provider, FailureCode: clean.FailureCode);
        }

        EvidenceScanResult result;
        try { result = await scan(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Net.Sockets.SocketException)
        { result = new(EvidenceScanVerdict.Unavailable, "clamd", ex.GetType().Name); }

        receipts.Add(new SupportEvidenceScanReceipt
        {
            UploadFileRecordId = uploadFileRecordId,
            Sha256 = sha256,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            Verdict = result.Verdict.ToString(),
            Provider = result.Provider,
            FailureCode = result.FailureCode,
            ScannedAtUtc = DateTime.UtcNow,
            ValidationVersion = validationVersion,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
        });
    }

    public async Task AppendAsync(Guid uploadFileRecordId, string sha256, long sizeBytes, string contentType,
        int validationVersion, EvidenceScanResult result, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            var key = $"evidence-scan:{uploadFileRecordId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
        if (result.Verdict == EvidenceScanVerdict.Rejected
            && await db.Set<SupportEvidenceScanReceipt>().AnyAsync(x => x.UploadFileRecordId == uploadFileRecordId
                && x.Sha256 == sha256 && x.SizeBytes == sizeBytes && x.ContentType == contentType
                && x.ValidationVersion == validationVersion && x.Verdict == nameof(EvidenceScanVerdict.Rejected), cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        db.Set<SupportEvidenceScanReceipt>().Add(new SupportEvidenceScanReceipt
        {
            UploadFileRecordId = uploadFileRecordId, Sha256 = sha256, SizeBytes = sizeBytes,
            ContentType = contentType, Verdict = result.Verdict.ToString(), Provider = result.Provider,
            FailureCode = result.FailureCode,
            ScannedAtUtc = DateTime.UtcNow, ValidationVersion = validationVersion,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        });
    }
}
