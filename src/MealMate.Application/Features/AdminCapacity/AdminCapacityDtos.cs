namespace MealMate.Application.Features.AdminCapacity;

public sealed record AdminPlatformCapacityPolicyDto(
    int DailyCapTotal,
    decimal RenewalReservePercentage,
    int RenewalWindowDays,
    int CutoffLeadHours,
    string UpdatedAt);

public sealed record AdminRestaurantCapacityReductionInfo(
    int RequestedLimit,
    string EffectiveSafeDate,
    string RequestedAt,
    string Reason);

public sealed record AdminRestaurantCapacityRowDto(
    string RestaurantId,
    string RestaurantName,
    string RestaurantNameEn,
    string AreaName,
    string ServiceWindow,
    string OperatingDay,
    int CapacityLimit,
    int SubscriptionBooked,
    int RenewalReserved,
    int FrozenCompensated,
    int SafetyReserve,
    int BookedMeals,
    int ConfirmedMeals,
    int Pending72h,
    int BlockedMeals,
    int AvailableNewSales,
    string Status,
    string AutomationReason,
    string? OverrideUntil,
    string RuleVersion,
    string UpdatedAt,
    string SourceReference,
    AdminRestaurantCapacityReductionInfo? PendingReduction);

public sealed record AdminCapacityReductionDto(
    string Id,
    string RestaurantId,
    string RestaurantName,
    int CurrentLimit,
    int RequestedLimit,
    string RequestedEffectiveDate,
    string FirstSafeDate,
    string Reason,
    string Status,
    string CreatedAt);

public sealed record AdminCapacityAuditDto(
    string Id,
    string RestaurantId,
    string RestaurantName,
    string Action,
    string ActorName,
    string Reason,
    string CreatedAt);

public sealed record AdminCapacityKpisDto(
    int TotalCapacity,
    int TotalSubscriptionBooked,
    int TotalRenewalReserved,
    int TotalFrozenCompensated,
    int TotalSafetyReserve,
    int TotalAvailableNewSales,
    int TotalBooked,
    int TotalBlocked,
    int UtilizationPercent,
    int BusyCount,
    int AtRiskCount,
    int OverridesCount,
    int PendingReductionsCount,
    int PlatformDailyCap,
    int PlatformRenewalReserve);

public sealed record AdminCapacityOverviewResponseDto(
    AdminPlatformCapacityPolicyDto Policy,
    List<AdminRestaurantCapacityRowDto> Restaurants,
    List<AdminCapacityReductionDto> ReductionRequests,
    List<AdminCapacityAuditDto> AuditEvents,
    AdminCapacityKpisDto Kpis);

public sealed record UpdatePlatformPolicyRequest(
    int? DailyCapTotal,
    decimal? RenewalReservePercentage,
    int? RenewalWindowDays,
    int? CutoffLeadHours,
    string? Reason);

public sealed record UpdateRestaurantCapacityLimitRequest(
    int NewCapacityLimit,
    string Reason);

public sealed record ToggleRestaurantCapacityBusyRequest(
    bool IsBusy,
    string Reason,
    int? OverrideHours = null);

public sealed record ReviewCapacityReductionRequest(
    string? Notes = null);
