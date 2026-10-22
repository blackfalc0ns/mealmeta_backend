using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.AdminCapacity;

public sealed record GetAdminCapacityOverviewQuery(
    DateOnly? ServiceDate = null) : IQuery<AdminCapacityOverviewResponseDto>;

public sealed record UpdatePlatformCapacityPolicyCommand(
    UpdatePlatformPolicyRequest Request) : ICommand<AdminPlatformCapacityPolicyDto>;

public sealed record UpdateRestaurantCapacityLimitCommand(
    Guid RestaurantId,
    int NewCapacityLimit,
    string Reason) : ICommand<AdminRestaurantCapacityRowDto>;

public sealed record ToggleRestaurantCapacityBusyCommand(
    Guid RestaurantId,
    bool IsBusy,
    string Reason,
    int? OverrideHours = null) : ICommand<AdminRestaurantCapacityRowDto>;

public sealed record ApproveAdminCapacityReductionCommand(
    Guid RequestId,
    string? Notes = null) : ICommand<AdminCapacityReductionDto>;

public sealed record RejectAdminCapacityReductionCommand(
    Guid RequestId,
    string? Notes = null) : ICommand<AdminCapacityReductionDto>;
