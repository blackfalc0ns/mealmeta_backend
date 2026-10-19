using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record SubscriptionSnapshotDto(
    Guid Id,
    Guid SubscriptionId,
    decimal A_t,
    decimal ROfD,
    string SnapshotJson,
    DateTime CapturedAtUtc);

public sealed record SubscriptionOperationDto(
    Guid Id,
    Guid SubscriptionId,
    string OperationType,
    SubscriptionStatus? PreviousStatus,
    SubscriptionStatus NewStatus,
    string? Reason,
    Guid PerformedByUserId,
    UserType PerformedByUserType,
    string? CorrelationId,
    DateTime TimestampUtc);

public sealed record SubscriptionDto(
    Guid Id,
    Guid CustomerUserId,
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    RestaurantTier SelectedTier,
    int DurationDays,
    SubscriptionStatus Status,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal PaidAmount,
    string CurrencyCode,
    string? DeliveryAddress,
    string? CorrelationId,
    DateTime CreatedAtUtc,
    IReadOnlyList<SubscriptionSnapshotDto> Snapshots,
    IReadOnlyList<SubscriptionOperationDto> Operations,
    bool IncludeFriday = false,
    string? CustomerFullName = null,
    string? CustomerEmail = null,
    string? CustomerPhone = null,
    Guid? RegionId = null,
    string? RegionNameAr = null,
    string? RegionNameEn = null,
    int UsedDays = 0,
    int RemainingDays = 0,
    int TotalServiceDays = 0,
    bool IsFamily = false,
    int FamilyMemberCount = 0);

// Commands
public sealed record CreateSubscriptionCommand(
    Guid CustomerUserId,
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier SelectedTier,
    int DurationDays,
    DateOnly StartDate,
    string? DeliveryAddress = null,
    Guid? CountryId = null,
    Guid? RegionId = null,
    Guid? PromoCodeId = null,
    Guid? CampaignId = null,
    string? CorrelationId = null,
    bool IncludeFriday = false,
    Guid? CouponOfferId = null,
    decimal CouponDiscountKwd = 0m,
    decimal WalletAppliedKwd = 0m,
    DateOnly? CheckoutAnchorDate = null) : ICommand<SubscriptionDto>;

public sealed record ConfirmSubscriptionPaymentCommand(
    Guid SubscriptionId,
    string PaymentReference,
    string? CorrelationId = null) : ICommand<SubscriptionDto>;

// Queries
public sealed record GetSubscriptionByIdQuery(Guid Id) : IQuery<SubscriptionDto>;

public sealed record GetSubscriptionsListQuery(
    Guid? CustomerUserId = null,
    SubscriptionStatus? Status = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    int Page = 1,
    int PageSize = 20,
    bool? IsFamily = null) : IQuery<PagedResult<SubscriptionDto>>;

public sealed record SubscriptionListStatsDto(
    int IndividualActive,
    int IndividualFrozen,
    int IndividualExpiring,
    int IndividualCancelled,
    int FamilyGroups,
    int FamilyMembers);

public sealed record GetSubscriptionListStatsQuery() : IQuery<SubscriptionListStatsDto>;
