using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Commands;

public sealed record RedeemOfferCommand(
    Guid OfferId,
    Guid CustomerId,
    Guid SubscriptionId,
    decimal CartAmount,
    int DurationDays,
    string? DeliveryGovernorate = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    Guid? RestaurantId = null,
    Guid? PromoCodeId = null
) : ICommand<OfferRedemptionResultDto>;

public sealed class RedeemOfferCommandHandler : ICommandHandler<RedeemOfferCommand, OfferRedemptionResultDto>
{
    private readonly IAppDbContext _context;
    private readonly IOfferEligibilityEngine _eligibilityEngine;
    private readonly IDateTimeProvider _clock;

    public RedeemOfferCommandHandler(
        IAppDbContext context,
        IOfferEligibilityEngine eligibilityEngine,
        IDateTimeProvider clock)
    {
        _context = context;
        _eligibilityEngine = eligibilityEngine;
        _clock = clock;
    }

    public async Task<Result<OfferRedemptionResultDto>> Handle(RedeemOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await _context.Offers
            .FirstOrDefaultAsync(o => o.Id == request.OfferId, cancellationToken);

        if (offer == null)
        {
            return Error.NotFound("Offer.NotFound", $"Offer with ID '{request.OfferId}' was not found.");
        }

        var priorSubsCount = await _context.Subscriptions
            .CountAsync(s => s.CustomerUserId == request.CustomerId, cancellationToken);

        var customerRedemptionCount = await _context.OfferRedemptions
            .CountAsync(r => r.OfferId == request.OfferId && r.CustomerId == request.CustomerId, cancellationToken);

        var evalCtx = new OfferEligibilityContext(
            request.CustomerId,
            priorSubsCount,
            customerRedemptionCount,
            request.CartAmount,
            request.DurationDays,
            request.DeliveryGovernorate,
            request.ProgramId,
            request.BundleId,
            request.RestaurantId);

        var now = _clock.UtcNow;
        var evalResult = _eligibilityEngine.Evaluate(offer, evalCtx, now);

        if (!evalResult.IsEligible)
        {
            return Error.Validation("Offer.NotEligible", string.Join("; ", evalResult.IneligibilityReasons));
        }

        var discount = evalResult.CalculatedDiscountAmount;

        // Split funding
        decimal platformFunded = discount;
        decimal restaurantFunded = 0m;

        if (offer.FundingMode == OfferFundingMode.RestaurantFull)
        {
            platformFunded = 0m;
            restaurantFunded = discount;
        }
        else if (offer.FundingMode == OfferFundingMode.Shared && offer.PlatformShareRate != null && offer.RestaurantShareRate != null)
        {
            var totalRate = offer.PlatformShareRate.Value + offer.RestaurantShareRate.Value;
            if (totalRate > 0)
            {
                var platformRatio = offer.PlatformShareRate.Value / totalRate;
                platformFunded = Math.Round(discount * platformRatio, 3, MidpointRounding.AwayFromZero);
                restaurantFunded = discount - platformFunded;
            }
        }

        var redemption = new OfferRedemption
        {
            OfferId = offer.Id,
            CustomerId = request.CustomerId,
            SubscriptionId = request.SubscriptionId,
            PromoCodeId = request.PromoCodeId,
            DiscountAppliedAmount = new Money(discount, "KWD"),
            PlatformFundedAmount = new Money(platformFunded, "KWD"),
            RestaurantFundedAmount = new Money(restaurantFunded, "KWD"),
            RedeemedAtUtc = now
        };

        offer.CurrentUsageCount++;
        _context.OfferRedemptions.Add(redemption);

        await _context.SaveChangesAsync(cancellationToken);

        return new OfferRedemptionResultDto(
            redemption.Id,
            redemption.OfferId,
            redemption.CustomerId,
            redemption.SubscriptionId,
            discount,
            platformFunded,
            restaurantFunded,
            now
        );
    }
}
