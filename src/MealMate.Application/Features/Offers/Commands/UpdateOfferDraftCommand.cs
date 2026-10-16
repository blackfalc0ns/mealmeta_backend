using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Commands;

public sealed record UpdateOfferDraftCommand(
    Guid OfferId,
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
    string? TermsAndConditionsEn = null
    ,List<OfferPackageTargetInput>? PackageTargets = null
) : ICommand<OfferDto>;

public sealed class UpdateOfferDraftCommandValidator : AbstractValidator<UpdateOfferDraftCommand>
{
    public UpdateOfferDraftCommandValidator()
    {
        RuleFor(x => x.OfferId).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StartsAtUtc).LessThan(x => x.EndsAtUtc);
        RuleFor(x => x.PerCustomerLimit).GreaterThan(0);
    }
}

public sealed class UpdateOfferDraftCommandHandler : ICommandHandler<UpdateOfferDraftCommand, OfferDto>
{
    private readonly IAppDbContext _context;

    public UpdateOfferDraftCommandHandler(IAppDbContext context) => _context = context;

    public async Task<Result<OfferDto>> Handle(UpdateOfferDraftCommand request, CancellationToken cancellationToken)
    {
        var offer = await _context.Offers
            .Include(o => o.PackageTargets)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId, cancellationToken);

        if (offer == null)
        {
            return Error.NotFound("Offer.NotFound", $"Offer with ID '{request.OfferId}' was not found.");
        }

        if (offer.Status != OfferStatus.Draft)
        {
            return Error.Validation("Offer.NotDraft", $"Only offers in 'Draft' status can be modified (current status: '{offer.Status}').");
        }

        var targetValidation = await OfferPackageTargetCommandValidation.ValidateAsync(_context, request.PackageTargets, cancellationToken);
        if (targetValidation is not null) return targetValidation;

        offer.NameAr = request.NameAr.Trim();
        offer.NameEn = request.NameEn.Trim();
        offer.DescriptionAr = request.DescriptionAr?.Trim();
        offer.DescriptionEn = request.DescriptionEn?.Trim();
        offer.BannerImageUrl = request.BannerImageUrl?.Trim();
        offer.Type = request.Type;
        offer.DiscountType = request.DiscountType;
        offer.FundingMode = request.FundingMode;
        offer.DiscountRate = request.DiscountRate.HasValue ? Percentage.FromFraction(request.DiscountRate.Value) : null;
        offer.DiscountAmount = request.DiscountAmount.HasValue ? new Money(request.DiscountAmount.Value, "KWD") : null;
        offer.PlatformShareRate = request.PlatformShareRate.HasValue ? Percentage.FromFraction(request.PlatformShareRate.Value) : null;
        offer.RestaurantShareRate = request.RestaurantShareRate.HasValue ? Percentage.FromFraction(request.RestaurantShareRate.Value) : null;
        offer.StartsAtUtc = request.StartsAtUtc;
        offer.EndsAtUtc = request.EndsAtUtc;
        offer.MinOrderAmount = request.MinOrderAmount.HasValue ? new Money(request.MinOrderAmount.Value, "KWD") : null;
        offer.MinSubscriptionDays = request.MinSubscriptionDays;
        offer.MaxSubscriptionDays = request.MaxSubscriptionDays;
        offer.TotalUsageLimit = request.TotalUsageLimit;
        offer.PerCustomerLimit = request.PerCustomerLimit;
        offer.CustomerSegment = request.CustomerSegment;
        offer.AllowedGovernoratesJson = request.AllowedGovernoratesJson;
        offer.CampaignId = request.CampaignId;
        offer.AllowPromoCodeStacking = request.AllowPromoCodeStacking;
        offer.PromoCode = request.PromoCode;
        offer.TermsAndConditionsAr = request.TermsAndConditionsAr;
        offer.TermsAndConditionsEn = request.TermsAndConditionsEn;

        if (request.PackageTargets is not null)
        {
            _context.OfferPackageTargets.RemoveRange(offer.PackageTargets);
            offer.PackageTargets = request.PackageTargets.Select(target => new OfferPackageTarget
            {
                OfferId = offer.Id,
                SubscriptionType = target.SubscriptionType.Trim(),
                ProgramId = target.ProgramId,
                BundleId = target.BundleId,
                Tier = target.Tier,
                AllowedDurationDays = target.AllowedDurationDays.Distinct().Order().ToArray()
            }).ToList();
        }

        await _context.SaveChangesAsync(cancellationToken);

        return CreateOfferDraftCommandHandler.MapOfferDto(offer);
    }
}
