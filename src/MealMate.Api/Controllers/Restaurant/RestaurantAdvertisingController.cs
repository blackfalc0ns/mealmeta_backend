using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Advertising.Restaurant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.Restaurant;

[Authorize]
[Route("api/v1/restaurant/advertising")]
public sealed class RestaurantAdvertisingController : ApiControllerBase
{
    [HttpGet("opportunities")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetOpportunities(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdOpportunitiesQuery(), cancellationToken));

    [HttpPost("auctions/{auctionId:guid}/bids")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> PlaceBid(Guid auctionId, [FromBody] PlaceBidRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(
            new PlaceAdBidCommand(auctionId, request.Amount, request.IdempotencyKey), cancellationToken));

    [HttpGet("bids")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetMyBids(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetMyAdBidsQuery(), cancellationToken));

    [HttpGet("bids/page")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetMyBidPage(CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => HandleResult(await Sender.Send(new GetMyAdBidPageQuery(page, pageSize), cancellationToken));

    /// <summary>Winner confirms a captured payment for its auction; idempotent by provider transaction.</summary>
    [HttpPost("auctions/{auctionId:guid}/payment")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> RecordPayment(Guid auctionId, [FromBody] RecordAdPaymentRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(
            new RecordAdPaymentCommand(auctionId, request.PaymentProvider, request.ProviderTransactionId, request.Amount, request.CurrencyCode),
            cancellationToken));

    [HttpPost("auctions/{auctionId:guid}/payment-session")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> CreatePaymentSession(Guid auctionId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new CreateAdPaymentSessionCommand(auctionId), cancellationToken));

    [HttpPost("auctions/{auctionId:guid}/creative")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> SubmitCreative(Guid auctionId, [FromBody] SubmitAdCreativeCommand command, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command with { AuctionId = auctionId }, cancellationToken));

    [HttpPost("placements/{placementId:guid}/stop")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> StopPlacement(Guid placementId, [FromBody] RestaurantStopPlacementRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RestaurantStopAdPlacementCommand(placementId, request.Reason), cancellationToken));

    [HttpPost("next-bidder-offers/{offerId:guid}/respond")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> RespondToNextBidderOffer(Guid offerId, [FromBody] NextBidderOfferResponse request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RespondToAdAuctionNextBidderOfferCommand(offerId, request.Accept, request.Reason), cancellationToken));

    [HttpGet("next-bidder-offers")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetPendingNextBidderOffers(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetPendingAdAuctionOffersQuery(), cancellationToken));
}

public sealed record PlaceBidRequest(decimal Amount, string IdempotencyKey);

public sealed record RecordAdPaymentRequest(string PaymentProvider, string ProviderTransactionId, decimal Amount, string CurrencyCode);

public sealed record RestaurantStopPlacementRequest(string Reason);
public sealed record NextBidderOfferResponse(bool Accept, string Reason);
