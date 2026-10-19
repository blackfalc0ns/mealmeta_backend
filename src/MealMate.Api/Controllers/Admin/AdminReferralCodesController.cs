using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions.Referrals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.Admin;

[Authorize]
[Route("api/v1/admin/referral-codes")]
public sealed class AdminReferralCodesController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.OffersView)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetAdminReferralCodesListQuery(page, pageSize, q), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAdminReferralCodeCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return HandleCreated(result, $"api/v1/admin/referral-codes/{result.Value?.Id}");
    }

    [HttpPut("{id:guid}/status")]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> SetStatus(
        Guid id,
        [FromBody] SetReferralCodeStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SetReferralCodeStatusCommand(id, request.IsActive), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}/attributions")]
    [HasPermission(PermissionCodes.OffersView)]
    public async Task<IActionResult> GetAttributions(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetAdminReferralAttributionsQuery(id, page, pageSize), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record SetReferralCodeStatusRequest(bool IsActive);
