using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/subscription_pricing_settings")]
public sealed class SubscriptionPricingSettingsController : ApiControllerBase
{
    private readonly ISubscriptionPricingSettingsService _pricingSettingsService;

    public SubscriptionPricingSettingsController(ISubscriptionPricingSettingsService pricingSettingsService)
    {
        _pricingSettingsService = pricingSettingsService;
    }

    [HttpGet]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => HandleResult(await _pricingSettingsService.GetSettingsAsync(cancellationToken));

    [HttpGet("tier_labels")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTierLabels(CancellationToken cancellationToken)
        => HandleResult(await _pricingSettingsService.GetTierLabelsAsync(cancellationToken));

    [HttpPut]
    [HasPermission(PermissionCodes.ProgramsManage)]
    public async Task<IActionResult> Put([FromBody] SubscriptionPricingSettingsDocument document, CancellationToken cancellationToken)
        => HandleResult(await _pricingSettingsService.UpdateSettingsAsync(document, cancellationToken));
}
