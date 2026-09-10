using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Freeze;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/subscription_freeze_policy")]
public sealed class SubscriptionFreezePolicyController : ApiControllerBase
{
    private readonly ISubscriptionFreezePolicyService _freezePolicyService;

    public SubscriptionFreezePolicyController(ISubscriptionFreezePolicyService freezePolicyService)
        => _freezePolicyService = freezePolicyService;

    [HttpGet]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => HandleResult(await _freezePolicyService.GetPolicyAsync(ct));

    [HttpPut]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> Put([FromBody] SubscriptionFreezePolicyDocument document, CancellationToken ct)
        => HandleResult(await _freezePolicyService.UpdatePolicyAsync(document, ct));
}
