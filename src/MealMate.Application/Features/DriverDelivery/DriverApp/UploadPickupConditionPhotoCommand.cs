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

    public UploadPickupConditionPhotoCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _messages = messages;
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
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

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
            .AnyAsync(da => da.Id == validation.DriverAssignmentId && da.Status == "Active", cancellationToken);

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

        if (request.ContentStream is null || request.ContentStream.Length == 0)
        {
            return Error.Validation("PHOTO_REQUIRED", "Condition photo file stream is required.");
        }

        if (request.ContentStream.Length > MaxFileSizeBytes)
        {
            return Error.Validation("PHOTO_TOO_LARGE", "Condition photo must not exceed 10 MiB.");
        }

        // Verify magic bytes signature
        request.ContentStream.Position = 0;
        var header = new byte[12];
        var read = await request.ContentStream.ReadAsync(header.AsMemory(0, Math.Min(12, (int)request.ContentStream.Length)), cancellationToken);
        request.ContentStream.Position = 0;

        if (!IsValidImageSignature(header, read, contentType))
        {
            return Error.Validation("PHOTO_INVALID_SIGNATURE", "File content does not match the declared image format.");
        }

        var folder = $"uploads/drivers/delivery/pickup-condition/{validation.TripId}/{validation.BoxId}";
        var storedFile = await _storage.SaveAsync(request.ContentStream, request.FileName, contentType, folder, cancellationToken);

        // Check if a previous unconfirmed photo existed for this validation; if so, replace it
        var existingPhotos = await _db.PickupConditionPhotos
            .Where(p => p.PickupValidationId == validation.Id && p.ConfirmedAtUtc == null && p.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);

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

        _db.PickupConditionPhotos.Add(photoRecord);
        await _db.SaveChangesAsync(cancellationToken);

        // Safely clean up previous replaced unconfirmed files
        foreach (var oldPhoto in existingPhotos)
        {
            try
            {
                await _storage.DeleteAsync(oldPhoto.StorageKey, cancellationToken);
                oldPhoto.DeletedAtUtc = nowUtc;
            }
            catch
            {
                // Non-fatal cleanup log
            }
        }
        if (existingPhotos.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new UploadPickupConditionPhotoResponseDto(
            BoxId: validation.BoxId,
            StorageKey: storedFile.StorageKey,
            ContentType: contentType,
            SizeBytes: storedFile.SizeBytes,
            UploadedAtUtc: nowUtc,
            ScanStatus: "PhotoUploaded",
            NextAction: "ConfirmPickup"));
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
