using MealMate.Api.Common;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/customer_subscription_pricing")]
[Route("api/v1/customer/subscription-pricing")]
public sealed class CustomerSubscriptionPricingController : ApiControllerBase
{
    [HttpGet("catalog")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), cancellationToken));

    [HttpGet("preview")]
    [AllowAnonymous]
    public async Task<IActionResult> PreviewPricing(
        [FromQuery] Guid programId,
        [FromQuery] Guid bundleId,
        [FromQuery] RestaurantTier tier,
        [FromQuery] int durationDays,
        [FromQuery] Guid? offerId = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] string? promoCode = null,
        [FromQuery] decimal campaignDiscountKwd = 0m,
        [FromQuery] decimal promoCodeDiscountKwd = 0m,
        [FromQuery] bool includeFriday = false,
        [FromQuery] int fridayDays = 0,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new PreviewCustomerPricingQuery(
            programId, bundleId, tier, durationDays, campaignDiscountKwd, promoCodeDiscountKwd, includeFriday, fridayDays, offerId, customerId, promoCode), cancellationToken));

    [HttpPost("preview")]
    [AllowAnonymous]
    public async Task<IActionResult> PreviewPricingPost(
        [FromBody] PreviewCustomerPricingRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new PreviewCustomerPricingQuery(
            request.ProgramId,
            request.BundleId,
            request.Tier,
            request.DurationDays,
            request.CampaignDiscountKwd,
            request.PromoCodeDiscountKwd,
            request.IncludeFriday,
            request.FridayDays,
            request.OfferId,
            request.CustomerId,
            request.PromoCode,
            request.CustomerSubscriptionType ?? "Individual"), cancellationToken));
}

public sealed record PreviewCustomerPricingRequest(
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    Guid? OfferId = null,
    Guid? CustomerId = null,
    string? PromoCode = null,
    decimal CampaignDiscountKwd = 0m,
    decimal PromoCodeDiscountKwd = 0m,
    bool IncludeFriday = false,
    int FridayDays = 0,
    string? CustomerSubscriptionType = "Individual");
