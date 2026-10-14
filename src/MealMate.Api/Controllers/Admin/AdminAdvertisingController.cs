using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Advertising.Admin;
using MealMate.Application.Features.Advertising.Restaurant;
using MealMate.Application.Features.Advertising.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.Admin;

[Authorize]
[Route("api/v1/admin/advertising")]
public sealed class AdminAdvertisingController : ApiControllerBase
{
    [HttpGet("settings")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdvertisingSettingsQuery(), cancellationToken));

    [HttpPut("settings")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateAdvertisingSettingsCommand command, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken));

    [HttpPost("commercial-policy")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> ApproveCommercialPolicy([FromBody] ApproveAdCommercialPolicyCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/admin/advertising/commercial-policy");

    [HttpGet("commercial-policy")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetCommercialPolicy(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdCommercialPolicyQuery(), cancellationToken));

    [HttpPost("spaces")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> CreateSpace([FromBody] CreateAdSpaceCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/admin/advertising/spaces");

    [HttpGet("spaces")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetSpaces(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdSpacesQuery(), cancellationToken));

    [HttpPost("auctions")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> CreateAuction([FromBody] CreateAdAuctionCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/admin/advertising/auctions");

    [HttpGet("auctions")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetAuctions(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetOpenAuctionsQuery(), cancellationToken));

    [HttpPost("placements/{id:guid}/review")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> ReviewCreative(Guid id, [FromBody] ReviewCreativeRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ReviewAdCreativeCommand(id, request.Approve, request.Notes), cancellationToken));

    [HttpPost("placements/{id:guid}/stop")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> StopPlacement(Guid id, [FromBody] StopPlacementRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminStopAdPlacementCommand(id, request.Reason), cancellationToken));

    [HttpGet("refund-reviews")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetRefundReviews(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetPendingAdRefundReviewsQuery(), cancellationToken));

    [HttpGet("payment-reconciliation-reviews")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetPaymentReconciliationReviews(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdPaymentReconciliationReviewsQuery(), cancellationToken));

    [HttpPost("refund-reviews/{id:guid}")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> ReviewRefund(Guid id, [FromBody] ReviewAdRefundRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ReviewAdRefundRequestCommand(id, request.Approve, request.Reason), cancellationToken));

    [HttpPost("sweep")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> Sweep(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RunAdSweepCommand(), cancellationToken));
}

public sealed record ReviewCreativeRequest(bool Approve, string? Notes);

public sealed record StopPlacementRequest(string Reason);

public sealed record ReviewAdRefundRequest(bool Approve, string Reason);
