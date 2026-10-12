using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record AdminFamilyMemberRowDto(
    Guid GroupId,
    Guid MemberId,
    Guid? CustomerUserId,
    Guid? MemberSubscriptionId,
    Guid? CurrentSubscriptionId,
    Guid? UpcomingSubscriptionId,
    Guid? BatchId,
    string DisplayName,
    FamilyMemberRole Role,
    FamilyMemberSetupStatus SetupStatus,
    string CheckoutSelectionStatus,
    string? PaymentStatus,
    string FulfillmentStatus,
    string ServiceStatus,
    string AccountStatus,
    string? ReadinessBlocker,
    string? ProgramNameAr,
    string? ProgramNameEn,
    string? BundleNameAr,
    string? BundleNameEn,
    int? DurationDays,
    int UsedDays,
    int ActiveOrders);

public sealed record AdminFamilyGroupRowDto(
    Guid Id,
    Guid? BatchId,
    string Name,
    Guid ManagerCustomerUserId,
    string ManagerName,
    Guid? AnchorSubscriptionId,
    string ProgramNameAr,
    string ProgramNameEn,
    string BundleNameAr,
    string BundleNameEn,
    string TierNameAr,
    string TierNameEn,
    int MemberCount,
    int SubscriptionDays,
    int UsedDays,
    int ActiveOrders,
    FamilyGroupStatus Status,
    IReadOnlyList<AdminFamilyMemberRowDto> Members);

/// <summary>
/// Admin KPI-friendly stats. <see cref="FamilyGroups"/> maps to Active groups;
/// <see cref="FamilyDisputes"/> / <see cref="FamilyPendingDetach"/> mirror Dispute / PendingDetach counts.
/// </summary>
public sealed record AdminFamilyGroupStatsDto(
    int FamilyGroups,
    int FamilyMembers,
    int FamilyDisputes,
    int FamilyPendingDetach,
    int Draft,
    int CheckoutPending,
    int Active,
    int Frozen,
    int Dispute,
    int PendingDetach,
    int Cancelled,
    int ActiveServiceMembers = 0);

public sealed record GetAdminFamilyGroupsListQuery(
    FamilyGroupStatus? Status = null,
    string? Q = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AdminFamilyGroupRowDto>>;

public sealed record GetAdminFamilyGroupByIdQuery(Guid Id) : IQuery<AdminFamilyGroupRowDto>;

public sealed record GetAdminFamilyGroupStatsQuery() : IQuery<AdminFamilyGroupStatsDto>;

public sealed record AdminFamilyGroupHistoryRowDto(
    Guid BatchId,
    Guid BatchLineId,
    Guid GroupId,
    Guid MemberId,
    Guid? SubscriptionId,
    Guid PayerCustomerUserId,
    bool CheckoutSelected,
    string BatchStatus,
    string EvidenceStatus,
    string Purpose,
    decimal? NetPaidAmount,
    string CurrencyCode,
    long Version,
    DateTime BatchCreatedAtUtc,
    DateTime LineCreatedAtUtc,
    IReadOnlyList<AdminFamilyPaymentAttemptDto> Attempts,
    IReadOnlyList<AdminFamilyReceiptDto> Receipts,
    IReadOnlyList<AdminFamilyUpgradeDto> Upgrades,
    int AttemptCount,
    int ReceiptCount,
    int UpgradeCount,
    bool ChildrenTruncated);

public sealed record AdminFamilyPaymentAttemptDto(
    Guid AttemptId, string AttemptKey, string? GatewayPaymentId, string? GatewayInvoiceId,
    DateTime RequestedAtUtc, DateTime? VerifiedAtUtc, long Version, string? ResultCode);

public sealed record AdminFamilyReceiptDto(
    Guid ReceiptId, string? PaymentId, string? InvoiceId, decimal? VerifiedAmount,
    string? CurrencyCode, string Status, DateTime? VerifiedAtUtc, DateTime ProcessedAtUtc);

public sealed record AdminFamilyUpgradeDto(
    Guid UpgradeId, Guid ReceiptId, Guid? ActualPayerCustomerUserId, string? PaymentId, string? InvoiceId,
    decimal? VerifiedPaidAmount, string? CurrencyCode, string Status, DateTime? VerifiedPaidAtUtc,
    bool IsApplied);

public sealed record GetAdminFamilyGroupHistoryQuery(Guid GroupId, int Page = 1, int PageSize = 20,
    int EvidencePage = 1, int EvidencePageSize = 10)
    : IQuery<PagedResult<AdminFamilyGroupHistoryRowDto>>;

public sealed record AdminFamilyCancellationRowDto(Guid CancellationId, Guid SubscriptionId, Guid? MemberId,
    Guid? BatchId, Guid? BatchLineId, Guid? ActorUserId, Guid? PayerCustomerUserId, string Status,
    DateTime RequestedAtUtc, DateTime? CompletedAtUtc, int CountedDays, int UnusedDays,
    decimal? FinalCashRefund, string? CurrencyCode, Guid? RefundCalculationId);

public sealed record GetAdminFamilyCancellationHistoryQuery(Guid GroupId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<AdminFamilyCancellationRowDto>>;
