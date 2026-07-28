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

    public UploadDocumentCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType)
            .Must(AllowedContentTypes.Contains)
            .WithMessage("Unsupported file type.");
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

        var stored = await _storage.SaveAsync(
            request.Content,
            request.FileName,
            request.ContentType,
            $"documents/{request.OwnerType}/{request.OwnerId}",
            cancellationToken);

        if (stored.SizeBytes > MaxBytes)
        {
            await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return Error.Validation(MessageCodes.Validation.MaxLength, "File exceeds 10MB limit.");
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

    private static bool CanUploadForAnyOwner(ICurrentUser user) =>
        DocumentAccess.CanAccessAnyDocument(user);
}

public sealed record ReviewDocumentCommand(
    Guid DocumentId,
    bool Approve,
    string? Reason) : ICommand<DocumentDto>;

public sealed class ReviewDocumentCommandHandler : ICommandHandler<ReviewDocumentCommand, DocumentDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public ReviewDocumentCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
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

        if (string.IsNullOrWhiteSpace(trimmedReason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Review reason is required.");
        }

        if (trimmedReason.Length > 2000)
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, "Review reason must not exceed 2000 characters.");
        }

        var status = request.Approve ? VerificationStatus.Approved : VerificationStatus.Rejected;
        var verification = new DocumentVerification
        {
            DocumentId = document.Id,
            Status = status,
            ReviewedByUserId = _currentUser.UserId,
            ReviewedAtUtc = _clock.UtcNow,
            Reason = trimmedReason,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.DocumentVerifications.Add(verification);

        await _audit.LogAsync(new AuditEntry(
            request.Approve ? "DocumentApproved" : "DocumentRejected",
            nameof(Document),
            document.Id,
            After: new { status, Reason = trimmedReason },
            Reason: trimmedReason,
            SourceChannel: "admin"), cancellationToken);

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
            trimmedReason);
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
