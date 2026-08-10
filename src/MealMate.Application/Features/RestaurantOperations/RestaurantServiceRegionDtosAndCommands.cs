using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.RestaurantOperations;

public sealed record RestaurantServiceRegionItemDto(
    Guid RegionId,
    string Code,
    string NameAr,
    string NameEn,
    Guid CountryId,
    bool IsActive,
    TimeOnly DeliveryWindowStart,
    TimeOnly DeliveryWindowEnd,
    int? PreparationLeadTimeMinutes,
    int ConfirmedMealsCount,
    string? UpdatedAtIso,
    Guid? ParentRegionId = null,
    string? ParentNameAr = null,
    string? ParentNameEn = null);

public sealed record GetRestaurantServiceRegionsQuery()
    : IQuery<List<RestaurantServiceRegionItemDto>>;

public sealed record GetRestaurantServiceRegionDetailQuery(Guid RegionId)
    : IQuery<RestaurantServiceRegionItemDto>;

public sealed record UpdateRestaurantServiceRegionCommand(
    Guid RegionId,
    bool IsActive,
    TimeOnly DeliveryWindowStart,
    TimeOnly DeliveryWindowEnd,
    int? PreparationLeadTimeMinutes)
    : ICommand<RestaurantServiceRegionItemDto>;

public sealed record UpdateRestaurantServiceRegionSelectionCommand(
    List<Guid> RegionIds)
    : ICommand<List<RestaurantServiceRegionItemDto>>;
