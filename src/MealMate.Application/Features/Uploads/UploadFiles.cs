using System.Text.RegularExpressions;
using FluentValidation;
using MealMate.Application.Common.Storage;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Validation;

namespace MealMate.Application.Features.Uploads;

public sealed record UploadedFileDto(
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum,
    string ReadUrl,
    string? Folder,
    string? Scope);

public sealed record UploadFileCommand(
    string FileName,
    string ContentType,
    Stream Content,
    string? Folder = null,
    string? Scope = null) : ICommand<UploadedFileDto>;

public sealed class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    private static readonly Regex SafeSegmentRegex = new(
        @"^[a-zA-Z0-9_\-/]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public UploadFileCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);

        RuleFor(x => x.Folder)
            .MaximumLength(100)
            .WithMessageCode(MessageCodes.Validation.MaxLength)
            .Must(f => f is null || SafeSegmentRegex.IsMatch(f))
            .WithMessage("Folder contains invalid characters. Only letters, digits, dashes, underscores, and slashes are allowed.")
            .When(x => x.Folder is not null);

        RuleFor(x => x.Scope)
            .MaximumLength(100)
            .WithMessageCode(MessageCodes.Validation.MaxLength)
            .Must(s => s is null || SafeSegmentRegex.IsMatch(s))
            .WithMessage("Scope contains invalid characters. Only letters, digits, dashes, underscores, and slashes are allowed.")
            .When(x => x.Scope is not null);
    }
}

public sealed class UploadFileCommandHandler : ICommandHandler<UploadFileCommand, UploadedFileDto>
{
    private const long MaxBytes = 25 * 1024 * 1024;

    private readonly IFileStorage _storage;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MealMate.Application.Common.Storage.SupportEvidenceScanService _evidenceScans;

    public UploadFileCommandHandler(IFileStorage storage, IAppDbContext db, ICurrentUser currentUser,
        MealMate.Application.Common.Storage.SupportEvidenceScanService evidenceScans)
    {
        _storage = storage;
        _db = db;
        _currentUser = currentUser;
        _evidenceScans = evidenceScans;
    }

    public async Task<Result<UploadedFileDto>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        // Reject empty file stream if seekable and length is zero
        if (request.Content.CanSeek && request.Content.Length == 0)
        {
            return Error.Validation(
                MessageCodes.Validation.RequiredField,
                "Uploaded file is empty.");
        }

        // Sanitize folder and scope: only safe path segments
        var sanitizedFolder = SanitizeSegment(request.Folder) ?? "misc";
        var sanitizedScope = SanitizeSegment(request.Scope) ?? "general";

        // Build folder path for storage
        var storageFolder = $"uploads/{sanitizedScope}/{sanitizedFolder}";
        var protectedEvidence = MealMate.Application.Common.Storage.ProtectedEvidenceStorage.IsProtectedKey(storageFolder);
        if (protectedEvidence && (_currentUser.UserType != MealMate.Domain.Enums.UserType.Driver || !_currentUser.UserId.HasValue
            || sanitizedScope != "drivers" || sanitizedFolder is not ("delivery" or "issue-evidence")
            || request.ContentType is not ("image/jpeg" or "image/png" or "image/webp")))
            return Error.Forbidden("UPLOAD_PRIVATE_EVIDENCE_SCOPE_REQUIRED", "Private evidence uploads require a driver account and an approved evidence purpose.");

        MemoryStream? protectedBuffer = null;
        if (protectedEvidence)
        {
            protectedBuffer = new MemoryStream();
            var inputChunk = new byte[81920];
            int inputRead;
            while ((inputRead = await request.Content.ReadAsync(inputChunk, cancellationToken)) > 0)
            {
                if (protectedBuffer.Length + inputRead > MaxBytes)
                    return Error.Validation(MessageCodes.Validation.MaxLength, "File exceeds 25MB limit.");
                await protectedBuffer.WriteAsync(inputChunk.AsMemory(0, inputRead), cancellationToken);
            }
            var incomingBytes = protectedBuffer.ToArray();
            if (incomingBytes.Length == 0 || !MealMate.Application.Features.UnifiedSupport.SupportCaseService.HasValidSignature(incomingBytes, request.ContentType))
                return Error.Validation(MessageCodes.Validation.RequiredField, "Evidence content does not match its declared image type.");
            protectedBuffer.Position = 0;
        }

        // Save through IFileStorage
        StoredFile stored;
        try
        {
            stored = await _storage.SaveAsync(
                (Stream?)protectedBuffer ?? request.Content,
                request.FileName,
                request.ContentType,
                storageFolder,
                cancellationToken);
        }
        catch (Exception ex)
        {
            protectedBuffer?.Dispose();
            return Error.Failure(
                MessageCodes.Common.ServerError,
                $"Failed to save file: {ex.Message}");
        }
        protectedBuffer?.Dispose();

        // Check final size after save
        if (stored.SizeBytes > MaxBytes)
        {
            try
            {
                await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            }
            catch
            {
                // Cleanup best-effort; ignore failures.
            }

            return Error.Validation(
                MessageCodes.Validation.MaxLength,
                "File exceeds 25MB limit.");
        }

        var readUrl = CatalogImageStorage.ToContentUrl(stored.StorageKey)
            ?? $"/api/v1/uploads/content?key={Uri.EscapeDataString(stored.StorageKey)}";
        if (Uri.TryCreate(stored.StorageKey, UriKind.Absolute, out var storedUri)
            && (storedUri.Scheme == Uri.UriSchemeHttps || storedUri.Scheme == Uri.UriSchemeHttp)
            && storedUri.Host.Contains("imagekit.io", StringComparison.OrdinalIgnoreCase))
        {
            readUrl = stored.StorageKey;
        }
        else
        {
            try
            {
                var publicUrl = await _storage.GetReadUrlAsync(stored.StorageKey, TimeSpan.FromDays(3650), cancellationToken);
                if (Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    && uri.Host.Contains("imagekit.io", StringComparison.OrdinalIgnoreCase))
                {
                    readUrl = publicUrl;
                }
            }
            catch (InvalidOperationException)
            {
                // Local storage and missing ImageKit URL endpoint keep the API proxy URL.
            }
        }

        // Ownership metadata: persisted so later consumers (delivery proof binding,
        // protected read paths) can verify the key belongs to this uploader. Without
        // this record a storage key is just an unverified string.
        var uploadRecord = new MealMate.Domain.Common.UploadFileRecord
        {
            StorageKey = stored.StorageKey,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            Scope = sanitizedScope,
            Folder = sanitizedFolder,
            UploadedByUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            UploadedAtUtc = DateTime.UtcNow
        };
        _db.UploadFileRecords.Add(uploadRecord);
        await _db.SaveChangesAsync(cancellationToken);

        if (protectedEvidence)
        {
            var scan = await _evidenceScans.ReadCleanAsync(uploadRecord, cancellationToken);
            if (scan.Scan.Verdict == MealMate.Application.Common.Abstractions.EvidenceScanVerdict.Unavailable)
                return Error.Failure("UPLOAD_EVIDENCE_SCAN_UNAVAILABLE", "Evidence scanning is unavailable.");
            if (scan.Scan.Verdict != MealMate.Application.Common.Abstractions.EvidenceScanVerdict.Clean
                || scan.Bytes is null || !MealMate.Application.Features.UnifiedSupport.SupportCaseService.HasValidSignature(scan.Bytes, request.ContentType))
                return Error.Validation("UPLOAD_EVIDENCE_REJECTED", "The evidence was rejected.");
        }

        return new UploadedFileDto(
            stored.StorageKey,
            stored.FileName,
            stored.ContentType,
            stored.SizeBytes,
            stored.Checksum,
            readUrl,
            request.Folder,
            request.Scope);
    }

    private static string? SanitizeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        // Remove any characters that are not letters, digits, dash, underscore, or slash
        var sanitized = Regex.Replace(
            segment,
            @"[^a-zA-Z0-9_\-/]",
            "",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Collapse consecutive slashes
        sanitized = Regex.Replace(sanitized, @"/{2,}", "/", RegexOptions.Compiled);

        // Remove leading/trailing slashes and dots (prevent traversal)
        sanitized = sanitized.Trim('/', '.');

        // Reject if traversal patterns remain
        if (sanitized.Contains("..") || sanitized.StartsWith('/') || sanitized.EndsWith('/'))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }
}
