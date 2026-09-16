using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CollaborativeCampaigns.DTOs;

public sealed record CampaignDraftDto(
    Guid Id,
    string NameAr,
    string NameEn,
    CampaignStatus Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal TotalDiscountRate,
    decimal MealMateShareRate,
    decimal RestaurantShareRate,
    CampaignFundingMode FundingMode,
    int? MaxSubscribers,
    DateTime? EnrollmentOpensAtUtc,
    DateTime? EnrollmentClosesAtUtc,
    IReadOnlyList<Guid> ProgramIds,
    IReadOnlyList<Guid> BundleIds);
