using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record AdminFamilyMemberRowDto(
    Guid MemberId,
    Guid? MemberSubscriptionId,
    string DisplayName,
    FamilyMemberRole Role,
    FamilyMemberSetupStatus SetupStatus,
    string? ProgramNameAr,
    string? ProgramNameEn,
    string? BundleNameAr,
    string? BundleNameEn,
    int? DurationDays,
    int UsedDays,
    int ActiveOrders);

public sealed record AdminFamilyGroupRowDto(
    Guid Id,
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
    int Cancelled);

public sealed record GetAdminFamilyGroupsListQuery(
    FamilyGroupStatus? Status = null,
    string? Q = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AdminFamilyGroupRowDto>>;

public sealed record GetAdminFamilyGroupByIdQuery(Guid Id) : IQuery<AdminFamilyGroupRowDto>;

public sealed record GetAdminFamilyGroupStatsQuery() : IQuery<AdminFamilyGroupStatsDto>;
