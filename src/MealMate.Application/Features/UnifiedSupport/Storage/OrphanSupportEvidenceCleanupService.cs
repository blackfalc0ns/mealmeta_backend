using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.UnifiedSupport.Storage;

/// <summary>Deletes old support-evidence payloads that were never attached to a message.</summary>
public sealed class OrphanSupportEvidenceCleanupService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan UnattachedGracePeriod = TimeSpan.FromHours(24);

    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IApplicationLockManager _locks;
    private readonly IApplicationTransactionExecutor _transactions;
    private readonly ILogger<OrphanSupportEvidenceCleanupService> _logger;

    public OrphanSupportEvidenceCleanupService(
        IAppDbContext db,
        IFileStorage storage,
        IApplicationLockManager locks,
        IApplicationTransactionExecutor transactions,
        ILogger<OrphanSupportEvidenceCleanupService> logger)
    {
        _db = db;
        _storage = storage;
        _locks = locks;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<int> RunCleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        var cutoffUtc = DateTime.UtcNow.Subtract(UnattachedGracePeriod);
        var candidateIds = await _db.UploadFileRecords.AsNoTracking()
            .Where(record => record.StorageKey.StartsWith("documents/support/evidence/")
                && record.Scope == "support" && record.Folder == "evidence"
                && record.UploadedAtUtc < cutoffUtc
                && record.SupportPayloadDeletedAtUtc == null
                && !_db.SupportCaseMessages.Any(message => message.AttachmentKeys.Contains(record.StorageKey)))
            .OrderBy(record => record.SupportCleanupLastAttemptAtUtc ?? record.UploadedAtUtc)
            .ThenBy(record => record.Id)
            .Select(record => record.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;
        foreach (var uploadId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UploadFileRecord? trackedRecord = null;
            bool deleted;
            try
            {
                deleted = await _transactions.ExecuteAsync(async transactionToken =>
                {
                    await _locks.LockSupportEvidenceUploadAsync(uploadId, transactionToken);
                    var record = await _db.UploadFileRecords.SingleOrDefaultAsync(row => row.Id == uploadId, transactionToken);
                    if (record is null) return false;
                    trackedRecord = record;
                    await _db.ReloadEntityAsync(record, transactionToken);
                    if (record.SupportPayloadDeletedAtUtc is not null
                        || !await IsOldUnreferencedSupportEvidence(record, cutoffUtc, transactionToken))
                        return false;

                    record.SupportCleanupLastAttemptAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(transactionToken);

                    try
                    {
                        await _storage.DeleteAsync(record.StorageKey, transactionToken);
                    }
                    catch (OperationCanceledException) when (transactionToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception,
                            "Failed to delete unreferenced support evidence payload {StorageKey}; cleanup will retry.", record.StorageKey);
                        return false;
                    }

                    var hasReceipts = await _db.SupportEvidenceScanReceipts
                        .AnyAsync(receipt => receipt.UploadFileRecordId == record.Id, transactionToken);
                    if (hasReceipts)
                    {
                        record.SupportPayloadDeletedAtUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        _db.UploadFileRecords.Remove(record);
                    }

                    await _db.SaveChangesAsync(transactionToken);
                    return true;
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception,
                    "Failed to process unreferenced support evidence upload {UploadFileRecordId}; cleanup will retry.", uploadId);
                deleted = false;
            }
            finally
            {
                if (trackedRecord is not null)
                    _db.DetachTrackedEntity(trackedRecord);
            }

            if (deleted) deletedCount++;
        }

        if (deletedCount > 0)
            _logger.LogInformation("Support evidence cleanup removed {DeletedCount} unreferenced payload(s).", deletedCount);

        return deletedCount;
    }

    private async Task<bool> IsOldUnreferencedSupportEvidence(
        UploadFileRecord record,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        if (!record.StorageKey.StartsWith("documents/support/evidence/", StringComparison.Ordinal)
            || record.Scope != "support" || record.Folder != "evidence"
            || record.UploadedAtUtc >= cutoffUtc)
            return false;

        // Protect all already-bound evidence, including records with stale historical scan receipts.
        return !await _db.SupportCaseMessages.AnyAsync(
            message => message.AttachmentKeys.Contains(record.StorageKey), cancellationToken);
    }
}
