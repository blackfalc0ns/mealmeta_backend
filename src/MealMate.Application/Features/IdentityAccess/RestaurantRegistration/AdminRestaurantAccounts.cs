using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Storage;
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
    Guid? ReviewedByUserId,
    string? ReviewedByName = null);

public sealed record AdminRestaurantRegistrationDetailDto(
    RestaurantRegistrationDto Registration,
    IReadOnlyList<DocumentReviewSummaryDto> Documents,
    IReadOnlyList<FieldReviewSummaryDto> FieldReviews,
    string? ReviewedByName = null,
    string? ResultingRestaurantNameAr = null,
    string? ResultingRestaurantNameEn = null,
    bool SensitiveDataMasked = true,
    bool CanRevealSensitive = false);

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
    string OwnerName,
    bool IsArchived = false,
    DateTime? ClosedAtUtc = null,
    string? LogoUrl = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

internal static class AdminRestaurantSensitiveDataAccess
{
    public static bool CanReveal(ICurrentUser currentUser) =>
        currentUser.IsInRole(AppRoles.SuperAdmin)
        || currentUser.HasPermission(PermissionCodes.RestaurantRegApprove);
}

// ──────────────────────────────────────────────────────────────────────────────
// Admin registration detail (aggregates documents with review metadata)
// ──────────────────────────────────────────────────────────────────────────────

public sealed record GetAdminRestaurantRegistrationDetailQuery(Guid Id, bool IncludeSensitive = false)
    : IQuery<AdminRestaurantRegistrationDetailDto>;

public sealed class GetAdminRestaurantRegistrationDetailQueryHandler
    : IQueryHandler<GetAdminRestaurantRegistrationDetailQuery, AdminRestaurantRegistrationDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;

    public GetAdminRestaurantRegistrationDetailQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
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

        var canRevealSensitive = AdminRestaurantSensitiveDataAccess.CanReveal(_currentUser);
        if (request.IncludeSensitive && !canRevealSensitive)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed to reveal sensitive registration data.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .AsNoTracking()
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        var latestVerifications = docs.ToDictionary(
            document => document.Id,
            document => document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault());
        var reviewerIds = latestVerifications.Values
            .Select(verification => verification?.ReviewedByUserId)
            .Append(registration.ReviewedByUserId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var reviewerNames = (await _users.GetUserIdentitiesAsync(reviewerIds, cancellationToken))
            .ToDictionary(identity => identity.Id, identity => identity.FullName);

        var summaries = docs.Select(d =>
        {
            var latest = latestVerifications[d.Id];
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
                latest?.ReviewedByUserId,
                latest?.ReviewedByUserId is Guid reviewerId
                    ? reviewerNames.GetValueOrDefault(reviewerId)
                    : null);
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
        var dto = RestaurantRegistrationMapping.Map(registration, maskSensitive: !request.IncludeSensitive);
        var resultingRestaurant = registration.ResultingRestaurantId is Guid restaurantId
            ? await _db.RestaurantProfiles
                .AsNoTracking()
                .Where(profile => profile.Id == restaurantId)
                .Select(profile => new { profile.TradeNameAr, profile.TradeNameEn })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var reviewedByName = registration.ReviewedByUserId is Guid reviewedByUserId
            ? reviewerNames.GetValueOrDefault(reviewedByUserId)
            : null;

        return new AdminRestaurantRegistrationDetailDto(
            dto,
            summaries,
            fieldSummaries,
            reviewedByName,
            resultingRestaurant?.TradeNameAr,
            resultingRestaurant?.TradeNameEn,
            SensitiveDataMasked: !request.IncludeSensitive,
            CanRevealSensitive: canRevealSensitive);
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
            .Select(profile => AdminRestaurantProfileMapping.Map(profile)));

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

        var logoUrls = await RestaurantLogoUrlResolver.ResolveBatchAsync(
            _db,
            dtos.Where(d => d.ProfileId.HasValue).Select(d => d.ProfileId!.Value),
            cancellationToken);
        if (logoUrls.Count > 0)
        {
            dtos = dtos
                .Select(d => d.ProfileId is { } profileId && logoUrls.TryGetValue(profileId, out var logoUrl)
                    ? d with { LogoUrl = logoUrl }
                    : d)
                .ToList();
        }

        return new PagedResult<AdminRestaurantProfileDto>(dtos, total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Admin restaurant profile detail (composite: profile + registration + documents)
// ──────────────────────────────────────────────────────────────────────────────

public sealed record AdminRestaurantDetailResponseDto(
    AdminRestaurantProfileDto Profile,
    AdminRestaurantRegistrationDetailDto? Registration,
    bool SensitiveDataMasked = true,
    bool CanRevealSensitive = false);

public sealed record GetAdminRestaurantProfileDetailQuery(Guid Id, bool IncludeSensitive = false)
    : IQuery<AdminRestaurantDetailResponseDto>;

public sealed class GetAdminRestaurantProfileDetailQueryHandler
    : IQueryHandler<GetAdminRestaurantProfileDetailQuery, AdminRestaurantDetailResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;

    public GetAdminRestaurantProfileDetailQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
    }

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

        var canRevealSensitive = AdminRestaurantSensitiveDataAccess.CanReveal(_currentUser);
        if (request.IncludeSensitive && !canRevealSensitive)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed to reveal sensitive restaurant data.");
        }

        var profileDto = AdminRestaurantProfileMapping.Map(
            profile,
            maskSensitive: !request.IncludeSensitive);

        var owner = await _users.FindByIdAsync(profile.UserId, cancellationToken);
        if (owner is not null)
        {
            profileDto = profileDto with { AccountStatus = owner.AccountStatus };
        }

        profileDto = profileDto with
        {
            LogoUrl = await RestaurantLogoUrlResolver.ResolveAsync(_db, profile.Id, cancellationToken),
        };

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
            var regDto = RestaurantRegistrationMapping.Map(
                registration,
                maskSensitive: !request.IncludeSensitive);

            var docs = await _db.Documents
                .Include(d => d.Verifications)
                .AsNoTracking()
                .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
                .ToListAsync(cancellationToken);

            var latestVerifications = docs.ToDictionary(
                document => document.Id,
                document => document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault());
            var reviewerIds = latestVerifications.Values
                .Select(verification => verification?.ReviewedByUserId)
                .Append(registration.ReviewedByUserId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
            var reviewerNames = (await _users.GetUserIdentitiesAsync(reviewerIds, cancellationToken))
                .ToDictionary(identity => identity.Id, identity => identity.FullName);

            var summaries = docs.Select(d =>
            {
                var latest = latestVerifications[d.Id];
                return new DocumentReviewSummaryDto(
                    d.Id, d.DocumentType, d.FileName, d.ContentType, d.SizeBytes,
                    d.CreatedAtUtc, d.ExpiresAtUtc, latest?.Status, latest?.Reason,
                    latest?.ReviewedAtUtc, latest?.ReviewedByUserId,
                    latest?.ReviewedByUserId is Guid reviewerId
                        ? reviewerNames.GetValueOrDefault(reviewerId)
                        : null);
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
            var reviewedByName = registration.ReviewedByUserId is Guid reviewedByUserId
                ? reviewerNames.GetValueOrDefault(reviewedByUserId)
                : null;
            registrationDetail = new AdminRestaurantRegistrationDetailDto(
                regDto,
                summaries,
                fieldSummaries,
                reviewedByName,
                profile.TradeNameAr,
                profile.TradeNameEn,
                SensitiveDataMasked: !request.IncludeSensitive,
                CanRevealSensitive: canRevealSensitive);
        }

        return new AdminRestaurantDetailResponseDto(
            profileDto,
            registrationDetail,
            SensitiveDataMasked: !request.IncludeSensitive,
            CanRevealSensitive: canRevealSensitive);
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

        var dto = AdminRestaurantProfileMapping.Map(profile);
        return dto with
        {
            LogoUrl = await RestaurantLogoUrlResolver.ResolveAsync(_db, profile.Id, cancellationToken),
        };
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
    private readonly IAuthSessionService _sessions;

    public AdminRestaurantLifecycleCommandHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IAuthSessionService sessions)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _sessions = sessions;
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
        var before = AdminRestaurantProfileMapping.MapAuditSnapshot(profile);
        var linkedAccountIds = await ResolveLinkedAccountIdsAsync(profile, cancellationToken);

        switch (action)
        {
            case "activate":
                if (profile.ClosedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is closed.");
                }

                if (profile.IsArchived)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is archived.");
                }
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

                foreach (var userId in linkedAccountIds)
                {
                    await _users.UpdateAccountStatusAsync(userId, AccountStatus.Active, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantActivated",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "suspend":
                if (profile.ClosedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is closed.");
                }

                if (profile.IsArchived)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is archived.");
                }

                if (!profile.IsOperationallyActive)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is already suspended.");
                }

                profile.IsOperationallyActive = false;
                profile.ModifiedAtUtc = _clock.UtcNow;
                profile.ModifiedByUserId = _currentUser.UserId;

                foreach (var userId in linkedAccountIds)
                {
                    await _users.UpdateAccountStatusAsync(userId, AccountStatus.Suspended, cancellationToken);
                    await _sessions.RevokeAllUserTokensAsync(userId, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantSuspended",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "reactivate":
                if (profile.ClosedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is closed.");
                }

                if (profile.IsArchived)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is archived.");
                }

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

                foreach (var userId in linkedAccountIds)
                {
                    await _users.UpdateAccountStatusAsync(userId, AccountStatus.Active, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantReactivated",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "lock-login":
                if (profile.ClosedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is closed.");
                }

                foreach (var userId in linkedAccountIds)
                {
                    await _users.UpdateAccountStatusAsync(userId, AccountStatus.Suspended, cancellationToken);
                    await _sessions.RevokeAllUserTokensAsync(userId, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantLoginLocked",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "archive":
                if (profile.ClosedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is closed.");
                }

                if (profile.IsArchived)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is already archived.");
                }

                profile.IsArchived = true;
                profile.IsOperationallyActive = false;
                profile.ModifiedAtUtc = _clock.UtcNow;
                profile.ModifiedByUserId = _currentUser.UserId;

                foreach (var userId in linkedAccountIds)
                {
                    await _users.UpdateAccountStatusAsync(userId, AccountStatus.Suspended, cancellationToken);
                    await _sessions.RevokeAllUserTokensAsync(userId, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantArchived",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "close":
                if (profile.ClosedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is already closed.");
                }

                profile.IsArchived = true;
                profile.IsOperationallyActive = false;
                profile.ClosedAtUtc = _clock.UtcNow;
                profile.ModifiedAtUtc = _clock.UtcNow;
                profile.ModifiedByUserId = _currentUser.UserId;

                var allLinkedIds = await ResolveLinkedAccountIdsAsync(profile, cancellationToken, includeClosed: true);
                foreach (var userId in allLinkedIds)
                {
                    await _users.UpdateAccountStatusAsync(userId, AccountStatus.Closed, cancellationToken);
                    await _sessions.RevokeAllUserTokensAsync(userId, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantClosed",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "unlock-login":
                if (profile.ClosedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is closed.");
                }

                if (profile.IsArchived)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is archived.");
                }

                if (!profile.IsOperationallyActive)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is operationally suspended; use reactivate instead.");
                }

                var ownerAccount = await _users.FindByIdAsync(profile.UserId, cancellationToken);
                if (ownerAccount is null || ownerAccount.AccountStatus != AccountStatus.Suspended)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant login is not locked.");
                }

                foreach (var userId in linkedAccountIds)
                {
                    await _users.UpdateAccountStatusAsync(userId, AccountStatus.Active, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantLoginUnlocked",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "unarchive":
                if (profile.ClosedAtUtc is not null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is closed.");
                }

                if (!profile.IsArchived)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant is not archived.");
                }

                profile.IsArchived = false;
                profile.ModifiedAtUtc = _clock.UtcNow;
                profile.ModifiedByUserId = _currentUser.UserId;

                foreach (var userId in linkedAccountIds)
                {
                    await _users.UpdateAccountStatusAsync(
                        userId,
                        ResolveLinkedAccountStatus(profile),
                        cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantUnarchived",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            case "reopen":
                if (profile.ClosedAtUtc is null)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Restaurant account is not closed.");
                }

                profile.IsArchived = false;
                profile.ClosedAtUtc = null;
                profile.ModifiedAtUtc = _clock.UtcNow;
                profile.ModifiedByUserId = _currentUser.UserId;

                var reopenedLinkedIds = await ResolveLinkedAccountIdsAsync(profile, cancellationToken, includeClosed: true);
                foreach (var userId in reopenedLinkedIds)
                {
                    await _users.UpdateAccountStatusAsync(
                        userId,
                        ResolveLinkedAccountStatus(profile),
                        cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "RestaurantReopened",
                    nameof(RestaurantProfile),
                    profile.Id,
                    Before: before,
                    After: AdminRestaurantProfileMapping.MapAuditSnapshot(profile),
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);
                break;

            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        await _db.SaveChangesAsync(cancellationToken);

        var dto = AdminRestaurantProfileMapping.Map(profile);
        return dto with
        {
            LogoUrl = await RestaurantLogoUrlResolver.ResolveAsync(_db, profile.Id, cancellationToken),
        };
    }

    private async Task<List<Guid>> ResolveLinkedAccountIdsAsync(
        RestaurantProfile profile,
        CancellationToken cancellationToken,
        bool includeClosed = false)
    {
        var linkedAccountIds = new HashSet<Guid>();

        if (profile.UserId != Guid.Empty)
        {
            var owner = await _users.FindByIdAsync(profile.UserId, cancellationToken);
            if (owner is not null && (includeClosed || owner.AccountStatus != AccountStatus.Closed))
            {
                linkedAccountIds.Add(profile.UserId);
            }
        }

        var restaurantUsers = await _users.ListUsersByTypeAsync(UserType.Restaurant, cancellationToken);
        foreach (var user in restaurantUsers.Where(user => user.RestaurantId == profile.Id))
        {
            if (includeClosed || user.AccountStatus != AccountStatus.Closed)
            {
                linkedAccountIds.Add(user.Id);
            }
        }

        var deliveryManagers = await _users.ListUsersByTypeAsync(UserType.DeliveryManager, cancellationToken);
        foreach (var user in deliveryManagers.Where(user => user.RestaurantId == profile.Id))
        {
            if (includeClosed || user.AccountStatus != AccountStatus.Closed)
            {
                linkedAccountIds.Add(user.Id);
            }
        }

        return linkedAccountIds.ToList();
    }

    private static AccountStatus ResolveLinkedAccountStatus(RestaurantProfile profile) =>
        profile.IsOperationallyActive
            ? AccountStatus.Active
            : profile.ActivatedAtUtc is null
                ? AccountStatus.Registered
                : AccountStatus.Suspended;
}

// ──────────────────────────────────────────────────────────────────────────────
// Mapping
// ──────────────────────────────────────────────────────────────────────────────

file static class AdminRestaurantProfileMapping
{
    public static AdminRestaurantProfileDto Map(
        UserAccountDto user,
        RestaurantProfile? profile,
        MealMate.Domain.IdentityAccess.RestaurantRegistration? registration,
        bool maskSensitive = true)
    {
        var accountName = FirstNonEmpty(user.FullName, user.Email, user.PhoneNumber, user.Id.ToString());
        var fallbackAccountName = maskSensitive
            ? RestaurantRegistrationRules.MaskSensitive(accountName) ?? string.Empty
            : accountName;
        var contactPhone = FirstNonEmpty(profile?.ContactPhone, registration?.ContactPhone, user.PhoneNumber);
        var contactEmail = FirstNonEmpty(profile?.ContactEmail, registration?.ContactEmail, user.Email);
        var ownerName = FirstNonEmpty(registration?.OwnerName, user.FullName);
        var commercialLicenseNumber = profile?.CommercialLicenseNumber ?? registration?.CommercialLicenseNumber;

        return new AdminRestaurantProfileDto(
            profile?.Id ?? user.Id,
            user.Id,
            FirstNonEmpty(profile?.LegalNameAr, registration?.LegalNameAr, fallbackAccountName),
            FirstNonEmpty(profile?.LegalNameEn, registration?.LegalNameEn, fallbackAccountName),
            FirstNonEmpty(profile?.TradeNameAr, registration?.TradeNameAr, fallbackAccountName),
            FirstNonEmpty(profile?.TradeNameEn, registration?.TradeNameEn, fallbackAccountName),
            maskSensitive ? RestaurantRegistrationRules.MaskSensitive(commercialLicenseNumber) : commercialLicenseNumber,
            profile?.CountryId ?? registration?.CountryId,
            profile?.PrimaryRegionId ?? registration?.RegionId,
            profile?.VerificationStatus ?? registration?.Status ?? VerificationStatus.Draft,
            profile?.IsOperationallyActive ?? false,
            profile?.ActivatedAtUtc,
            maskSensitive ? RestaurantRegistrationRules.MaskSensitive(contactPhone) : contactPhone,
            maskSensitive ? RestaurantRegistrationRules.MaskSensitive(contactEmail) : contactEmail,
            user.CreatedAtUtc,
            profile?.Id,
            registration?.Id,
            user.AccountStatus,
            maskSensitive ? RestaurantRegistrationRules.MaskSensitive(ownerName) ?? string.Empty : ownerName,
            profile?.IsArchived ?? false,
            profile?.ClosedAtUtc,
            LogoUrl: null,
            Latitude: profile?.Latitude ?? registration?.Latitude,
            Longitude: profile?.Longitude ?? registration?.Longitude);
    }

    public static AdminRestaurantProfileDto Map(RestaurantProfile p, bool maskSensitive = true) => new(
        p.Id,
        p.UserId,
        p.LegalNameAr,
        p.LegalNameEn,
        p.TradeNameAr,
        p.TradeNameEn,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(p.CommercialLicenseNumber) : p.CommercialLicenseNumber,
        p.CountryId,
        p.PrimaryRegionId,
        p.VerificationStatus,
        p.IsOperationallyActive,
        p.ActivatedAtUtc,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(p.ContactPhone) : p.ContactPhone,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(p.ContactEmail) : p.ContactEmail,
        p.CreatedAtUtc,
        p.Id,
        null,
        p.IsOperationallyActive
            ? AccountStatus.Active
            : p.ActivatedAtUtc is null
                ? AccountStatus.Registered
                : AccountStatus.Suspended,
        string.Empty,
        p.IsArchived,
        p.ClosedAtUtc,
        LogoUrl: null,
        Latitude: p.Latitude,
        Longitude: p.Longitude);

    public static object MapAuditSnapshot(RestaurantProfile p) => new
    {
        p.Id,
        p.UserId,
        p.LegalNameAr,
        p.LegalNameEn,
        p.TradeNameAr,
        p.TradeNameEn,
        CommercialLicenseNumber = RestaurantRegistrationRules.MaskSensitive(p.CommercialLicenseNumber),
        p.CountryId,
        p.PrimaryRegionId,
        p.Latitude,
        p.Longitude,
        p.VerificationStatus,
        p.IsOperationallyActive,
        p.IsArchived,
        p.ClosedAtUtc,
        p.ActivatedAtUtc,
        ContactPhone = RestaurantRegistrationRules.MaskSensitive(p.ContactPhone),
        ContactEmail = RestaurantRegistrationRules.MaskSensitive(p.ContactEmail),
        p.CreatedAtUtc,
        p.ModifiedAtUtc,
    };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}

file static class RestaurantLogoUrlResolver
{
    public static async Task<string?> ResolveAsync(
        IAppDbContext db,
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        var storagePath = await db.Documents
            .AsNoTracking()
            .Where(x => x.OwnerType == DocumentOwnerType.Restaurant
                && x.OwnerId == restaurantId
                && (x.DocumentType == DocumentType.ProfileImage || x.DocumentType == DocumentType.BranchPhoto))
            .OrderByDescending(x => x.DocumentType == DocumentType.ProfileImage)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => x.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        return CatalogImageStorage.ToContentUrl(storagePath);
    }

    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveBatchAsync(
        IAppDbContext db,
        IEnumerable<Guid> restaurantIds,
        CancellationToken cancellationToken)
    {
        var ids = restaurantIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await db.Documents
            .AsNoTracking()
            .Where(x => x.OwnerType == DocumentOwnerType.Restaurant
                && ids.Contains(x.OwnerId)
                && (x.DocumentType == DocumentType.ProfileImage || x.DocumentType == DocumentType.BranchPhoto))
            .OrderByDescending(x => x.DocumentType == DocumentType.ProfileImage)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.OwnerId, x.StoragePath })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.OwnerId)
            .Select(g => g.First())
            .Select(x => new { x.OwnerId, Url = CatalogImageStorage.ToContentUrl(x.StoragePath) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .ToDictionary(x => x.OwnerId, x => x.Url!);
    }
}
