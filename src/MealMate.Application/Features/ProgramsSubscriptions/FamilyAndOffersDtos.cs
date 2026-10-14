using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Family Subscription DTOs & Commands
public sealed record FamilyMemberDto(
    Guid Id,
    Guid PrimarySubscriptionId,
    Guid MemberCustomerUserId,
    string? DisplayName,
    string? Relationship,
    int MaxMealsAllowedPerDay,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? CustomerUserId = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    string? SubscriptionType = null);

public sealed record AddFamilyMemberCommand(
    Guid PrimarySubscriptionId,
    Guid MemberCustomerUserId,
    string? DisplayName = null,
    string? Relationship = null,
    int MaxMealsAllowedPerDay = 1) : ICommand<FamilyMemberDto>;

public sealed record RemoveFamilyMemberCommand(Guid MemberId) : ICommand<bool>;

public sealed record GetFamilyMembersQuery(Guid PrimarySubscriptionId) : IQuery<IReadOnlyList<FamilyMemberDto>>;

// Promo & Referral DTOs & Commands
public sealed record PromoCodeDto(
    Guid Id,
    string Code,
    decimal? DiscountPercent,
    decimal? DiscountAmount,
    int? MaxUses,
    int UsedCount,
    DateTime StartsAtUtc,
    DateTime? EndsAtUtc,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? CustomerUserId = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    string? SubscriptionType = null,
    int? PerCustomerLimit = null);

public sealed record DiscountValidationResultDto(
    bool IsValid,
    string Code,
    string CodeType, // PromoCode | ReferralCode
    decimal CalculatedDiscountKwd,
    string? FailureReason,
    decimal? DiscountPercent = null,
    string? EligibilityCode = null);

public sealed record CreatePromoCodeCommand(
    string Code,
    decimal? DiscountPercent = null,
    decimal? DiscountAmountKwd = null,
    int? MaxUses = null,
    DateTime? StartsAtUtc = null,
    DateTime? EndsAtUtc = null,
    Guid? CustomerUserId = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    string? SubscriptionType = null,
    int? PerCustomerLimit = null) : ICommand<PromoCodeDto>;

public sealed record ValidateDiscountCodeQuery(
    string Code,
    Guid? SubscriptionId = null,
    decimal TotalAmountKwd = 0m,
    Guid? CustomerUserId = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    string? SubscriptionType = null) : IQuery<DiscountValidationResultDto>;

public sealed record GetPromoCodesListQuery(
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<PromoCodeDto>>;

public sealed record SetPromoCodeActiveCommand(Guid PromoCodeId, bool IsActive, string Reason) : ICommand<PromoCodeDto>;
