using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Services;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record CampaignPricePreviewDto(
    Guid CampaignId,
    string CampaignNameAr,
    string CampaignNameEn,
    Guid BundleId,
    int DurationDays,
    decimal ReferenceCustomerPrice,
    decimal TotalDiscountPct,
    decimal TotalDiscountAmount,
    decimal RestaurantContributionPct,
    decimal RestaurantContributionAmount,
    decimal PlatformContributionPct,
    decimal PlatformContributionAmount,
    decimal FinalCustomerPrice,
    decimal PricePerDay,
    string Currency,
    bool IsCustomerEligible,
    IReadOnlyList<string> IneligibilityReasons);

public sealed record GetCampaignPricePreviewQuery(
    Guid CampaignId,
    Guid BundleId,
    int DurationDays,
    Guid? CustomerId = null,
    string? Governorate = null) : IQuery<CampaignPricePreviewDto>;

public sealed class GetCampaignPricePreviewQueryHandler : IQueryHandler<GetCampaignPricePreviewQuery, CampaignPricePreviewDto>
{
    private readonly ICustomerCampaignEvaluationService _evaluation;

    public GetCampaignPricePreviewQueryHandler(ICustomerCampaignEvaluationService evaluation)
    {
        _evaluation = evaluation;
    }

    public async Task<Result<CampaignPricePreviewDto>> Handle(
        GetCampaignPricePreviewQuery request,
        CancellationToken cancellationToken)
    {
        var evaluation = await _evaluation.EvaluateAsync(
            new CustomerCampaignEvaluationRequest(request.CampaignId, request.BundleId, request.DurationDays, request.CustomerId),
            cancellationToken);

        if (evaluation.IsFailure)
        {
            return Result.Failure<CampaignPricePreviewDto>(evaluation.Error!);
        }

        var e = evaluation.Value;
        var durationDays = request.DurationDays;
        var finalPrice = e.Price.CampaignCustomerPrice.Amount;
        var pricePerDay = durationDays > 0
            ? Math.Round(finalPrice / durationDays, 3, MidpointRounding.AwayFromZero)
            : 0m;

        var dto = new CampaignPricePreviewDto(
            e.Campaign.Id,
            e.Campaign.NameAr,
            e.Campaign.NameEn,
            e.CampaignBundle.Id,
            durationDays,
            e.Price.ReferenceCustomerPrice.Amount,
            e.Price.HeadlineDiscount.Value,
            e.Price.CustomerDiscountAmount.Amount,
            e.Price.RestaurantContribution.Value,
            e.Price.RestaurantContributionFunded.Amount,
            e.Price.PlatformContribution.Value,
            e.Price.PlatformContributionFunded.Amount,
            finalPrice,
            pricePerDay,
            e.Price.ReferenceCustomerPrice.CurrencyCode,
            e.LegacyIneligibilityReasons.Count == 0,
            e.LegacyIneligibilityReasons);

        return Result.Success(dto);
    }
}
