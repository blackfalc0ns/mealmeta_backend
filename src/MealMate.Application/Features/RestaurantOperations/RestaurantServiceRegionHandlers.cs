using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Application.Features.Catalog;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantOperations;

public sealed class GetRestaurantServiceRegionsQueryHandler
    : IQueryHandler<GetRestaurantServiceRegionsQuery, List<RestaurantServiceRegionItemDto>>
{
    private readonly RestaurantServiceRegionService _service;

    public GetRestaurantServiceRegionsQueryHandler(RestaurantServiceRegionService service)
    {
        _service = service;
    }

    public Task<Result<List<RestaurantServiceRegionItemDto>>> Handle(GetRestaurantServiceRegionsQuery request, CancellationToken cancellationToken)
    {
        return _service.GetServiceRegionsAsync(cancellationToken);
    }
}

public sealed class GetRestaurantServiceRegionDetailQueryHandler
    : IQueryHandler<GetRestaurantServiceRegionDetailQuery, RestaurantServiceRegionItemDto>
{
    private readonly RestaurantServiceRegionService _service;

    public GetRestaurantServiceRegionDetailQueryHandler(RestaurantServiceRegionService service)
    {
        _service = service;
    }

    public Task<Result<RestaurantServiceRegionItemDto>> Handle(GetRestaurantServiceRegionDetailQuery request, CancellationToken cancellationToken)
    {
        return _service.GetServiceRegionDetailAsync(request.RegionId, cancellationToken);
    }
}

public sealed class UpdateRestaurantServiceRegionCommandHandler
    : ICommandHandler<UpdateRestaurantServiceRegionCommand, RestaurantServiceRegionItemDto>
{
    private readonly RestaurantServiceRegionService _service;

    public UpdateRestaurantServiceRegionCommandHandler(RestaurantServiceRegionService service)
    {
        _service = service;
    }

    public Task<Result<RestaurantServiceRegionItemDto>> Handle(UpdateRestaurantServiceRegionCommand request, CancellationToken cancellationToken)
    {
        return _service.UpdateServiceRegionAsync(request, cancellationToken);
    }
}

public sealed class UpdateRestaurantServiceRegionSelectionCommandHandler
    : ICommandHandler<UpdateRestaurantServiceRegionSelectionCommand, List<RestaurantServiceRegionItemDto>>
{
    private readonly RestaurantServiceRegionService _service;

    public UpdateRestaurantServiceRegionSelectionCommandHandler(RestaurantServiceRegionService service)
    {
        _service = service;
    }

    public Task<Result<List<RestaurantServiceRegionItemDto>>> Handle(
        UpdateRestaurantServiceRegionSelectionCommand request,
        CancellationToken cancellationToken)
    {
        return _service.UpdateServiceRegionSelectionAsync(request.RegionIds, cancellationToken);
    }
}

public sealed class RestaurantServiceRegionService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly KuwaitTenantResolver _tenantResolver;

    public RestaurantServiceRegionService(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock, KuwaitTenantResolver tenantResolver)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _tenantResolver = tenantResolver;
    }

    public async Task<Result<List<RestaurantServiceRegionItemDto>>> GetServiceRegionsAsync(CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
        {
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(Error.Unauthorized("restaurant.id_required"));
            /*
            var catalogRegions = await _db.Regions
                .AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.NameAr)
                .Select(r => new RestaurantServiceRegionItemDto(
                    r.Id,
                    r.Code,
                    r.NameAr,
                    r.NameEn,
                    r.CountryId,
                    true,
                    TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)),
                    TimeOnly.FromTimeSpan(TimeSpan.FromHours(22)),
                    null,
                    0,
                    null
                ))
                .ToListAsync(cancellationToken);

            return Result.Success(catalogRegions);
            */
        }

        var tenantResult = await _tenantResolver.ResolveAsync(cancellationToken);
        if (tenantResult.IsFailure)
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(tenantResult.Error);
        var tenant = tenantResult.Value;

        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == restaurantId.Value, cancellationToken);

        if (restaurant == null)
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(Error.NotFound("restaurant.not_found", "Restaurant profile not found."));

        var systemRegions = await _db.Regions
            .AsNoTracking()
            .Include(r => r.ParentRegion)
            .Where(r => r.IsActive && r.Type == Domain.Enums.RegionType.Area && r.CountryId == tenant.CountryId)
            .OrderBy(r => r.NameAr)
            .ToListAsync(cancellationToken);

        if (restaurant.CountryId != tenant.CountryId)
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(Error.Forbidden("catalog.restaurant_tenant_mismatch"));

        var configuredRegions = await _db.RestaurantServiceRegions
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId.Value)
            .ToListAsync(cancellationToken);

        /* Registration selections are materialized during approval, never during a GET.
        if (configuredRegions.Count == 0)
        {
            var registration = await _db.RestaurantRegistrations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.ResultingRestaurantId == restaurantId.Value || r.SubmittedByUserId == _currentUser.UserId, cancellationToken);

            var autoActiveRegionIds = new HashSet<Guid>();
            if (restaurant.PrimaryRegionId.HasValue)
            {
                autoActiveRegionIds.Add(restaurant.PrimaryRegionId.Value);
            }
            if (registration?.RegionId.HasValue == true)
            {
                autoActiveRegionIds.Add(registration.RegionId.Value);
            }
            if (!string.IsNullOrWhiteSpace(registration?.ServiceRegionIdsJson))
            {
                try
                {
                    var rawServiceRegions = JsonSerializer.Deserialize<List<string>>(registration.ServiceRegionIdsJson) ?? [];
                    var allDbRegions = await _db.Regions.AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);
                    foreach (var item in rawServiceRegions)
                    {
                        if (string.IsNullOrWhiteSpace(item)) continue;
                        var trimmed = item.Trim();
                        var match = allDbRegions.FirstOrDefault(r =>
                            r.Id.ToString().Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                            r.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                            r.NameEn.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                            r.NameAr.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                            r.Code.EndsWith($"-{trimmed}", StringComparison.OrdinalIgnoreCase) ||
                            r.Code.Replace("KW-", "").Equals(trimmed, StringComparison.OrdinalIgnoreCase));
                        if (match != null) autoActiveRegionIds.Add(match.Id);
                    }
                }
                catch { }
            }

            if (autoActiveRegionIds.Count > 0)
            {
                foreach (var regId in autoActiveRegionIds)
                {
                    if (systemRegions.Any(s => s.Id == regId))
                    {
                        var newConfig = new RestaurantServiceRegion
                        {
                            RestaurantId = restaurantId.Value,
                            RegionId = regId,
                            IsActive = true,
                            DeliveryWindowStart = new TimeOnly(8, 0),
                            DeliveryWindowEnd = new TimeOnly(14, 0),
                            CreatedAtUtc = _clock.UtcNow
                        };
                        _db.RestaurantServiceRegions.Add(newConfig);
                        configuredRegions.Add(newConfig);
                    }
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
        } */

        var result = new List<RestaurantServiceRegionItemDto>();
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        var confirmedCountsByRegion = await (
                from day in _db.SubscriptionDays.AsNoTracking()
                join subscription in _db.Subscriptions.AsNoTracking()
                    on day.SubscriptionId equals subscription.Id
                where day.RestaurantId == restaurantId.Value &&
                      day.ServiceDate >= today &&
                      day.Status != SubscriptionDayStatus.Cancelled &&
                      subscription.RegionId.HasValue
                group day by subscription.RegionId!.Value
                into groupedDays
                select new
                {
                    RegionId = groupedDays.Key,
                    Count = groupedDays.Count()
                })
            .ToDictionaryAsync(x => x.RegionId, x => x.Count, cancellationToken);

        foreach (var sysRegion in systemRegions)
        {
            var config = configuredRegions.FirstOrDefault(x => x.RegionId == sysRegion.Id);
            var isActive = config?.IsActive ?? false;
            var start = config?.DeliveryWindowStart ?? new TimeOnly(8, 0);
            var end = config?.DeliveryWindowEnd ?? new TimeOnly(14, 0);
            var leadTime = config?.PreparationLeadTimeMinutes;
            var updatedAt = config?.ModifiedAtUtc?.ToString("O") ?? config?.CreatedAtUtc.ToString("O");

            var confirmedCount = confirmedCountsByRegion.GetValueOrDefault(sysRegion.Id);

            result.Add(new RestaurantServiceRegionItemDto(
                RegionId: sysRegion.Id,
                Code: sysRegion.Code,
                NameAr: sysRegion.NameAr,
                NameEn: sysRegion.NameEn,
                CountryId: sysRegion.CountryId,
                IsActive: isActive,
                DeliveryWindowStart: start,
                DeliveryWindowEnd: end,
                PreparationLeadTimeMinutes: leadTime,
                ConfirmedMealsCount: confirmedCount,
                UpdatedAtIso: updatedAt,
                ParentRegionId: sysRegion.ParentRegionId,
                ParentNameAr: sysRegion.ParentRegion?.NameAr,
                ParentNameEn: sysRegion.ParentRegion?.NameEn));
        }

        return Result.Success(result);
    }

    public async Task<Result<RestaurantServiceRegionItemDto>> GetServiceRegionDetailAsync(Guid regionId, CancellationToken cancellationToken)
    {
        var listResult = await GetServiceRegionsAsync(cancellationToken);
        if (listResult.IsFailure)
            return Result.Failure<RestaurantServiceRegionItemDto>(listResult.Error);

        var item = listResult.Value.FirstOrDefault(x => x.RegionId == regionId);
        if (item == null)
            return Result.Failure<RestaurantServiceRegionItemDto>(Error.NotFound("service_region.not_found", "Service region not found."));

        return Result.Success(item);
    }

    public async Task<Result<RestaurantServiceRegionItemDto>> UpdateServiceRegionAsync(
        UpdateRestaurantServiceRegionCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<RestaurantServiceRegionItemDto>(Error.Unauthorized("restaurant.id_required", "Restaurant identity is required."));

        var tenantResult = await _tenantResolver.ResolveAsync(cancellationToken);
        if (tenantResult.IsFailure)
            return Result.Failure<RestaurantServiceRegionItemDto>(tenantResult.Error);

        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == restaurantId.Value, cancellationToken);
        if (restaurant is null)
            return Result.Failure<RestaurantServiceRegionItemDto>(Error.NotFound("restaurant.not_found"));
        if (restaurant.CountryId != tenantResult.Value.CountryId)
            return Result.Failure<RestaurantServiceRegionItemDto>(Error.Forbidden("catalog.restaurant_tenant_mismatch"));

        var sysRegion = await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.RegionId && x.CountryId == tenantResult.Value.CountryId && x.IsActive && x.Type == Domain.Enums.RegionType.Area, cancellationToken);
        if (sysRegion == null)
            return Result.Failure<RestaurantServiceRegionItemDto>(Error.NotFound(MessageCodes.RestaurantOperations.RegionNotFound));

        if (request.DeliveryWindowStart >= request.DeliveryWindowEnd)
        {
            return Result.Failure<RestaurantServiceRegionItemDto>(Error.Validation(MessageCodes.RestaurantOperations.InvalidDeliveryWindow));
        }

        var config = await _db.RestaurantServiceRegions
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId.Value && x.RegionId == request.RegionId, cancellationToken);

        if (config == null)
        {
            config = new RestaurantServiceRegion
            {
                RestaurantId = restaurantId.Value,
                RegionId = request.RegionId,
                IsActive = request.IsActive,
                DeliveryWindowStart = request.DeliveryWindowStart,
                DeliveryWindowEnd = request.DeliveryWindowEnd,
                PreparationLeadTimeMinutes = request.PreparationLeadTimeMinutes,
                CreatedAtUtc = _clock.UtcNow
            };
            _db.RestaurantServiceRegions.Add(config);
        }
        else
        {
            config.IsActive = request.IsActive;
            config.DeliveryWindowStart = request.DeliveryWindowStart;
            config.DeliveryWindowEnd = request.DeliveryWindowEnd;
            config.PreparationLeadTimeMinutes = request.PreparationLeadTimeMinutes;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await GetServiceRegionDetailAsync(request.RegionId, cancellationToken);
    }

    public async Task<Result<List<RestaurantServiceRegionItemDto>>> UpdateServiceRegionSelectionAsync(
        IReadOnlyCollection<Guid> regionIds,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(Error.Unauthorized("restaurant.id_required"));

        var tenantResult = await _tenantResolver.ResolveAsync(cancellationToken);
        if (tenantResult.IsFailure)
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(tenantResult.Error);

        var restaurantCountryId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(x => x.Id == restaurantId.Value)
            .Select(x => (Guid?)x.CountryId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!restaurantCountryId.HasValue)
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(Error.NotFound("restaurant.not_found"));
        if (restaurantCountryId.Value != tenantResult.Value.CountryId)
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(Error.Forbidden("catalog.restaurant_tenant_mismatch"));

        var selectedIds = regionIds.Distinct().ToHashSet();
        var validSelectedIds = await _db.Regions
            .AsNoTracking()
            .Where(region => selectedIds.Contains(region.Id) &&
                             region.CountryId == tenantResult.Value.CountryId &&
                             region.IsActive &&
                             region.Type == Domain.Enums.RegionType.Area)
            .Select(region => region.Id)
            .ToListAsync(cancellationToken);

        if (validSelectedIds.Count != selectedIds.Count)
            return Result.Failure<List<RestaurantServiceRegionItemDto>>(
                Error.Validation(MessageCodes.RestaurantOperations.RegionNotFound));

        var validSelectedSet = validSelectedIds.ToHashSet();
        var configurations = await _db.RestaurantServiceRegions
            .Where(item => item.RestaurantId == restaurantId.Value)
            .ToListAsync(cancellationToken);

        foreach (var configuration in configurations)
        {
            configuration.IsActive = validSelectedSet.Contains(configuration.RegionId);
        }

        var configuredIds = configurations.Select(item => item.RegionId).ToHashSet();
        foreach (var regionId in validSelectedSet.Where(id => !configuredIds.Contains(id)))
        {
            _db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
            {
                RestaurantId = restaurantId.Value,
                RegionId = regionId,
                IsActive = true,
                DeliveryWindowStart = new TimeOnly(8, 0),
                DeliveryWindowEnd = new TimeOnly(14, 0),
                CreatedAtUtc = _clock.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetServiceRegionsAsync(cancellationToken);
    }

}
