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
    DateTime? ExpiresAtUtc,
    VerificationStatus? LatestStatus,
    string? RejectionReason,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId);

public sealed record AdminRestaurantRegistrationDetailDto(
    RestaurantRegistrationDto Registration,
    IReadOnlyList<DocumentReviewSummaryDto> Documents,
    IReadOnlyList<FieldReviewSummaryDto> FieldReviews);

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
    DateTime CreatedAtUtc,
    Guid? ProfileId,
    Guid? RegistrationId,
    AccountStatus AccountStatus,
    string OwnerName);

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
                d.ExpiresAtUtc,
                latest?.Status,
                latest?.Reason,
                latest?.ReviewedAtUtc,
                latest?.ReviewedByUserId);
        }).ToList();

        // Materialize field reviews; for legacy rows with no reviews, create Submitted reviews safely
        var fieldReviews = await _db.RestaurantRegistrationFieldReviews
            .AsNoTracking()
            .Where(f => f.RegistrationId == registration.Id)
            .ToListAsync(cancellationToken);

        if (fieldReviews.Count == 0 && registration.Status is not VerificationStatus.Draft)
        {
            // Legacy submitted registrations with no field reviews yet — treat as Submitted
            fieldReviews = RegistrationFieldKeys.All
                .Select(fk => new RestaurantRegistrationFieldReview
                {
                    FieldKey = fk,
                    Status = VerificationStatus.Submitted,
                })
                .ToList();
        }

        var fieldSummaries = fieldReviews.Select(RestaurantRegistrationMapping.MapFieldReview).ToList();
        var dto = RestaurantRegistrationMapping.Map(registration);
        return new AdminRestaurantRegistrationDetailDto(dto, summaries, fieldSummaries);
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
    private readonly IUserAccountService _users;

    public ListAdminRestaurantsQueryHandler(IAppDbContext db, IUserAccountService users)
    {
        _db = db;
        _users = users;
    }

    public async Task<Result<PagedResult<AdminRestaurantProfileDto>>> Handle(
        ListAdminRestaurantsQuery request,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var users = await _users.ListUsersByTypeAsync(UserType.Restaurant, cancellationToken);
        var profiles = await _db.RestaurantProfiles
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var registrations = await _db.RestaurantRegistrations
            .AsNoTracking()
            .Where(x => x.SubmittedByUserId != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var profilesByUser = profiles
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(p => p.CreatedAtUtc).First());
        var registrationsByUser = registrations
            .GroupBy(x => x.SubmittedByUserId!.Value)
            .ToDictionary(x => x.Key, x => x.First());

        var rows = users
            .Select(user =>
            {
                profilesByUser.TryGetValue(user.Id, out var profile);
                registrationsByUser.TryGetValue(user.Id, out var registration);
                return AdminRestaurantProfileMapping.Map(user, profile, registration);
            })
            .ToList();

        // Keep legacy/orphaned profile rows visible even if their Identity user
        // cannot be resolved, while preventing duplicates for normal accounts.
        var knownUserIds = users.Select(x => x.Id).ToHashSet();
        rows.AddRange(profiles
            .Where(x => !knownUserIds.Contains(x.UserId))
            .Select(AdminRestaurantProfileMapping.Map));

        if (request.IsOperationallyActive is { } active)
        {
            rows = rows.Where(x => x.IsOperationallyActive == active).ToList();
        }

        var total = rows.Count;
        var dtos = rows
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToList();
        return new PagedResult<AdminRestaurantProfileDto>(dtos, total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Admin restaurant profile detail (composite: profile + registration + documents)
// ──────────────────────────────────────────────────────────────────────────────

public sealed record AdminRestaurantDetailResponseDto(
    AdminRestaurantProfileDto Profile,
    AdminRestaurantRegistrationDetailDto? Registration);

public sealed record GetAdminRestaurantProfileDetailQuery(Guid Id)
    : IQuery<AdminRestaurantDetailResponseDto>;

public sealed class GetAdminRestaurantProfileDetailQueryHandler
    : IQueryHandler<GetAdminRestaurantProfileDetailQuery, AdminRestaurantDetailResponseDto>
{
    private readonly IAppDbContext _db;

    public GetAdminRestaurantProfileDetailQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<AdminRestaurantDetailResponseDto>> Handle(
        GetAdminRestaurantProfileDetailQuery request,
        CancellationToken cancellationToken)
    {
        var profile = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant profile not found.");
        }

        var profileDto = AdminRestaurantProfileMapping.Map(profile);

        // Find the latest registration linked to this user,
        // preferring one whose ResultingRestaurantId matches this profile.
        var registration = await _db.RestaurantRegistrations
            .AsNoTracking()
            .Where(r =>
                r.ResultingRestaurantId == profile.Id ||
                r.SubmittedByUserId == profile.UserId)
            .OrderByDescending(r => r.ResultingRestaurantId == profile.Id ? 1 : 0)
            .ThenByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        AdminRestaurantRegistrationDetailDto? registrationDetail = null;

        if (registration is not null)
        {
            var regDto = RestaurantRegistrationMapping.Map(registration);

            var docs = await _db.Documents
                .Include(d => d.Verifications)
                .AsNoTracking()
                .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
                .ToListAsync(cancellationToken);

            var summaries = docs.Select(d =>
            {
                var latest = d.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                return new DocumentReviewSummaryDto(
                    d.Id, d.DocumentType, d.FileName, d.ContentType, d.SizeBytes,
                    d.CreatedAtUtc, d.ExpiresAtUtc, latest?.Status, latest?.Reason,
                    latest?.ReviewedAtUtc, latest?.ReviewedByUserId);
            }).ToList();

            var fieldReviews = await _db.RestaurantRegistrationFieldReviews
                .AsNoTracking()
                .Where(f => f.RegistrationId == registration.Id)
                .ToListAsync(cancellationToken);

            if (fieldReviews.Count == 0 && registration.Status is not VerificationStatus.Draft)
            {
                fieldReviews = RegistrationFieldKeys.All
                    .Select(fk => new RestaurantRegistrationFieldReview
                    {
                        FieldKey = fk,
                        Status = VerificationStatus.Submitted,
                    })
                    .ToList();
            }

            var fieldSummaries = fieldReviews.Select(RestaurantRegistrationMapping.MapFieldReview).ToList();
            registrationDetail = new AdminRestaurantRegistrationDetailDto(regDto, summaries, fieldSummaries);
        }

        return new AdminRestaurantDetailResponseDto(profileDto, registrationDetail);
    }
}

// Keep the original simple query for backward compatibility
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
    public static AdminRestaurantProfileDto Map(
        UserAccountDto user,
        RestaurantProfile? profile,
        MealMate.Domain.IdentityAccess.RestaurantRegistration? registration)
    {
        var accountName = FirstNonEmpty(user.FullName, user.Email, user.PhoneNumber, user.Id.ToString());

        return new AdminRestaurantProfileDto(
            profile?.Id ?? user.Id,
            user.Id,
            FirstNonEmpty(profile?.LegalNameAr, registration?.LegalNameAr, accountName),
            FirstNonEmpty(profile?.LegalNameEn, registration?.LegalNameEn, accountName),
            FirstNonEmpty(profile?.TradeNameAr, registration?.TradeNameAr, accountName),
            FirstNonEmpty(profile?.TradeNameEn, registration?.TradeNameEn, accountName),
            profile?.CommercialLicenseNumber ?? registration?.CommercialRegistrationNumber,
            profile?.CountryId ?? registration?.CountryId,
            profile?.PrimaryRegionId ?? registration?.RegionId,
            profile?.VerificationStatus ?? registration?.Status ?? VerificationStatus.Draft,
            profile?.IsOperationallyActive ?? false,
            profile?.ActivatedAtUtc,
            FirstNonEmpty(profile?.ContactPhone, registration?.ContactPhone, user.PhoneNumber),
            FirstNonEmpty(profile?.ContactEmail, registration?.ContactEmail, user.Email),
            user.CreatedAtUtc,
            profile?.Id,
            registration?.Id,
            user.AccountStatus,
            FirstNonEmpty(registration?.OwnerName, user.FullName));
    }

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
        p.CreatedAtUtc,
        p.Id,
        null,
        p.IsOperationallyActive
            ? AccountStatus.Active
            : p.ActivatedAtUtc is null
                ? AccountStatus.Registered
                : AccountStatus.Suspended,
        string.Empty);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}
