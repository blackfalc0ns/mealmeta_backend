using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
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

    public UploadDocumentCommandHandler(
        IAppDbContext db,
        IFileStorage storage,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DocumentDto>> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
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

        if (!request.Approve && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Rejection reason is required.");
        }

        var status = request.Approve ? VerificationStatus.Approved : VerificationStatus.Rejected;
        var verification = new DocumentVerification
        {
            DocumentId = document.Id,
            Status = status,
            ReviewedByUserId = _currentUser.UserId,
            ReviewedAtUtc = _clock.UtcNow,
            Reason = request.Reason,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.DocumentVerifications.Add(verification);

        await _audit.LogAsync(new AuditEntry(
            request.Approve ? "DocumentApproved" : "DocumentRejected",
            nameof(Document),
            document.Id,
            After: new { status, request.Reason },
            Reason: request.Reason,
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
            request.Reason);
    }
}

public sealed record GetDocumentQuery(Guid Id) : IQuery<DocumentDto>;

public sealed class GetDocumentQueryHandler : IQueryHandler<GetDocumentQuery, DocumentDto>
{
    private readonly IAppDbContext _db;

    public GetDocumentQueryHandler(IAppDbContext db) => _db = db;

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

    public ListOwnerDocumentsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<DocumentDto>>> Handle(ListOwnerDocumentsQuery request, CancellationToken cancellationToken)
    {
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
