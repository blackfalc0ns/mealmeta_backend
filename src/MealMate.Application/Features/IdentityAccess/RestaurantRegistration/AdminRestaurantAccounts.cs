using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

// ──────────────────────────────────────────────────────────────────────────────
// DTOs
// ──────────────────────────────────────────────────────────────────────────────

public sealed record DocumentReviewSummaryDto(
    Guid DocumentId,
    DocumentType DocumentType,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc,
    VerificationStatus? LatestStatus,
    string? RejectionReason,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId);

public sealed record AdminRestaurantRegistrationDetailDto(
    RestaurantRegistrationDto Registration,
    IReadOnlyList<DocumentReviewSummaryDto> Documents);

public sealed record AdminRestaurantProfileDto(
    Guid Id,
    Guid UserId,
    string LegalNameAr,
    string LegalNameEn,
    string TradeNameAr,
    string TradeNameEn,
    string? CommercialLicenseNumber,
    Guid? CountryId,
    Guid? PrimaryRegionId,
    VerificationStatus VerificationStatus,
    bool IsOperationallyActive,
    DateTime? ActivatedAtUtc,
    string? ContactPhone,
    string? ContactEmail,
    DateTime CreatedAtUtc);

// ──────────────────────────────────────────────────────────────────────────────
// Admin registration detail (aggregates documents with review metadata)
// ──────────────────────────────────────────────────────────────────────────────

public sealed record GetAdminRestaurantRegistrationDetailQuery(Guid Id)
    : IQuery<AdminRestaurantRegistrationDetailDto>;

public sealed class GetAdminRestaurantRegistrationDetailQueryHandler
    : IQueryHandler<GetAdminRestaurantRegistrationDetailQuery, AdminRestaurantRegistrationDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetAdminRestaurantRegistrationDetailQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<AdminRestaurantRegistrationDetailDto>> Handle(
        GetAdminRestaurantRegistrationDetailQuery request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        // Owner or admin with view/approve permission
        if (!RegistrationAccess.CanView(
                _currentUser,
                registration.SubmittedByUserId,
                PermissionCodes.RestaurantRegView,
                PermissionCodes.RestaurantRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .AsNoTracking()
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        var summaries = docs.Select(d =>
        {
            var latest = d.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            return new DocumentReviewSummaryDto(
                d.Id,
                d.DocumentType,
                d.FileName,
                d.ContentType,
                d.SizeBytes,
                d.CreatedAtUtc,
                latest?.Status,
                latest?.Reason,
                latest?.ReviewedAtUtc,
                latest?.ReviewedByUserId);
        }).ToList();

        var dto = RestaurantRegistrationMapping.Map(registration);
        return new AdminRestaurantRegistrationDetailDto(dto, summaries);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Admin restaurant profile list
// ──────────────────────────────────────────────────────────────────────────────

public sealed record ListAdminRestaurantsQuery(
    int Page = 1,
    int PageSize = 20,
    bool? IsOperationallyActive = null)
    : IQuery<PagedResult<AdminRestaurantProfileDto>>;

public sealed class ListAdminRestaurantsQueryHandler
    : IQueryHandler<ListAdminRestaurantsQuery, PagedResult<AdminRestaurantProfileDto>>
{
    private readonly IAppDbContext _db;

    public ListAdminRestaurantsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<AdminRestaurantProfileDto>>> Handle(
        ListAdminRestaurantsQuery request,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var query = _db.RestaurantProfiles.AsNoTracking().AsQueryable();

        if (request.IsOperationallyActive is { } active)
        {
            query = query.Where(x => x.IsOperationallyActive == active);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(AdminRestaurantProfileMapping.Map).ToList();
        return new PagedResult<AdminRestaurantProfileDto>(dtos, total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Admin restaurant profile detail
// ──────────────────────────────────────────────────────────────────────────────

public sealed record GetAdminRestaurantProfileQuery(Guid Id) : IQuery<AdminRestaurantProfileDto>;

public sealed class GetAdminRestaurantProfileQueryHandler
    : IQueryHandler<GetAdminRestaurantProfileQuery, AdminRestaurantProfileDto>
{
    private readonly IAppDbContext _db;

    public GetAdminRestaurantProfileQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<AdminRestaurantProfileDto>> Handle(
        GetAdminRestaurantProfileQuery request,
        CancellationToken cancellationToken)
    {
        var profile = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant profile not found.");
        }

        return AdminRestaurantProfileMapping.Map(profile);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Activate / Suspend / Reactivate restaurant profile
// ──────────────────────────────────────────────────────────────────────────────

public sealed record AdminRestaurantLifecycleCommand(
    Guid RestaurantId,
    string Action,
    string Reason) : ICommand<AdminRestaurantProfileDto>;

public sealed class AdminRestaurantLifecycleCommandHandler
    : ICommandHandler<AdminRestaurantLifecycleCommand, AdminRestaurantProfileDto>
{
    private const int MaxReasonLength = 2000;

    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public AdminRestaurantLifecycleCommandHandler(
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

    public async Task<Result<AdminRestaurantProfileDto>> Handle(
        AdminRestaurantLifecycleCommand request,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required.");
        }

        if (reason.Length > MaxReasonLength)
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, $"Reason must not exceed {MaxReasonLength} characters.");
        }

        var profile = await _db.RestaurantProfiles
            .FirstOrDefaultAsync(p => p.Id == request.RestaurantId, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant profile not found.");
        }

        var action = request.Action.ToLowerInvariant();
        var before = AdminRestaurantProfileMapping.Map(profile);

        switch (action)
        {
            case "activate":
                if (profile.VerificationStatus != VerificationStatus.Approved)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant must be approved before activation.");
                }

                if (profile.ActivatedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant has already been activated; use reactivate instead.");
                }

                if (profile.IsOperationallyActive)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is already operationally active.");
                }

                profile.IsOperationallyActive = true;
                profile.ActivatedAtUtc = _clock.UtcNow;
                profile.ModifiedAtUtc = _clock.UtcNow;
                profile.ModifiedByUserId = _currentUser.UserId;

                // Activate the owner account on initial activation
                if (profile.UserId != Guid.Empty)
                {
                    await _users.UpdateAccountStatusAsync(profile.UserId, AccountStatus.Active, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantActivated",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.Map(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "suspend":
                if (!profile.IsOperationallyActive)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is already suspended.");
                }

                profile.IsOperationallyActive = false;
                profile.ModifiedAtUtc = _clock.UtcNow;
                profile.ModifiedByUserId = _currentUser.UserId;

                // Suspend the owner account for consistency
                if (profile.UserId != Guid.Empty)
                {
                    await _users.UpdateAccountStatusAsync(profile.UserId, AccountStatus.Suspended, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantSuspended",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.Map(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "reactivate":
                if (profile.VerificationStatus != VerificationStatus.Approved)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant must be approved before reactivation.");
                }

                if (profile.ActivatedAtUtc is null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant has never been activated; use activate instead.");
                }

                if (profile.IsOperationallyActive)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is already operationally active.");
                }

                profile.IsOperationallyActive = true;
                profile.ModifiedAtUtc = _clock.UtcNow;
                profile.ModifiedByUserId = _currentUser.UserId;

                // Reactivate the owner account for consistency
                if (profile.UserId != Guid.Empty)
                {
                    await _users.UpdateAccountStatusAsync(profile.UserId, AccountStatus.Active, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantReactivated",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.Map(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        return AdminRestaurantProfileMapping.Map(profile);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Mapping
// ──────────────────────────────────────────────────────────────────────────────

file static class AdminRestaurantProfileMapping
{
    public static AdminRestaurantProfileDto Map(RestaurantProfile p) => new(
        p.Id,
        p.UserId,
        p.LegalNameAr,
        p.LegalNameEn,
        p.TradeNameAr,
        p.TradeNameEn,
        p.CommercialLicenseNumber,
        p.CountryId,
        p.PrimaryRegionId,
        p.VerificationStatus,
        p.IsOperationallyActive,
        p.ActivatedAtUtc,
        p.ContactPhone,
        p.ContactEmail,
        p.CreatedAtUtc);
}
