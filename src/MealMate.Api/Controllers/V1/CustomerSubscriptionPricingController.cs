using MealMate.Api.Common;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/customer_subscription_pricing")]
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
        [FromQuery] decimal campaignDiscountKwd = 0m,
        [FromQuery] decimal promoCodeDiscountKwd = 0m,
        [FromQuery] bool includeFriday = false,
        [FromQuery] int fridayDays = 0,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new PreviewCustomerPricingQuery(programId, bundleId, tier, durationDays, campaignDiscountKwd, promoCodeDiscountKwd, includeFriday, fridayDays), cancellationToken));
}
