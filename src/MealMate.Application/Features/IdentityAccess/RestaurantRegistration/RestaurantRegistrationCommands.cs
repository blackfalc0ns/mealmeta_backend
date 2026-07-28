using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record RestaurantRegistrationDto(
    Guid Id,
    string LegalNameAr,
    string LegalNameEn,
    string TradeNameAr,
    string TradeNameEn,
    string ContactPhone,
    string ContactEmail,
    string? CommercialRegistrationNumber,
    string? OwnerName,
    string? Address,
    Guid? CountryId,
    Guid? RegionId,
    VerificationStatus Status,
    string? RejectionReason,
    string? ChangeRequestNotes,
    Guid? ResultingRestaurantId,
    Guid? SubmittedByUserId,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId);

public sealed record CreateRestaurantRegistrationCommand(
    string LegalNameAr,
    string LegalNameEn,
    string TradeNameAr,
    string TradeNameEn,
    string ContactPhone,
    string ContactEmail,
    string Password,
    string CommercialRegistrationNumber,
    string OwnerName,
    string Address,
    Guid? CountryId,
    Guid? RegionId) : ICommand<RestaurantRegistrationDto>;

public sealed class CreateRestaurantRegistrationCommandValidator : AbstractValidator<CreateRestaurantRegistrationCommand>
{
    public CreateRestaurantRegistrationCommandValidator()
    {
        RuleFor(x => x.LegalNameAr)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.LegalNameEn)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.TradeNameAr)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.TradeNameEn)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.ContactPhone)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.ContactEmail)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField)
            .EmailAddress()
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField)
            .MinimumLength(8)
            .WithMessageCode(MessageCodes.Validation.PasswordMinimumLength);
        RuleFor(x => x.CommercialRegistrationNumber)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.OwnerName)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.Address)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
    }
}

public sealed class CreateRestaurantRegistrationCommandHandler
    : ICommandHandler<CreateRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public CreateRestaurantRegistrationCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        CreateRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var duplicateCr = await _db.RestaurantRegistrations.AnyAsync(
            r => r.CommercialRegistrationNumber == request.CommercialRegistrationNumber
                 && r.Status != VerificationStatus.Rejected,
            cancellationToken);
        if (duplicateCr)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateCommercialRegistration, "Commercial registration already used.");
        }

        var createUser = await _users.CreateAsync(new CreateUserRequest(
            request.ContactEmail.Trim().ToLowerInvariant(),
            request.ContactPhone.Trim(),
            request.Password,
            request.OwnerName,
            UserType.Restaurant,
            AccountStatus.Registered), cancellationToken);

        if (createUser.IsFailure)
        {
            return createUser.Error;
        }

        await _users.AddToRoleAsync(createUser.Value.Id, AppRoles.RestaurantOwner, cancellationToken);

        var registration = new Domain.IdentityAccess.RestaurantRegistration
        {
            SubmittedByUserId = createUser.Value.Id,
            LegalNameAr = request.LegalNameAr,
            LegalNameEn = request.LegalNameEn,
            TradeNameAr = request.TradeNameAr,
            TradeNameEn = request.TradeNameEn,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(),
            CommercialRegistrationNumber = request.CommercialRegistrationNumber,
            OwnerName = request.OwnerName,
            Address = request.Address,
            CountryId = request.CountryId,
            RegionId = request.RegionId,
            Status = VerificationStatus.Draft,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.RestaurantRegistrations.Add(registration);
        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationCreated",
            "RestaurantRegistration",
            registration.Id,
            After: registration,
            SourceChannel: "restaurant"), cancellationToken);

        return RestaurantRegistrationMapping.Map(registration);
    }
}

public sealed record UpdateRestaurantRegistrationCommand(
    Guid Id,
    string? LegalNameAr,
    string? LegalNameEn,
    string? TradeNameAr,
    string? TradeNameEn,
    string? Address,
    string? OwnerName) : ICommand<RestaurantRegistrationDto>;

public sealed class UpdateRestaurantRegistrationCommandHandler
    : ICommandHandler<UpdateRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public UpdateRestaurantRegistrationCommandHandler(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        UpdateRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (registration.Status is not (VerificationStatus.Draft or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration cannot be edited in current status.");
        }

        if (registration.SubmittedByUserId != _currentUser.UserId
            && !_currentUser.IsInRole(AppRoles.SuperAdmin)
            && !_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        var before = RestaurantRegistrationMapping.Map(registration);
        registration.LegalNameAr = request.LegalNameAr ?? registration.LegalNameAr;
        registration.LegalNameEn = request.LegalNameEn ?? registration.LegalNameEn;
        registration.TradeNameAr = request.TradeNameAr ?? registration.TradeNameAr;
        registration.TradeNameEn = request.TradeNameEn ?? registration.TradeNameEn;
        registration.Address = request.Address ?? registration.Address;
        registration.OwnerName = request.OwnerName ?? registration.OwnerName;

        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationUpdated",
            "RestaurantRegistration",
            registration.Id,
            Before: before,
            After: RestaurantRegistrationMapping.Map(registration),
            SourceChannel: "restaurant"), cancellationToken);

        return RestaurantRegistrationMapping.Map(registration);
    }
}

public sealed record SubmitRestaurantRegistrationCommand(Guid Id) : ICommand<RestaurantRegistrationDto>;

public sealed class SubmitRestaurantRegistrationCommandHandler
    : ICommandHandler<SubmitRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private static readonly DocumentType[] RequiredDocs =
    [
        DocumentType.CommercialLicense,
        DocumentType.Contract,
        DocumentType.NationalId,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public SubmitRestaurantRegistrationCommandHandler(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        SubmitRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (registration.SubmittedByUserId != _currentUser.UserId
            && !_currentUser.IsInRole(AppRoles.SuperAdmin)
            && !_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        if (registration.Status is not (VerificationStatus.Draft or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Invalid status for submit.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        foreach (var required in RequiredDocs)
        {
            var doc = docs.FirstOrDefault(d => d.DocumentType == required);
            if (doc is null)
            {
                return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Missing document: {required}");
            }

            var latest = doc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            if (latest?.Status == VerificationStatus.Rejected)
            {
                return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Rejected document must be re-uploaded: {required}");
            }
        }

        registration.Status = VerificationStatus.Submitted;
        registration.RejectionReason = null;
        registration.ChangeRequestNotes = null;

        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationSubmitted",
            "RestaurantRegistration",
            registration.Id,
            SourceChannel: "restaurant"), cancellationToken);

        return RestaurantRegistrationMapping.Map(registration);
    }
}

public sealed record AdminRestaurantDecisionCommand(
    Guid Id,
    string Action,
    string Reason) : ICommand<RestaurantRegistrationDto>;

public sealed class AdminRestaurantDecisionCommandHandler
    : ICommandHandler<AdminRestaurantDecisionCommand, RestaurantRegistrationDto>
{
    private const int MaxReasonLength = 2000;

    private static readonly DocumentType[] RequiredDocs =
    [
        DocumentType.CommercialLicense,
        DocumentType.Contract,
        DocumentType.NationalId,
    ];

    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public AdminRestaurantDecisionCommandHandler(
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

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        AdminRestaurantDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required.");
        }

        if (reason.Length > MaxReasonLength)
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, $"Reason must not exceed {MaxReasonLength} characters.");
        }

        var action = request.Action.ToLowerInvariant();
        switch (action)
        {
            case "approve":
                {
                    if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
                    {
                        return Error.Conflict(MessageCodes.Auth.InvalidState, "Cannot approve in current status.");
                    }

                    var docs = await _db.Documents
                        .Include(d => d.Verifications)
                        .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
                        .ToListAsync(cancellationToken);

                    // All required document types must be present and approved
                    foreach (var required in RequiredDocs)
                    {
                        var doc = docs.FirstOrDefault(d => d.DocumentType == required);
                        if (doc is null)
                        {
                            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Missing required document: {required}");
                        }

                        var latest = doc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                        if (latest is null || latest.Status != VerificationStatus.Approved)
                        {
                            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Required document not approved: {required}");
                        }
                    }

                    var profile = new RestaurantProfile
                    {
                        UserId = registration.SubmittedByUserId ?? Guid.Empty,
                        LegalNameAr = registration.LegalNameAr,
                        LegalNameEn = registration.LegalNameEn,
                        TradeNameAr = registration.TradeNameAr,
                        TradeNameEn = registration.TradeNameEn,
                        CommercialLicenseNumber = registration.CommercialRegistrationNumber,
                        CountryId = registration.CountryId,
                        PrimaryRegionId = registration.RegionId,
                        ContactPhone = registration.ContactPhone,
                        ContactEmail = registration.ContactEmail,
                        VerificationStatus = VerificationStatus.Approved,
                        IsOperationallyActive = false,
                        CreatedAtUtc = _clock.UtcNow,
                    };
                    _db.RestaurantProfiles.Add(profile);

                    registration.Status = VerificationStatus.Approved;
                    registration.ResultingRestaurantId = profile.Id;
                    registration.ReviewedByUserId = _currentUser.UserId;
                    registration.ReviewedAtUtc = _clock.UtcNow;
                    registration.RejectionReason = null;

                    if (registration.SubmittedByUserId is { } userId)
                    {
                        await _users.SetRestaurantIdAsync(userId, profile.Id, cancellationToken);
                        // Approval does NOT activate the owner account; activation is a separate admin action.
                    }

                    await _audit.LogAsync(new AuditEntry(
                        "RestaurantRegistrationApproved",
                        "RestaurantRegistration",
                        registration.Id,
                        After: RestaurantRegistrationMapping.Map(registration),
                        Reason: reason,
                        SourceChannel: "admin"), cancellationToken);
                    break;
                }
            case "reject":
                if (registration.Status is VerificationStatus.Rejected)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration is already rejected.");
                }

                registration.Status = VerificationStatus.Rejected;
                registration.RejectionReason = reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry(
                    "RestaurantRegistrationRejected",
                    "RestaurantRegistration",
                    registration.Id,
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;
            case "request-changes":
                if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Cannot request changes in current status.");
                }

                registration.Status = VerificationStatus.NeedsChanges;
                registration.ChangeRequestNotes = reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry(
                    "RestaurantRegistrationChangesRequested",
                    "RestaurantRegistration",
                    registration.Id,
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;
            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        return RestaurantRegistrationMapping.Map(registration);
    }
}

public sealed record ListRestaurantRegistrationsQuery(int Page = 1, int PageSize = 20, VerificationStatus? Status = null)
    : IQuery<PagedResult<RestaurantRegistrationDto>>;

public sealed class ListRestaurantRegistrationsQueryHandler
    : IQueryHandler<ListRestaurantRegistrationsQuery, PagedResult<RestaurantRegistrationDto>>
{
    private readonly IAppDbContext _db;

    public ListRestaurantRegistrationsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<RestaurantRegistrationDto>>> Handle(
        ListRestaurantRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var query = _db.RestaurantRegistrations.AsNoTracking().AsQueryable();
        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RestaurantRegistrationDto>(items.Select(RestaurantRegistrationMapping.Map).ToList(), total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

public sealed record GetRestaurantRegistrationQuery(Guid Id) : IQuery<RestaurantRegistrationDto>;

public sealed class GetRestaurantRegistrationQueryHandler : IQueryHandler<GetRestaurantRegistrationQuery, RestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantRegistrationQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(GetRestaurantRegistrationQuery request, CancellationToken cancellationToken)
    {
        var item = await _db.RestaurantRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (!RegistrationAccess.CanView(
                _currentUser,
                item.SubmittedByUserId,
                PermissionCodes.RestaurantRegView,
                PermissionCodes.RestaurantRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        return RestaurantRegistrationMapping.Map(item);
    }
}

internal static class RestaurantRegistrationMapping
{
    public static RestaurantRegistrationDto Map(Domain.IdentityAccess.RestaurantRegistration r) => new(
        r.Id, r.LegalNameAr, r.LegalNameEn, r.TradeNameAr, r.TradeNameEn,
        r.ContactPhone, r.ContactEmail, r.CommercialRegistrationNumber, r.OwnerName, r.Address,
        r.CountryId, r.RegionId, r.Status, r.RejectionReason, r.ChangeRequestNotes, r.ResultingRestaurantId,
        r.SubmittedByUserId, r.CreatedAtUtc, r.ModifiedAtUtc, r.ReviewedAtUtc, r.ReviewedByUserId);
}
