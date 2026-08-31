using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Audit;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record RestaurantCommercialSettingsDto(
    Guid RestaurantId,
    decimal CommissionPercent,
    int ActiveOfferingCount);

public sealed record UpdateRestaurantCommercialSettingsCommand(
    Guid RestaurantId,
    decimal CommissionPercent) : ICommand<RestaurantCommercialSettingsDto>;

public sealed class UpdateRestaurantCommercialSettingsCommandValidator
    : AbstractValidator<UpdateRestaurantCommercialSettingsCommand>
{
    public UpdateRestaurantCommercialSettingsCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.CommissionPercent).InclusiveBetween(0, 100);
    }
}

public sealed record RestaurantOperationalPreferencesDto(
    Guid RestaurantId,
    bool AutoConfirmOrders,
    bool ManualBusyOverride);

public sealed record UpdateRestaurantOperationalPreferencesCommand(
    Guid RestaurantId,
    bool AutoConfirmOrders,
    bool ManualBusyOverride) : ICommand<RestaurantOperationalPreferencesDto>;

public sealed record RestaurantNotificationPreferenceDto(
    string EventKey,
    bool EmailEnabled,
    bool PushEnabled);

public sealed record RestaurantNotificationPreferencesDto(
    Guid RestaurantId,
    IReadOnlyList<RestaurantNotificationPreferenceDto> Preferences);

public sealed record UpdateRestaurantNotificationPreferencesCommand(
    Guid RestaurantId,
    IReadOnlyList<RestaurantNotificationPreferenceDto> Preferences)
    : ICommand<RestaurantNotificationPreferencesDto>;

public sealed class UpdateRestaurantNotificationPreferencesCommandValidator
    : AbstractValidator<UpdateRestaurantNotificationPreferencesCommand>
{
    public UpdateRestaurantNotificationPreferencesCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleForEach(x => x.Preferences).ChildRules(item =>
        {
            item.RuleFor(x => x.EventKey).NotEmpty().MaximumLength(80);
        });
    }
}

public sealed record GetRestaurantCommercialSettingsQuery(Guid RestaurantId)
    : IQuery<RestaurantCommercialSettingsDto>;

public sealed record GetRestaurantOperationalPreferencesQuery(Guid RestaurantId)
    : IQuery<RestaurantOperationalPreferencesDto>;

public sealed record GetRestaurantNotificationPreferencesQuery(Guid RestaurantId)
    : IQuery<RestaurantNotificationPreferencesDto>;

public sealed record GetRestaurantAuditLogQuery(
    Guid RestaurantId,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AuditLogDto>>;

internal static class RestaurantNotificationPreferenceDefaults
{
    internal static readonly IReadOnlyList<RestaurantNotificationPreferenceDto> Items =
    [
        new("new_orders", true, true),
        new("confirmation_deadline", true, false),
        new("capacity_near_limit", true, true),
        new("document_expiry", true, false),
    ];
}

public sealed class RestaurantAdminSettingsHandlers :
    IQueryHandler<GetRestaurantCommercialSettingsQuery, RestaurantCommercialSettingsDto>,
    ICommandHandler<UpdateRestaurantCommercialSettingsCommand, RestaurantCommercialSettingsDto>,
    IQueryHandler<GetRestaurantOperationalPreferencesQuery, RestaurantOperationalPreferencesDto>,
    ICommandHandler<UpdateRestaurantOperationalPreferencesCommand, RestaurantOperationalPreferencesDto>,
    IQueryHandler<GetRestaurantNotificationPreferencesQuery, RestaurantNotificationPreferencesDto>,
    ICommandHandler<UpdateRestaurantNotificationPreferencesCommand, RestaurantNotificationPreferencesDto>,
    IQueryHandler<GetRestaurantAuditLogQuery, PagedResult<AuditLogDto>>
{
    private const decimal DefaultCommissionPercent = 12m;

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IUserAccountService _users;

    public RestaurantAdminSettingsHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IUserAccountService users)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _users = users;
    }

    public async Task<Result<RestaurantCommercialSettingsDto>> Handle(
        GetRestaurantCommercialSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var profileResult = await LoadProfileAsync(request.RestaurantId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;
        var offeringCount = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .CountAsync(x => x.RestaurantId == request.RestaurantId && x.IsActive, cancellationToken);

        return new RestaurantCommercialSettingsDto(
            profile.Id,
            profile.CommissionPercent ?? DefaultCommissionPercent,
            offeringCount);
    }

    public async Task<Result<RestaurantCommercialSettingsDto>> Handle(
        UpdateRestaurantCommercialSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var profileResult = await LoadProfileAsync(request.RestaurantId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;
        var before = MapCommercialSnapshot(profile);
        profile.CommissionPercent = request.CommissionPercent;
        profile.ModifiedAtUtc = _clock.UtcNow;
        profile.ModifiedByUserId = _currentUser.UserId;

        await _audit.LogAsync(new AuditEntry(
            "RestaurantCommissionUpdated",
            nameof(RestaurantProfile),
            profile.Id,
            Before: before,
            After: MapCommercialSnapshot(profile),
            SourceChannel: "admin"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var offeringCount = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .CountAsync(x => x.RestaurantId == request.RestaurantId && x.IsActive, cancellationToken);

        return new RestaurantCommercialSettingsDto(
            profile.Id,
            profile.CommissionPercent ?? DefaultCommissionPercent,
            offeringCount);
    }

    public async Task<Result<RestaurantOperationalPreferencesDto>> Handle(
        GetRestaurantOperationalPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        var profileResult = await LoadProfileAsync(request.RestaurantId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;
        return new RestaurantOperationalPreferencesDto(
            profile.Id,
            profile.AutoConfirmOrders,
            profile.ManualBusyOverride);
    }

    public async Task<Result<RestaurantOperationalPreferencesDto>> Handle(
        UpdateRestaurantOperationalPreferencesCommand request,
        CancellationToken cancellationToken)
    {
        var profileResult = await LoadProfileAsync(request.RestaurantId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;
        var before = MapOperationalSnapshot(profile);
        profile.AutoConfirmOrders = request.AutoConfirmOrders;
        profile.ManualBusyOverride = request.ManualBusyOverride;
        profile.ModifiedAtUtc = _clock.UtcNow;
        profile.ModifiedByUserId = _currentUser.UserId;

        await _audit.LogAsync(new AuditEntry(
            "RestaurantOperationalPreferencesUpdated",
            nameof(RestaurantProfile),
            profile.Id,
            Before: before,
            After: MapOperationalSnapshot(profile),
            SourceChannel: "admin"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new RestaurantOperationalPreferencesDto(
            profile.Id,
            profile.AutoConfirmOrders,
            profile.ManualBusyOverride);
    }

    public async Task<Result<RestaurantNotificationPreferencesDto>> Handle(
        GetRestaurantNotificationPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        var profileResult = await LoadProfileAsync(request.RestaurantId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var stored = await _db.RestaurantNotificationPreferences.AsNoTracking()
            .Where(x => x.RestaurantId == request.RestaurantId)
            .ToListAsync(cancellationToken);

        var preferences = MergeNotificationPreferences(stored);
        return new RestaurantNotificationPreferencesDto(request.RestaurantId, preferences);
    }

    public async Task<Result<RestaurantNotificationPreferencesDto>> Handle(
        UpdateRestaurantNotificationPreferencesCommand request,
        CancellationToken cancellationToken)
    {
        var profileResult = await LoadProfileAsync(request.RestaurantId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var incoming = request.Preferences
            .Where(x => !string.IsNullOrWhiteSpace(x.EventKey))
            .GroupBy(x => x.EventKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        var allowedKeys = RestaurantNotificationPreferenceDefaults.Items
            .Select(x => x.EventKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (incoming.Any(x => !allowedKeys.Contains(x.EventKey)))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                "One or more notification event keys are not supported.");
        }

        var existing = await _db.RestaurantNotificationPreferences
            .Where(x => x.RestaurantId == request.RestaurantId)
            .ToListAsync(cancellationToken);

        var before = existing.Select(x => new RestaurantNotificationPreferenceDto(
            x.EventKey,
            x.EmailEnabled,
            x.PushEnabled)).ToList();

        _db.RestaurantNotificationPreferences.RemoveRange(existing);

        var now = _clock.UtcNow;
        var saved = incoming.Select(x => new RestaurantNotificationPreference
        {
            RestaurantId = request.RestaurantId,
            EventKey = x.EventKey.Trim(),
            EmailEnabled = x.EmailEnabled,
            PushEnabled = x.PushEnabled,
            CreatedAtUtc = now,
            CreatedByUserId = _currentUser.UserId,
        }).ToList();

        _db.RestaurantNotificationPreferences.AddRange(saved);

        await _audit.LogAsync(new AuditEntry(
            "RestaurantNotificationPreferencesUpdated",
            nameof(RestaurantNotificationPreference),
            request.RestaurantId,
            Before: before,
            After: incoming,
            SourceChannel: "admin"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new RestaurantNotificationPreferencesDto(
            request.RestaurantId,
            MergeNotificationPreferences(saved));
    }

    public async Task<Result<PagedResult<AuditLogDto>>> Handle(
        GetRestaurantAuditLogQuery request,
        CancellationToken cancellationToken)
    {
        var profileResult = await LoadProfileAsync(request.RestaurantId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var entityTypes = new[]
        {
            nameof(RestaurantProfile),
            nameof(RestaurantNotificationPreference),
            nameof(RestaurantProgramBundleOffer),
            nameof(RestaurantSettingsChangeRequest),
            nameof(RestaurantReviewItem),
        };

        var query = _db.AuditLogs.AsNoTracking()
            .Where(x =>
                (x.EntityId == request.RestaurantId && entityTypes.Contains(x.EntityType))
                || (x.EntityType == nameof(RestaurantProfile) && x.EntityId == request.RestaurantId));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .Select(x => new AuditLogDto(
                x.Id,
                x.ActorUserId,
                null,
                null,
                x.Action,
                x.EntityType,
                x.EntityId,
                null,
                null,
                x.BeforeJson,
                x.AfterJson,
                x.Reason,
                x.CorrelationId,
                x.SourceChannel,
                x.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        items = await ListAuditLogsQueryHandler.EnrichIdentitiesAsync(items, _users, cancellationToken);

        return new PagedResult<AuditLogDto>(items, total, page.NormalizedPage, page.NormalizedPageSize);
    }

    private async Task<Result<RestaurantProfile>> LoadProfileAsync(
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        var profile = await _db.RestaurantProfiles
            .FirstOrDefaultAsync(x => x.Id == restaurantId, cancellationToken);

        return profile is null
            ? Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant profile not found.")
            : profile;
    }

    private static object MapCommercialSnapshot(RestaurantProfile profile) => new
    {
        profile.Id,
        CommissionPercent = profile.CommissionPercent ?? DefaultCommissionPercent,
    };

    private static object MapOperationalSnapshot(RestaurantProfile profile) => new
    {
        profile.Id,
        profile.AutoConfirmOrders,
        profile.ManualBusyOverride,
    };

    private static IReadOnlyList<RestaurantNotificationPreferenceDto> MergeNotificationPreferences(
        IEnumerable<RestaurantNotificationPreference> stored)
    {
        var byKey = stored.ToDictionary(x => x.EventKey, StringComparer.OrdinalIgnoreCase);
        return RestaurantNotificationPreferenceDefaults.Items
            .Select(defaultPref =>
            {
                if (byKey.TryGetValue(defaultPref.EventKey, out var saved))
                {
                    return new RestaurantNotificationPreferenceDto(
                        defaultPref.EventKey,
                        saved.EmailEnabled,
                        saved.PushEnabled);
                }

                return defaultPref;
            })
            .ToList();
    }
}
