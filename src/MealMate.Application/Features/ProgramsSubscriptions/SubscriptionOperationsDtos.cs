using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Freeze DTOs & Commands
public sealed record SubscriptionFreezeRequestDto(
    Guid Id,
    Guid SubscriptionId,
    DateOnly FreezeStartDate,
    DateOnly FreezeEndDate,
    int FreezeDays,
    string? Reason,
    VerificationStatus Status,
    Guid? ApprovedByUserId,
    DateTime? ProcessedAtUtc,
    DateTime CreatedAtUtc);

public sealed record RequestSubscriptionFreezeCommand(
    Guid SubscriptionId,
    DateOnly FreezeStartDate,
    DateOnly FreezeEndDate,
    string? Reason = null,
    bool AcknowledgeLostSelections = false,
    Guid? CustomerActorUserId = null) : ICommand<SubscriptionFreezeRequestDto>;

public sealed record ApproveSubscriptionFreezeCommand(
    Guid FreezeRequestId,
    bool Approved = true) : ICommand<SubscriptionFreezeRequestDto>;

// Upgrade DTOs & Commands
public sealed record SubscriptionUpgradeDto(
    Guid Id,
    Guid SubscriptionId,
    RestaurantTier FromTier,
    RestaurantTier ToTier,
    decimal PriceDifferenceKwd,
    VerificationStatus Status,
    DateTime? EffectiveAtUtc,
    DateTime CreatedAtUtc);

public sealed record RequestSubscriptionUpgradeCommand(
    Guid SubscriptionId,
    RestaurantTier TargetTier,
    string? Reason = null) : ICommand<SubscriptionUpgradeDto>;

public sealed record ApproveSubscriptionUpgradeCommand(
    Guid UpgradeId,
    bool Approved = true) : ICommand<SubscriptionUpgradeDto>;

// Worker / Batch Processing DTOs & Commands
public sealed record SubscriptionBatchProcessingResultDto(
    int TotalProcessed,
    int RenewedCount,
    int ExpiredCount,
    IReadOnlyList<string> Details);

public sealed record ProcessSubscriptionRenewalsAndExpirationsCommand : ICommand<SubscriptionBatchProcessingResultDto>;

// Admin Lifecycle Query DTOs
public sealed record AdminSubscriptionFreezeRequestRowDto(
    Guid Id,
    Guid SubscriptionId,
    Guid CustomerUserId,
    string CustomerName,
    DateOnly FreezeStartDate,
    DateOnly FreezeEndDate,
    int FreezeDays,
    string? Reason,
    VerificationStatus Status,
    Guid? ApprovedByUserId,
    DateTime? ProcessedAtUtc,
    DateTime CreatedAtUtc);

public sealed record GetSubscriptionFreezeRequestsQuery(
    VerificationStatus? Status = null) : IQuery<IReadOnlyList<AdminSubscriptionFreezeRequestRowDto>>;

public sealed record AdminSubscriptionUpgradeRowDto(
    Guid Id,
    Guid SubscriptionId,
    Guid CustomerUserId,
    string CustomerName,
    RestaurantTier FromTier,
    RestaurantTier ToTier,
    decimal PriceDifferenceKwd,
    VerificationStatus Status,
    DateTime? EffectiveAtUtc,
    DateTime CreatedAtUtc);

public sealed record GetSubscriptionUpgradeRequestsQuery(
    VerificationStatus? Status = null) : IQuery<IReadOnlyList<AdminSubscriptionUpgradeRowDto>>;

public sealed record AdminSubscriptionCancellationRequestDto(
    Guid Id,
    Guid SubscriptionId,
    Guid CustomerUserId,
    string CustomerName,
    string? Reason,
    DateTime RequestedAtUtc,
    string Status,
    bool IsFamilySettlement = false,
    Guid? FamilyGroupId = null,
    Guid? MemberId = null,
    Guid? BatchId = null,
    Guid? BatchLineId = null,
    Guid? ActorUserId = null,
    Guid? PayerCustomerUserId = null,
    decimal? FinalCashRefund = null,
    string? CurrencyCode = null,
    Guid? RefundCalculationId = null);

public sealed record GetSubscriptionCancellationRequestsQuery(int Page = 1, int PageSize = 50)
    : IQuery<PagedResult<AdminSubscriptionCancellationRequestDto>>;
