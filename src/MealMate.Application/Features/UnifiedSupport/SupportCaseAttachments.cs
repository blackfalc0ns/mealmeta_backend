using MealMate.Application.Common.Models;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed partial class SupportCaseService
{
    private const long MaxAttachmentBytes = 25 * 1024 * 1024;
    private const int MaxAttachmentsPerMessage = 5;
    private const string SupportEvidenceFolder = "documents/support/evidence";

    public sealed record SupportCaseAttachmentUploadDto(string StorageKey, string FileName, string ContentType, long SizeBytes);

    public async Task<Result<SupportCaseAttachmentUploadDto>> UploadAttachmentAsync(
        string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        if (ActorId is not { } actorId) return Unauthorized();
        if (_currentUser.UserType == UserType.Driver && !await _access.HasActiveDriverIdentityAsync(ct)) return Forbidden();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedContent(contentType, extension) || string.IsNullOrWhiteSpace(Path.GetFileName(fileName))
            || fileName.Length > 255) return Error.Validation(SupportCaseProblemCodes.AttachmentUnsupported);

        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxAttachmentBytes) return Error.Validation(SupportCaseProblemCodes.AttachmentUnsupported);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (buffer.Length == 0 || !HasValidSignature(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), contentType))
            return Error.Validation(SupportCaseProblemCodes.AttachmentUnsupported);

        buffer.Position = 0;
        StoredFile stored;
        try { stored = await _storage.SaveAsync(buffer, Path.GetFileName(fileName), contentType, SupportEvidenceFolder, ct); }
        catch (InvalidOperationException) { return Error.Failure(SupportCaseProblemCodes.AttachmentStorageUnavailable); }
        catch (IOException) { return Error.Failure(SupportCaseProblemCodes.AttachmentStorageUnavailable); }

        if (!IsSupportEvidenceKey(stored.StorageKey) || stored.SizeBytes != buffer.Length)
        {
            await _storage.DeleteAsync(stored.StorageKey, ct);
            return Error.Failure(SupportCaseProblemCodes.AttachmentStorageUnavailable);
        }

        var upload = new MealMate.Domain.Common.UploadFileRecord
        {
            StorageKey = stored.StorageKey, FileName = stored.FileName, ContentType = contentType,
            SizeBytes = stored.SizeBytes, Scope = "support", Folder = "evidence", UploadedByUserId = actorId,
            UploadedAtUtc = Now,
        };
        _db.UploadFileRecords.Add(upload);
        try { await _unitOfWork.SaveChangesAsync(ct); }
        catch
        {
            await _storage.DeleteAsync(stored.StorageKey, ct);
            throw;
        }
        var scan = await _evidenceScans.ReadCleanAsync(upload, ct);
        if (scan.Scan.Verdict == EvidenceScanVerdict.Unavailable)
            return Error.Failure(SupportCaseProblemCodes.AttachmentScanUnavailable);
        if (scan.Scan.Verdict != EvidenceScanVerdict.Clean)
            return Error.Validation(SupportCaseProblemCodes.AttachmentRejected);
        return Result.Success(new SupportCaseAttachmentUploadDto(stored.StorageKey, stored.FileName, contentType, stored.SizeBytes));
    }

    public async Task<Result<SupportCaseAttachmentFile>> GetAttachmentAsync(Guid caseId, Guid messageId, int index, CancellationToken ct = default)
    {
        var actorId = ActorId;
        if (actorId is null) return Unauthorized();
        var supportCase = await _db.SupportCases.AsNoTracking().SingleOrDefaultAsync(c => c.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        var message = await _db.SupportCaseMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId && m.SupportCaseId == caseId, ct);
        if (message is null || index < 0 || index >= message.AttachmentKeys.Length || !await CanReadMessageAsync(supportCase, message, actorId.Value, ct))
            return NotFound();
        var key = message.AttachmentKeys[index];
        var upload = await _db.UploadFileRecords.AsNoTracking().SingleOrDefaultAsync(x => x.StorageKey == key, ct);
        if (upload is null || !IsSupportEvidenceRecord(upload) || !SupportedContent(upload.ContentType, Path.GetExtension(upload.FileName).ToLowerInvariant())
            || upload.SizeBytes <= 0 || upload.SizeBytes > MaxAttachmentBytes || !await _storage.ExistsAsync(key, ct)) return NotFound();
        var scan = await _evidenceScans.ReadCleanAsync(upload, ct);
        if (scan.Bytes is null || !HasValidSignature(scan.Bytes, upload.ContentType)) return NotFound();
        return Result.Success(new SupportCaseAttachmentFile(scan.Bytes, SafeDownloadName(upload.FileName), upload.ContentType));
    }

    private async Task<bool> CanReadMessageAsync(SupportCase supportCase, SupportCaseMessage message, Guid actorId, CancellationToken ct)
    {
        if (message.Visibility == SupportCaseMessageVisibility.Internal)
            return _access.CanReadInternal && await _access.HasPersistedPermissionAsync(
                MealMate.Application.Common.Security.PermissionCodes.SupportInternal, ct);
        var restaurantRequester = _currentUser.UserType == UserType.Restaurant && supportCase.Channel == SupportCaseChannel.Restaurant
            && supportCase.RestaurantId == _currentUser.RestaurantId;
        if (supportCase.RequesterUserId == actorId || restaurantRequester) return message.SupportCaseTaskId is null;
        if (_access.IsSupportAdmin || _access.IsSuperAdmin) return true;
        if (message.SupportCaseTaskId is not { } taskId) return false;
        var task = await _db.SupportCaseTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId && t.SupportCaseId == supportCase.Id, ct);
        if (task is null || task.Status == SupportCaseTaskStatus.Cancelled) return false;
        if (_currentUser.UserType == UserType.Restaurant)
            return task.RecipientType == SupportCaseTaskRecipientType.Restaurant
                && task.RestaurantId != null && task.RestaurantId == _currentUser.RestaurantId;
        if (_currentUser.UserType == UserType.DeliveryManager && _currentUser.RestaurantId is { } restaurantId)
            return task.RecipientType == SupportCaseTaskRecipientType.DeliveryManager && task.RecipientUserId == actorId
                && task.RestaurantId == restaurantId && await _access.IsCurrentDeliveryManagerForRestaurantAsync(restaurantId, ct);
        return false;
    }

    private async Task<bool> ValidateAttachmentKeysAsync(IReadOnlyList<string> keys, Guid actorId, CancellationToken ct)
    {
        var discoveredRows = await _db.UploadFileRecords.AsNoTracking().Where(x => keys.Contains(x.StorageKey)).ToListAsync(ct);
        if (discoveredRows.Count != keys.Count) return false;

        foreach (var uploadId in discoveredRows.Select(row => row.Id).OrderBy(id => id))
            await _locks.LockSupportEvidenceUploadAsync(uploadId, ct);

        // Read again after obtaining the same transaction-scoped locks used by cleanup.
        var rows = await _db.UploadFileRecords.AsNoTracking().Where(x => keys.Contains(x.StorageKey)).ToListAsync(ct);
        if (rows.Count != keys.Count) return false;
        foreach (var row in rows)
        {
            if (!IsSupportEvidenceRecord(row) || row.UploadedByUserId != actorId || row.SizeBytes <= 0 || row.SizeBytes > MaxAttachmentBytes
                || row.SupportPayloadDeletedAtUtc is not null
                || !SupportedContent(row.ContentType, Path.GetExtension(row.FileName).ToLowerInvariant()) || !await _storage.ExistsAsync(row.StorageKey, ct)) return false;
            var scan = await _evidenceScans.ReadCleanAsync(row, ct);
            if (scan.Bytes is null || !HasValidSignature(scan.Bytes, row.ContentType)) return false;
        }
        return true;
    }

    private static bool IsSupportEvidenceRecord(MealMate.Domain.Common.UploadFileRecord upload)
        => string.Equals(upload.Scope, "support", StringComparison.Ordinal)
            && string.Equals(upload.Folder, "evidence", StringComparison.Ordinal)
            && IsSupportEvidenceKey(upload.StorageKey);

    private static bool IsSupportEvidenceKey(string key)
        => key.StartsWith("documents/support/evidence/", StringComparison.Ordinal)
            && !key.Contains('\\') && !key.Contains("..", StringComparison.Ordinal)
            && !Uri.TryCreate(key, UriKind.Absolute, out _);

    private static bool SupportedContent(string contentType, string extension)
        => (contentType, extension) is ("image/png", ".png") or ("image/jpeg", ".jpg") or ("image/jpeg", ".jpeg")
            or ("image/webp", ".webp") or ("application/pdf", ".pdf");

    internal static bool HasValidSignature(ReadOnlySpan<byte> bytes, string contentType)
        => contentType switch
        {
            "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            "application/pdf" => bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8),
            _ => false,
        };

    private static string SafeDownloadName(string value)
    {
        var name = Path.GetFileName(value).Replace('\\', '_').Replace('"', '_').Replace('\r', '_').Replace('\n', '_');
        return string.IsNullOrWhiteSpace(name) ? "support-attachment" : name;
    }
}
