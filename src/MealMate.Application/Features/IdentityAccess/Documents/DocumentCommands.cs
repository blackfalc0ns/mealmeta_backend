using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Documents;

public sealed record DocumentDto(
    Guid Id,
    DocumentOwnerType OwnerType,
    Guid OwnerId,
    DocumentType DocumentType,
    string FileName,
    string ContentType,
    long SizeBytes,
    VerificationStatus? LatestStatus,
    DateTime? ExpiresAtUtc,
    string? RejectionReason);

/// <summary>Result of a document download request. The stream must be disposed by the caller.</summary>
public sealed record DocumentDownloadResult(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes) : IDisposable
{
    public void Dispose() => Content.Dispose();
}

public sealed record UploadDocumentCommand(
    DocumentOwnerType OwnerType,
    Guid OwnerId,
    DocumentType DocumentType,
    string FileName,
    string ContentType,
    Stream Content,
    DateTime? ExpiresAtUtc = null) : ICommand<DocumentDto>;

public sealed class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
    ];

    private static readonly HashSet<string> AllowedLogoContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
    ];

    public UploadDocumentCommandValidator(IDateTimeProvider? clock = null)
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType)
            .Must(AllowedContentTypes.Contains)
            .WithMessage("Unsupported file type.");

        RuleFor(x => x.ContentType)
            .Must(AllowedLogoContentTypes.Contains)
            .When(x => x.DocumentType is DocumentType.ProfileImage or DocumentType.BranchPhoto)
            .WithMessage("Restaurant and branch photos must be JPEG, PNG, or WebP images.");

        RuleFor(x => x.ExpiresAtUtc)
            .NotNull()
            .WithMessage("Document expiry date is required.")
            .Must(date => date.HasValue && date.Value.Kind != DateTimeKind.Unspecified)
            .WithMessage("Document expiry date must specify a UTC kind.")
            .Must(date =>
            {
                if (!date.HasValue) return false;
                var now = clock?.UtcNow ?? DateTime.UtcNow;
                return date.Value > now;
            })
            .WithMessage("Document expiry date must be a future UTC date.")
            .When(x => x.DocumentType is not (DocumentType.ProfileImage or DocumentType.BranchPhoto));
    }
}

public sealed class UploadDocumentCommandHandler : ICommandHandler<UploadDocumentCommand, DocumentDto>
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;

    public UploadDocumentCommandHandler(
        IAppDbContext db,
        IFileStorage storage,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<DocumentDto>> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        if (!DocumentAccess.CanAccessAnyDocument(_currentUser)
            && !await DocumentAccess.CallerOwnsDocumentTargetAsync(_db, _currentUser, request.OwnerType, request.OwnerId, cancellationToken))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed to upload for this owner.");
        }

        if (request.Content.CanSeek && request.Content.Length > MaxBytes)
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, "File exceeds 10MB limit.");
        }

        if (request.DocumentType == DocumentType.ProfileImage
            && request.Content.CanSeek
            && request.Content.Length > 5 * 1024 * 1024)
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, "Restaurant logo exceeds 5MB limit.");
        }

        var stored = await _storage.SaveAsync(
            request.Content,
            request.FileName,
            request.ContentType,
            PublicImageFolder(request),
            cancellationToken);

        if (stored.SizeBytes > MaxBytes)
        {
            await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return Error.Validation(MessageCodes.Validation.MaxLength, "File exceeds 10MB limit.");
        }


        if (request.DocumentType == DocumentType.ProfileImage && stored.SizeBytes > 5 * 1024 * 1024)
        {
            await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return Error.Validation(MessageCodes.Validation.MaxLength, "Restaurant logo exceeds 5MB limit.");
        }

        var document = new Document
        {
            OwnerType = request.OwnerType,
            OwnerId = request.OwnerId,
            DocumentType = request.DocumentType,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            StoragePath = stored.StorageKey,
            SizeBytes = stored.SizeBytes,
            Checksum = stored.Checksum,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CreatedAtUtc = _clock.UtcNow,
        };

        document.Verifications.Add(new DocumentVerification
        {
            Status = VerificationStatus.Submitted,
            CreatedAtUtc = _clock.UtcNow,
        });

        _db.Documents.Add(document);

        await _audit.LogAsync(new AuditEntry(
            "DocumentUploaded",
            nameof(Document),
            document.Id,
            After: new { document.DocumentType, document.OwnerType, document.OwnerId },
            SourceChannel: "documents"), cancellationToken);

        return new DocumentDto(
            document.Id,
            document.OwnerType,
            document.OwnerId,
            document.DocumentType,
            document.FileName,
            document.ContentType,
            document.SizeBytes,
            VerificationStatus.Submitted,
            document.ExpiresAtUtc,
            null);
    }

    private static string PublicImageFolder(UploadDocumentCommand request)
        => request.DocumentType switch
        {
            DocumentType.ProfileImage => $"uploads/restaurant/logos/{request.OwnerId:N}",
            DocumentType.BranchPhoto => $"uploads/restaurant/branches/{request.OwnerId:N}",
            DocumentType.MealPhoto => $"uploads/restaurant/meals/{request.OwnerId:N}",
            _ => $"documents/{request.OwnerType}/{request.OwnerId}",
        };

    private static bool CanUploadForAnyOwner(ICurrentUser user) =>
        DocumentAccess.CanAccessAnyDocument(user);
}

public sealed record ReviewDocumentCommand(
    Guid DocumentId,
    bool Approve,
    string? Reason) : ICommand<DocumentDto>;

public sealed class ReviewDocumentCommandHandler : ICommandHandler<ReviewDocumentCommand, DocumentDto>
{
    private static readonly (string Key, string TitleAr, DocumentType[] DocTypes)[] RegistrationDocumentSections =
    [
        ("legal", "المستندات والتراخيص القانونية", [DocumentType.CommercialRegistration, DocumentType.CommercialLicense, DocumentType.FoodLicense]),
        ("signatory", "مستندات المفوض بالتوقيع", [DocumentType.AuthorizedSignatoryId, DocumentType.AuthorizedSignatoryAuthorization]),
        ("bank", "المستندات البنكية", [DocumentType.BankIbanProof]),
        ("branch", "مستندات الفرع والشعار", [DocumentType.ProfileImage, DocumentType.BranchLicense])
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly IRegistrationPushNotificationService _pushNotifier;

    public ReviewDocumentCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IRegistrationReviewNotifier? notifier = null,
        IRegistrationPushNotificationService? pushNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _notifier = notifier ?? NullRegistrationReviewNotifier.Instance;
        _pushNotifier = pushNotifier ?? NullRegistrationPushNotificationService.Instance;
    }

    public async Task<Result<DocumentDto>> Handle(ReviewDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await _db.Documents
            .Include(d => d.Verifications)
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken);

        if (document is null)
        {
            return Error.NotFound("documents.not_found", "Document not found.");
        }

        var trimmedReason = request.Reason?.Trim();

        // Approve does NOT require a reason; reject does.
        if (!request.Approve && string.IsNullOrWhiteSpace(trimmedReason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Review reason is required for rejection.");
        }

        if (trimmedReason is { Length: > 2000 })
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, "Review reason must not exceed 2000 characters.");
        }

        var previousStatus = document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status;
        var status = request.Approve ? VerificationStatus.Approved : VerificationStatus.Rejected;
        var verification = new DocumentVerification
        {
            DocumentId = document.Id,
            Status = status,
            ReviewedByUserId = _currentUser.UserId,
            ReviewedAtUtc = _clock.UtcNow,
            Reason = request.Approve ? (string.IsNullOrWhiteSpace(trimmedReason) ? null : trimmedReason) : trimmedReason,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.DocumentVerifications.Add(verification);

        await _audit.LogAsync(new AuditEntry(
            request.Approve ? "DocumentApproved" : "DocumentRejected",
            nameof(Document),
            document.Id,
            After: new { status, Reason = verification.Reason },
            Reason: verification.Reason,
            SourceChannel: "admin"), cancellationToken);

        // Notify registration owner when a registration-scoped document is reviewed.
        if (document.OwnerType == DocumentOwnerType.Registration)
        {
            var registration = await _db.RestaurantRegistrations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == document.OwnerId, cancellationToken);

            if (registration?.SubmittedByUserId is { } ownerUserId)
            {
                var changeType = request.Approve ? "document-approved" : "document-rejected";
                await _notifier.NotifyRegistrationUpdated(
                    ownerUserId, registration.Id, changeType, cancellationToken);

                if (!request.Approve)
                {
                    await _pushNotifier.NotifyRestaurantDocumentRejectedAsync(
                        ownerUserId,
                        document.DocumentType,
                        trimmedReason ?? string.Empty,
                        cancellationToken);
                }
                else
                {
                    var section = RegistrationDocumentSections.FirstOrDefault(s => s.DocTypes.Contains(document.DocumentType));
                    if (section.Key is not null && previousStatus != VerificationStatus.Approved)
                    {
                        var sectionDocs = await _db.Documents
                            .Include(d => d.Verifications)
                            .Where(d => d.OwnerType == DocumentOwnerType.Registration
                                        && d.OwnerId == document.OwnerId
                                        && section.DocTypes.Contains(d.DocumentType))
                            .ToListAsync(cancellationToken);

                        var allApproved = section.DocTypes.All(docType =>
                        {
                            if (docType == document.DocumentType) return true;
                            var matching = sectionDocs
                                .Where(d => d.DocumentType == docType)
                                .OrderByDescending(d => d.CreatedAtUtc)
                                .FirstOrDefault();
                            if (matching is null) return false;
                            var latest = matching.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                            return latest?.Status == VerificationStatus.Approved;
                        });

                        if (allApproved)
                        {
                            await _pushNotifier.NotifyRestaurantSectionApprovedAsync(
                                ownerUserId,
                                section.TitleAr,
                                section.Key,
                                cancellationToken);
                        }
                    }
                }
            }
        }

        return new DocumentDto(
            document.Id,
            document.OwnerType,
            document.OwnerId,
            document.DocumentType,
            document.FileName,
            document.ContentType,
            document.SizeBytes,
            status,
            document.ExpiresAtUtc,
            verification.Reason);
    }
}

public sealed record GetDocumentQuery(Guid Id) : IQuery<DocumentDto>;

public sealed class GetDocumentQueryHandler : IQueryHandler<GetDocumentQuery, DocumentDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetDocumentQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<DocumentDto>> Handle(GetDocumentQuery request, CancellationToken cancellationToken)
    {
        var document = await _db.Documents
            .Include(d => d.Verifications)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

        if (document is null)
        {
            return Error.NotFound("documents.not_found", "Document not found.");
        }

        if (!DocumentAccess.CanAccessAnyDocument(_currentUser)
            && !_currentUser.HasPermission(PermissionCodes.DocumentsView)
            && !await DocumentAccess.CallerOwnsDocumentTargetAsync(
                _db, _currentUser, document.OwnerType, document.OwnerId, cancellationToken))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed to view this document.");
        }

        var latest = document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
        return new DocumentDto(
            document.Id,
            document.OwnerType,
            document.OwnerId,
            document.DocumentType,
            document.FileName,
            document.ContentType,
            document.SizeBytes,
            latest?.Status,
            document.ExpiresAtUtc,
            latest?.Reason);
    }
}

public sealed record ListOwnerDocumentsQuery(DocumentOwnerType OwnerType, Guid OwnerId) : IQuery<IReadOnlyList<DocumentDto>>;

public sealed class ListOwnerDocumentsQueryHandler : IQueryHandler<ListOwnerDocumentsQuery, IReadOnlyList<DocumentDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListOwnerDocumentsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<DocumentDto>>> Handle(ListOwnerDocumentsQuery request, CancellationToken cancellationToken)
    {
        if (!DocumentAccess.CanAccessAnyDocument(_currentUser)
            && !_currentUser.HasPermission(PermissionCodes.DocumentsView)
            && !await DocumentAccess.CallerOwnsDocumentTargetAsync(
                _db, _currentUser, request.OwnerType, request.OwnerId, cancellationToken))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed to list documents for this owner.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .AsNoTracking()
            .Where(d => d.OwnerType == request.OwnerType && d.OwnerId == request.OwnerId)
            .ToListAsync(cancellationToken);

        return docs.Select(document =>
        {
            var latest = document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            return new DocumentDto(
                document.Id,
                document.OwnerType,
                document.OwnerId,
                document.DocumentType,
                document.FileName,
                document.ContentType,
                document.SizeBytes,
                latest?.Status,
                document.ExpiresAtUtc,
                latest?.Reason);
        }).ToList();
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Secure document download
// ──────────────────────────────────────────────────────────────────────────────

public sealed record DownloadDocumentQuery(Guid Id) : IQuery<DocumentDownloadResult>;

public sealed class DownloadDocumentQueryHandler : IQueryHandler<DownloadDocumentQuery, DocumentDownloadResult>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IAuditLogger _audit;

    public DownloadDocumentQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _audit = audit;
    }

    public async Task<Result<DocumentDownloadResult>> Handle(
        DownloadDocumentQuery request,
        CancellationToken cancellationToken)
    {
        var document = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

        if (document is null)
        {
            return Error.NotFound("documents.not_found", "Document not found.");
        }

        // Authorize: owner OR DocumentsView OR DocumentsReview OR SuperAdmin
        if (!DocumentAccess.CanAccessAnyDocument(_currentUser)
            && !_currentUser.HasPermission(PermissionCodes.DocumentsView)
            && !await DocumentAccess.CallerOwnsDocumentTargetAsync(
                _db, _currentUser, document.OwnerType, document.OwnerId, cancellationToken))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed to download this document.");
        }

        var stream = await _storage.OpenReadAsync(document.StoragePath, cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "DocumentDownloaded",
            nameof(Document),
            document.Id,
            After: new { document.DocumentType, document.OwnerType, document.OwnerId },
            SourceChannel: "documents"), cancellationToken);

        return new DocumentDownloadResult(
            stream,
            document.FileName,
            document.ContentType,
            document.SizeBytes);
    }
}

public sealed record AttachDocumentCommand(
    DocumentOwnerType OwnerType,
    Guid OwnerId,
    DocumentType DocumentType,
    string StorageKey,
    string? FileName = null,
    string? ContentType = null,
    long? SizeBytes = null,
    DateTime? ExpiresAtUtc = null) : ICommand<DocumentDto>;

public sealed class AttachDocumentCommandHandler : ICommandHandler<AttachDocumentCommand, DocumentDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;

    public AttachDocumentCommandHandler(
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<DocumentDto>> Handle(AttachDocumentCommand request, CancellationToken cancellationToken)
    {
        var doc = new Document
        {
            OwnerType = request.OwnerType,
            OwnerId = request.OwnerId,
            DocumentType = request.DocumentType,
            FileName = request.FileName ?? Path.GetFileName(request.StorageKey),
            ContentType = request.ContentType ?? "application/octet-stream",
            SizeBytes = request.SizeBytes ?? 0,
            StoragePath = request.StorageKey,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CreatedAtUtc = _clock.UtcNow,
        };

        doc.Verifications.Add(new DocumentVerification
        {
            Status = VerificationStatus.Submitted,
            CreatedAtUtc = _clock.UtcNow,
        });

        _db.Documents.Add(doc);
        await _audit.LogAsync(new AuditEntry(
            "DocumentAttached",
            nameof(Document),
            doc.Id,
            After: new { doc.DocumentType, doc.OwnerType, doc.OwnerId, doc.StoragePath },
            SourceChannel: "documents"), cancellationToken);

        return new DocumentDto(
            doc.Id,
            doc.OwnerType,
            doc.OwnerId,
            doc.DocumentType,
            doc.FileName,
            doc.ContentType,
            doc.SizeBytes,
            VerificationStatus.Submitted,
            doc.ExpiresAtUtc,
            null);
    }
}

