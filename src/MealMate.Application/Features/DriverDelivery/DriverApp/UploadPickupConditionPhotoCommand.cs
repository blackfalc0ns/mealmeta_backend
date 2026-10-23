using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record UploadPickupConditionPhotoCommand(
    Guid BoxId,
    string ValidationToken,
    string FileName,
    string ContentType,
    Stream ContentStream) : ICommand<UploadPickupConditionPhotoResponseDto>;

public sealed class UploadPickupConditionPhotoCommandHandler
    : ICommandHandler<UploadPickupConditionPhotoCommand, UploadPickupConditionPhotoResponseDto>
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MiB

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly ILocalizedMessageProvider _messages;
    private readonly MealMate.Application.Common.Storage.SupportEvidenceScanService? _evidenceScans;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public UploadPickupConditionPhotoCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        ILocalizedMessageProvider messages,
        MealMate.Application.Common.Storage.SupportEvidenceScanService? evidenceScans = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _messages = messages;
        _evidenceScans = evidenceScans;
        _transactions = transactions;
        _locks = locks;
    }

    public async Task<Result<UploadPickupConditionPhotoResponseDto>> Handle(
        UploadPickupConditionPhotoCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value && !d.IsDeleted, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }
        if (!(await _db.GetActiveAccountUserIdsAsync(new[] { driver.UserId }, cancellationToken)).Contains(driver.UserId))
            return Error.Forbidden("DRIVER_ACCOUNT_INACTIVE", _messages.GetMessage("driver.account_inactive", "The driver account is not active."));

        if (string.IsNullOrWhiteSpace(request.ValidationToken))
        {
            return Error.Validation("VALIDATION_TOKEN_INVALID", "Validation token is required.");
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ValidationToken.Trim()))).ToLowerInvariant();

        var validation = await _db.PickupValidations
            .FirstOrDefaultAsync(v => v.BoxId == request.BoxId &&
                                      v.DriverProfileId == driver.Id &&
                                      v.TokenHash == tokenHash, cancellationToken);

        if (validation is null)
        {
            return Error.Validation("VALIDATION_TOKEN_INVALID", "Invalid validation token for this box.");
        }

        if (validation.ExpiresAtUtc < DateTime.UtcNow)
        {
            return Error.Validation("VALIDATION_TOKEN_EXPIRED", "Validation token has expired. Please re-scan barcode.");
        }

        if (validation.ConsumedAtUtc.HasValue)
        {
            return Error.Validation("VALIDATION_TOKEN_CONSUMED", "Validation token has already been consumed.");
        }

        // Verify active assignment unchanged
        var activeAssignmentExists = await _db.DriverAssignments
            .AsNoTracking()
            .AnyAsync(da => da.Id == validation.DriverAssignmentId && da.DriverProfileId == driver.Id
                && da.TripId == validation.TripId && da.Status == "Active", cancellationToken);

        if (!activeAssignmentExists)
        {
            return Error.Forbidden("BOX_ASSIGNMENT_CHANGED", "Driver assignment for this trip has changed.");
        }

        // Validate content type & size
        var contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (contentType != "image/jpeg" && contentType != "image/jpg" && contentType != "image/png" && contentType != "image/webp")
        {
            return Error.Validation("PHOTO_INVALID_TYPE", "Only JPEG, PNG, and WebP images are allowed.");
        }

        if (request.ContentStream is null)
        {
            return Error.Validation("PHOTO_REQUIRED", "Condition photo file stream is required.");
        }

        // Buffer under the ingress limit before saving; request streams may not be seekable.
        await using var boundedContent = new MemoryStream();
        var copyBuffer = new byte[81920];
        int copied;
        while ((copied = await request.ContentStream.ReadAsync(copyBuffer, cancellationToken)) > 0)
        {
            if (boundedContent.Length + copied > MaxFileSizeBytes)
                return Error.Validation("PHOTO_TOO_LARGE", "Condition photo must not exceed 10 MiB.");
            await boundedContent.WriteAsync(copyBuffer.AsMemory(0, copied), cancellationToken);
        }
        if (boundedContent.Length == 0)
            return Error.Validation("PHOTO_REQUIRED", "Condition photo file stream is required.");
        var contentBytes = boundedContent.ToArray();
        var header = contentBytes.AsSpan(0, Math.Min(12, contentBytes.Length)).ToArray();
        var read = header.Length;

        if (!IsValidImageSignature(header, read, contentType))
        {
            return Error.Validation("PHOTO_INVALID_SIGNATURE", "File content does not match the declared image format.");
        }

        var folder = $"uploads/drivers/delivery/pickup-condition/{validation.TripId}/{validation.BoxId}";
        await using var contentToSave = new MemoryStream(contentBytes, writable: false);
        var storedFile = await _storage.SaveAsync(contentToSave, request.FileName, contentType, folder, cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var photoRecord = new PickupConditionPhoto
        {
            PickupValidationId = validation.Id,
            TripId = validation.TripId,
            BoxId = validation.BoxId,
            DriverProfileId = driver.Id,
            StorageKey = storedFile.StorageKey,
            ContentType = contentType,
            SizeBytes = storedFile.SizeBytes,
            Checksum = storedFile.Checksum,
            UploadedAtUtc = nowUtc
        };

        var uploadRecord = new MealMate.Domain.Common.UploadFileRecord
        {
            StorageKey = storedFile.StorageKey, FileName = Path.GetFileName(request.FileName),
            ContentType = contentType, SizeBytes = storedFile.SizeBytes, Scope = "drivers",
            Folder = "delivery", UploadedByUserId = _currentUser.UserId, UploadedAtUtc = nowUtc,
        };
        _db.UploadFileRecords.Add(uploadRecord);
        await _db.SaveChangesAsync(cancellationToken);
        if (_evidenceScans is null)
            return Error.Failure("UPLOAD_EVIDENCE_SCAN_FAILED", "Evidence scanning is unavailable.");
        var scan = await _evidenceScans.ReadCleanAsync(uploadRecord, cancellationToken);
        if (scan.Bytes is null || !MealMate.Application.Features.UnifiedSupport.SupportCaseService.HasValidSignature(scan.Bytes, contentType))
            return Error.Failure("UPLOAD_EVIDENCE_SCAN_FAILED", "The condition photo could not be verified as clean.");
        if (_transactions is null || _locks is null)
            return Error.Failure("PICKUP_INFRASTRUCTURE_UNAVAILABLE", "Pickup photo confirmation requires transaction and lock support.");

        var committed = await _transactions.ExecuteResultAsync(async ct =>
        {
            await _locks.LockTripAsync(validation.TripId, ct);
            var currentValidation = await _db.PickupValidations.FirstOrDefaultAsync(v => v.Id == validation.Id
                && v.BoxId == request.BoxId && v.DriverProfileId == driver.Id && v.TokenHash == tokenHash, ct);
            if (currentValidation is null || currentValidation.ExpiresAtUtc < DateTime.UtcNow
                || currentValidation.ConsumedAtUtc.HasValue)
                return Error.Conflict("VALIDATION_CHANGED", "The pickup validation changed while the photo was being verified.");
            var assignmentStillActive = await _db.DriverAssignments.AsNoTracking().AnyAsync(da =>
                da.Id == currentValidation.DriverAssignmentId && da.DriverProfileId == driver.Id
                && da.TripId == currentValidation.TripId && da.Status == "Active", ct);
            if (!assignmentStillActive)
                return Error.Conflict("BOX_ASSIGNMENT_CHANGED", "Driver assignment for this trip has changed.");

            var previous = await _db.PickupConditionPhotos
                .Where(p => p.PickupValidationId == currentValidation.Id && p.ConfirmedAtUtc == null && p.DeletedAtUtc == null)
                .ToListAsync(ct);
            _db.PickupConditionPhotos.Add(photoRecord);
            await _db.SaveChangesAsync(ct);

            foreach (var oldPhoto in previous)
            {
                try
                {
                    await _storage.DeleteAsync(oldPhoto.StorageKey, ct);
                    oldPhoto.DeletedAtUtc = nowUtc;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* Cleanup failure does not invalidate the newly verified photo. */ }
            }
            if (previous.Count > 0) await _db.SaveChangesAsync(ct);
            return Result.Success(new UploadPickupConditionPhotoResponseDto(
                BoxId: currentValidation.BoxId, StorageKey: storedFile.StorageKey, ContentType: contentType,
                SizeBytes: storedFile.SizeBytes, UploadedAtUtc: nowUtc, ScanStatus: "PhotoUploaded",
                NextAction: "ConfirmPickup", ReadUrl: $"/api/v1/uploads/content?key={Uri.EscapeDataString(storedFile.StorageKey)}"));
        }, cancellationToken);
        if (!committed.IsSuccess)
        {
            try { await _storage.DeleteAsync(storedFile.StorageKey, cancellationToken); } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; } catch { }
            return committed.Error;
        }
        return committed;
    }

    private static bool IsValidImageSignature(byte[] header, int read, string contentType)
    {
        if (read < 3) return false;

        // JPEG: FF D8 FF
        if ((contentType == "image/jpeg" || contentType == "image/jpg") &&
            header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (contentType == "image/png" && read >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return true;
        }

        // WebP: RIFF....WEBP
        if (contentType == "image/webp" && read >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return true;
        }

        return false;
    }
}
