using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/promo_referral_discount_codes")]
public sealed class PromoReferralDiscountCodesController : ApiControllerBase
{
    [HttpGet("promo")]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> ListPromoCodes(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetPromoCodesListQuery(page, pageSize), cancellationToken));

    [HttpPost("promo")]
    [HasPermission(PermissionCodes.ProgramsManage)]
    public async Task<IActionResult> CreatePromoCode([FromBody] CreatePromoCodeCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/promo_referral_discount_codes/promo");

    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateCode([FromBody] ValidateCodeRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ValidateDiscountCodeQuery(request.Code, request.SubscriptionId, request.TotalAmountKwd), cancellationToken));
}

public sealed record ValidateCodeRequest(
    string Code,
    Guid? SubscriptionId = null,
    decimal TotalAmountKwd = 0m);
