using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Application.Features.Offers.Commands;

public sealed record CreateOfferDraftCommand(
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string? BannerImageUrl = null,
    OfferType Type = OfferType.StandardDiscount,
    DiscountType DiscountType = DiscountType.Percentage,
    OfferFundingMode FundingMode = OfferFundingMode.PlatformFull,
    decimal? DiscountRate = null,
    decimal? DiscountAmount = null,
    decimal? PlatformShareRate = null,
    decimal? RestaurantShareRate = null,
    DateTime StartsAtUtc = default,
    DateTime EndsAtUtc = default,
    decimal? MinOrderAmount = null,
    int? MinSubscriptionDays = null,
    int? MaxSubscriptionDays = null,
    int? TotalUsageLimit = null,
    int PerCustomerLimit = 1,
    CustomerSegmentFilter CustomerSegment = CustomerSegmentFilter.All,
    string? AllowedGovernoratesJson = null,
    Guid? CampaignId = null,
    bool AllowPromoCodeStacking = false,
    string? PromoCode = null,
    string? TermsAndConditionsAr = null,
    string? TermsAndConditionsEn = null,
    List<Guid>? ProgramIds = null,
    List<Guid>? BundleIds = null,
    List<Guid>? RestaurantIds = null
) : ICommand<OfferDto>;

public sealed class CreateOfferDraftCommandValidator : AbstractValidator<CreateOfferDraftCommand>
{
    public CreateOfferDraftCommandValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StartsAtUtc).LessThan(x => x.EndsAtUtc);
        RuleFor(x => x.PerCustomerLimit).GreaterThan(0);
    }
}

public sealed class CreateOfferDraftCommandHandler : ICommandHandler<CreateOfferDraftCommand, OfferDto>
{
    private readonly IAppDbContext _context;

    public CreateOfferDraftCommandHandler(IAppDbContext context) => _context = context;

    public async Task<Result<OfferDto>> Handle(CreateOfferDraftCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
        {
            return Error.Validation("Offer.NameRequired", "Offer NameAr and NameEn are required.");
        }

        if (request.StartsAtUtc >= request.EndsAtUtc)
        {
            return Error.Validation("Offer.InvalidDates", "StartsAtUtc must be before EndsAtUtc.");
        }

        var offer = new Offer
        {
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DescriptionAr = request.DescriptionAr?.Trim(),
            DescriptionEn = request.DescriptionEn?.Trim(),
            BannerImageUrl = request.BannerImageUrl?.Trim(),
            Status = OfferStatus.Draft,
            Type = request.Type,
            DiscountType = request.DiscountType,
            FundingMode = request.FundingMode,
            DiscountRate = request.DiscountRate.HasValue ? Percentage.FromFraction(request.DiscountRate.Value) : null,
            DiscountAmount = request.DiscountAmount.HasValue ? new Money(request.DiscountAmount.Value, "KWD") : null,
            PlatformShareRate = request.PlatformShareRate.HasValue ? Percentage.FromFraction(request.PlatformShareRate.Value) : null,
            RestaurantShareRate = request.RestaurantShareRate.HasValue ? Percentage.FromFraction(request.RestaurantShareRate.Value) : null,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
            MinOrderAmount = request.MinOrderAmount.HasValue ? new Money(request.MinOrderAmount.Value, "KWD") : null,
            MinSubscriptionDays = request.MinSubscriptionDays,
            MaxSubscriptionDays = request.MaxSubscriptionDays,
            TotalUsageLimit = request.TotalUsageLimit,
            PerCustomerLimit = request.PerCustomerLimit,
            CustomerSegment = request.CustomerSegment,
            AllowedGovernoratesJson = request.AllowedGovernoratesJson,
            AllowPromoCodeStacking = request.AllowPromoCodeStacking,
            CampaignId = request.CampaignId,
            PromoCode = request.PromoCode,
            TermsAndConditionsAr = request.TermsAndConditionsAr,
            TermsAndConditionsEn = request.TermsAndConditionsEn
        };

        if (request.ProgramIds != null)
        {
            foreach (var pId in request.ProgramIds.Distinct())
            {
                offer.EligiblePrograms.Add(new OfferProgram { Offer = offer, ProgramId = pId });
            }
        }

        if (request.BundleIds != null)
        {
            foreach (var bId in request.BundleIds.Distinct())
            {
                offer.EligibleBundles.Add(new OfferBundle { Offer = offer, BundleId = bId });
            }
        }

        if (request.RestaurantIds != null)
        {
            foreach (var rId in request.RestaurantIds.Distinct())
            {
                offer.EligibleRestaurants.Add(new OfferRestaurant { Offer = offer, RestaurantId = rId });
            }
        }

        _context.Offers.Add(offer);
        await _context.SaveChangesAsync(cancellationToken);

        return MapOfferDto(offer);
    }

    public static OfferDto MapOfferDto(Offer o) => new(
        o.Id,
        o.NameAr,
        o.NameEn,
        o.DescriptionAr,
        o.DescriptionEn,
        o.BannerImageUrl,
        o.Status,
        o.Type,
        o.DiscountType,
        o.FundingMode,
        o.DiscountRate?.Value,
        o.DiscountAmount?.Amount,
        o.PlatformShareRate?.Value,
        o.RestaurantShareRate?.Value,
        o.StartsAtUtc,
        o.EndsAtUtc,
        o.MinOrderAmount?.Amount,
        o.MinSubscriptionDays,
        o.MaxSubscriptionDays,
        o.TotalUsageLimit,
        o.CurrentUsageCount,
        o.PerCustomerLimit,
        o.CustomerSegment,
        o.CampaignId,
        o.AllowPromoCodeStacking,
        o.TotalUsageLimit.HasValue ? Math.Max(0, o.TotalUsageLimit.Value - o.CurrentUsageCount) : null,
        o.PromoCode,
        o.TermsAndConditionsAr,
        o.TermsAndConditionsEn,
        o.EligiblePrograms.Select(p => p.ProgramId).Distinct().ToArray(),
        o.EligibleBundles.Select(b => b.BundleId).Distinct().ToArray(),
        o.EligibleRestaurants.Select(r => r.RestaurantId).Distinct().ToArray(),
        o.CampaignId.HasValue ? "Campaign" : "Offer"
    );
}
