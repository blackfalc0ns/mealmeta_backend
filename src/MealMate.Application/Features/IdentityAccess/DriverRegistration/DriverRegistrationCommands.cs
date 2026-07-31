using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed record DriverRegistrationDto(
    Guid Id,
    Guid? RestaurantId,
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string? NationalId,
    string? LicenseNumber,
    string? VehiclePlate,
    string? VehicleType,
    Guid? CountryId,
    Guid? RegionId,
    string? ProfileImageStorageKey,
    VerificationStatus Status,
    string? RejectionReason,
    string? ChangeRequestNotes,
    Guid? ResultingDriverProfileId);

public sealed record CreateDriverRegistrationCommand(
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string Password,
    Guid? RestaurantId = null,
    string? NationalId = null,
    string? LicenseNumber = null,
    string? VehiclePlate = null,
    string? VehicleType = null,
    Guid? CountryId = null,
    Guid? RegionId = null,
    string? ProfileImageStorageKey = null,
    string? NationalIdFrontStorageKey = null,
    string? NationalIdBackStorageKey = null,
    string? DrivingLicenseStorageKey = null,
    string? EmploymentContractStorageKey = null) : ICommand<DriverRegistrationDto>;

public sealed class CreateDriverRegistrationCommandValidator : AbstractValidator<CreateDriverRegistrationCommand>
{
    public CreateDriverRegistrationCommandValidator()
    {
        RuleFor(x => x.FullNameAr).NotEmpty();
        RuleFor(x => x.FullNameEn).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class CreateDriverRegistrationCommandHandler
    : ICommandHandler<CreateDriverRegistrationCommand, DriverRegistrationDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public CreateDriverRegistrationCommandHandler(
        IUserAccountService users, IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        CreateDriverRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var createUser = await _users.CreateAsync(new CreateUserRequest(
            request.Email?.Trim().ToLowerInvariant(),
            request.Phone.Trim(),
            request.Password,
            request.FullNameEn,
            UserType.Driver,
            AccountStatus.Registered,
            request.RestaurantId), cancellationToken);

        if (createUser.IsFailure)
        {
            return createUser.Error;
        }

        await _users.AddToRoleAsync(createUser.Value.Id, AppRoles.Driver, cancellationToken);

        var registration = new Domain.IdentityAccess.DriverRegistration
        {
            SubmittedByUserId = createUser.Value.Id,
            RestaurantId = request.RestaurantId,
            FullNameAr = request.FullNameAr,
            FullNameEn = request.FullNameEn,
            Phone = request.Phone,
            Email = request.Email?.Trim().ToLowerInvariant(),
            NationalId = request.NationalId,
            LicenseNumber = request.LicenseNumber,
            VehiclePlate = request.VehiclePlate,
            VehicleType = request.VehicleType,
            CountryId = request.CountryId,
            RegionId = request.RegionId,
            ProfileImageStorageKey = request.ProfileImageStorageKey,
            Status = VerificationStatus.Draft,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.DriverRegistrations.Add(registration);

        var docsToAttach = new (DocumentType Type, string? Key)[]
        {
            (DocumentType.ProfileImage, request.ProfileImageStorageKey),
            (DocumentType.NationalIdFront, request.NationalIdFrontStorageKey),
            (DocumentType.NationalIdBack, request.NationalIdBackStorageKey),
            (DocumentType.DrivingLicense, request.DrivingLicenseStorageKey),
            (DocumentType.EmploymentContract, request.EmploymentContractStorageKey),
        };

        foreach (var (docType, key) in docsToAttach)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                var doc = new Document
                {
                    OwnerType = DocumentOwnerType.Registration,
                    OwnerId = registration.Id,
                    DocumentType = docType,
                    FileName = Path.GetFileName(key),
                    ContentType = "image/jpeg",
                    StoragePath = key,
                    CreatedAtUtc = _clock.UtcNow,
                };
                doc.Verifications.Add(new DocumentVerification
                {
                    Status = VerificationStatus.Submitted,
                    CreatedAtUtc = _clock.UtcNow,
                });
                _db.Documents.Add(doc);
            }
        }

        await _audit.LogAsync(new AuditEntry("DriverRegistrationCreated", "DriverRegistration", registration.Id, SourceChannel: "driver"), cancellationToken);
        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record UploadDriverProfileImageCommand(Guid RegistrationId, string FileName, string ContentType, Stream Content)
    : ICommand<DriverRegistrationDto>;

public sealed class UploadDriverProfileImageCommandHandler
    : ICommandHandler<UploadDriverProfileImageCommand, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public UploadDriverProfileImageCommandHandler(
        IAppDbContext db,
        IFileStorage storage,
        ICurrentUser currentUser,
        IAuditLogger audit)
    {
        _db = db;
        _storage = storage;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        UploadDriverProfileImageCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not { } userId
            || (registration.SubmittedByUserId != userId
                && !_currentUser.IsInRole(AppRoles.SuperAdmin)
                && !_currentUser.HasPermission(PermissionCodes.DriverRegApprove)))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(request.ContentType))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "Profile image must be JPG/PNG/WebP.");
        }

        var stored = await _storage.SaveAsync(
            request.Content,
            request.FileName,
            request.ContentType,
            $"drivers/{registration.Id}/profile",
            cancellationToken);

        registration.ProfileImageStorageKey = stored.StorageKey;
        await _audit.LogAsync(new AuditEntry("DriverProfileImageUploaded", "DriverRegistration", registration.Id, SourceChannel: "driver"), cancellationToken);
        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record SubmitDriverRegistrationCommand(Guid Id) : ICommand<DriverRegistrationDto>;

public sealed class SubmitDriverRegistrationCommandHandler
    : ICommandHandler<SubmitDriverRegistrationCommand, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public SubmitDriverRegistrationCommandHandler(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        SubmitDriverRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not { } userId
            || (registration.SubmittedByUserId != userId
                && !_currentUser.IsInRole(AppRoles.SuperAdmin)
                && !_currentUser.HasPermission(PermissionCodes.DriverRegApprove)))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        if (string.IsNullOrWhiteSpace(registration.ProfileImageStorageKey))
        {
            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, "Profile image is required.");
        }

        var docs = await _db.Documents
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        if (!docs.Any(d => d.DocumentType == DocumentType.NationalId)
            || !docs.Any(d => d.DocumentType == DocumentType.DrivingLicense))
        {
            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, "National ID and driving license are required.");
        }

        registration.Status = VerificationStatus.Submitted;
        await _audit.LogAsync(new AuditEntry("DriverSubmittedForReview", "DriverRegistration", registration.Id, SourceChannel: "driver"), cancellationToken);
        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record AdminDriverDecisionCommand(Guid Id, string Action, string Reason) : ICommand<DriverRegistrationDto>;

public sealed class AdminDriverDecisionCommandHandler
    : ICommandHandler<AdminDriverDecisionCommand, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public AdminDriverDecisionCommandHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        AdminDriverDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required.");
        }

        switch (request.Action.ToLowerInvariant())
        {
            case "approve":
                var profile = new DriverProfile
                {
                    UserId = registration.SubmittedByUserId ?? Guid.Empty,
                    RestaurantId = registration.RestaurantId,
                    FullNameAr = registration.FullNameAr,
                    FullNameEn = registration.FullNameEn,
                    NationalId = registration.NationalId,
                    LicenseNumber = registration.LicenseNumber,
                    VehiclePlate = registration.VehiclePlate,
                    VehicleType = registration.VehicleType,
                    CountryId = registration.CountryId,
                    RegionId = registration.RegionId,
                    ProfileImageStorageKey = registration.ProfileImageStorageKey,
                    ProfileImageStatus = VerificationStatus.Approved,
                    VerificationStatus = VerificationStatus.Approved,
                    IsAvailable = false,
                    ActivatedAtUtc = _clock.UtcNow,
                    CreatedAtUtc = _clock.UtcNow,
                };
                _db.DriverProfiles.Add(profile);
                registration.Status = VerificationStatus.Approved;
                registration.ResultingDriverProfileId = profile.Id;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                if (registration.SubmittedByUserId is { } userId)
                {
                    await _users.UpdateAccountStatusAsync(userId, AccountStatus.Active, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry("DriverApproved", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "admin"), cancellationToken);
                break;
            case "reject":
                registration.Status = VerificationStatus.Rejected;
                registration.RejectionReason = request.Reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry("DriverRejected", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "admin"), cancellationToken);
                break;
            case "request-changes":
                registration.Status = VerificationStatus.NeedsChanges;
                registration.ChangeRequestNotes = request.Reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry("DriverChangesRequested", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "admin"), cancellationToken);
                break;
            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record ListDriverRegistrationsQuery(int Page = 1, int PageSize = 20, VerificationStatus? Status = null)
    : IQuery<PagedResult<DriverRegistrationDto>>;

public sealed class ListDriverRegistrationsQueryHandler
    : IQueryHandler<ListDriverRegistrationsQuery, PagedResult<DriverRegistrationDto>>
{
    private readonly IAppDbContext _db;

    public ListDriverRegistrationsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<DriverRegistrationDto>>> Handle(
        ListDriverRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var query = _db.DriverRegistrations.AsNoTracking().AsQueryable();
        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip).Take(page.NormalizedPageSize).ToListAsync(cancellationToken);
        return new PagedResult<DriverRegistrationDto>(items.Select(DriverRegistrationMapping.Map).ToList(), total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

public sealed record GetDriverRegistrationQuery(Guid Id) : IQuery<DriverRegistrationDto>;

public sealed class GetDriverRegistrationQueryHandler : IQueryHandler<GetDriverRegistrationQuery, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetDriverRegistrationQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(GetDriverRegistrationQuery request, CancellationToken cancellationToken)
    {
        var item = await _db.DriverRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (!RegistrationAccess.CanView(
                _currentUser,
                item.SubmittedByUserId,
                PermissionCodes.DriverRegView,
                PermissionCodes.DriverRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        return DriverRegistrationMapping.Map(item);
    }
}

internal static class DriverRegistrationMapping
{
    public static DriverRegistrationDto Map(Domain.IdentityAccess.DriverRegistration r) => new(
        r.Id, r.RestaurantId, r.FullNameAr, r.FullNameEn, r.Phone, r.Email,
        r.NationalId, r.LicenseNumber, r.VehiclePlate, r.VehicleType,
        r.CountryId, r.RegionId, r.ProfileImageStorageKey, r.Status,
        r.RejectionReason, r.ChangeRequestNotes, r.ResultingDriverProfileId);
}

public sealed record UpdateDriverRegistrationCommand(
    Guid Id,
    string? FullNameAr,
    string? FullNameEn,
    string? Phone,
    string? Email,
    string? NationalId,
    string? LicenseNumber,
    string? VehiclePlate,
    string? VehicleType,
    Guid? CountryId,
    Guid? RegionId) : ICommand<DriverRegistrationDto>;

public sealed class UpdateDriverRegistrationCommandValidator : AbstractValidator<UpdateDriverRegistrationCommand>
{
    public UpdateDriverRegistrationCommandValidator()
    {
        RuleFor(x => x.FullNameAr).NotEmpty().MaximumLength(200).When(x => x.FullNameAr is not null);
        RuleFor(x => x.FullNameEn).NotEmpty().MaximumLength(200).When(x => x.FullNameEn is not null);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30).When(x => x.Phone is not null);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.NationalId).MaximumLength(50).When(x => x.NationalId is not null);
        RuleFor(x => x.LicenseNumber).MaximumLength(50).When(x => x.LicenseNumber is not null);
        RuleFor(x => x.VehiclePlate).MaximumLength(30).When(x => x.VehiclePlate is not null);
        RuleFor(x => x.VehicleType).MaximumLength(50).When(x => x.VehicleType is not null);
    }
}

public sealed class UpdateDriverRegistrationCommandHandler
    : ICommandHandler<UpdateDriverRegistrationCommand, DriverRegistrationDto>
{
    private static readonly VerificationStatus[] AllowedStatuses =
    [
        VerificationStatus.Draft,
        VerificationStatus.NeedsChanges,
        VerificationStatus.ResubmissionRequired,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public UpdateDriverRegistrationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        UpdateDriverRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not { } userId
            || (registration.SubmittedByUserId != userId
                && !_currentUser.IsInRole(AppRoles.SuperAdmin)
                && !_currentUser.HasPermission(PermissionCodes.DriverRegApprove)))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        if (!AllowedStatuses.Contains(registration.Status))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration cannot be updated in current status.");
        }

        var before = new
        {
            registration.FullNameAr,
            registration.FullNameEn,
            registration.Phone,
            registration.Email,
            registration.NationalId,
            registration.LicenseNumber,
            registration.VehiclePlate,
            registration.VehicleType,
            registration.CountryId,
            registration.RegionId,
        };

        if (request.FullNameAr is not null)
        {
            registration.FullNameAr = request.FullNameAr;
        }

        if (request.FullNameEn is not null)
        {
            registration.FullNameEn = request.FullNameEn;
        }

        if (request.Phone is not null)
        {
            registration.Phone = request.Phone;
        }

        if (request.Email is not null)
        {
            registration.Email = request.Email.Trim().ToLowerInvariant();
        }

        if (request.NationalId is not null)
        {
            registration.NationalId = request.NationalId;
        }

        if (request.LicenseNumber is not null)
        {
            registration.LicenseNumber = request.LicenseNumber;
        }

        if (request.VehiclePlate is not null)
        {
            registration.VehiclePlate = request.VehiclePlate;
        }

        if (request.VehicleType is not null)
        {
            registration.VehicleType = request.VehicleType;
        }

        if (request.CountryId.HasValue)
        {
            registration.CountryId = request.CountryId;
        }

        if (request.RegionId.HasValue)
        {
            registration.RegionId = request.RegionId;
        }

        var after = new
        {
            registration.FullNameAr,
            registration.FullNameEn,
            registration.Phone,
            registration.Email,
            registration.NationalId,
            registration.LicenseNumber,
            registration.VehiclePlate,
            registration.VehicleType,
            registration.CountryId,
            registration.RegionId,
        };

        await _audit.LogAsync(new AuditEntry(
            "DriverRegistrationUpdated",
            "DriverRegistration",
            registration.Id,
            Before: before,
            After: after,
            SourceChannel: "driver"), cancellationToken);

        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record UploadDriverRegistrationDocumentCommand(
    Guid RegistrationId,
    DocumentType DocumentType,
    string FileName,
    string ContentType,
    Stream Content) : ICommand<DocumentDto>;

public sealed class UploadDriverRegistrationDocumentCommandHandler
    : ICommandHandler<UploadDriverRegistrationDocumentCommand, DocumentDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ISender _sender;

    public UploadDriverRegistrationDocumentCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ISender sender)
    {
        _db = db;
        _currentUser = currentUser;
        _sender = sender;
    }

    public async Task<Result<DocumentDto>> Handle(
        UploadDriverRegistrationDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not { } userId
            || (registration.SubmittedByUserId != userId
                && !_currentUser.IsInRole(AppRoles.SuperAdmin)
                && !_currentUser.HasPermission(PermissionCodes.DriverRegApprove)))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        return await _sender.Send(new UploadDocumentCommand(
            DocumentOwnerType.Registration,
            request.RegistrationId,
            request.DocumentType,
            request.FileName,
            request.ContentType,
            request.Content), cancellationToken);
    }
}
