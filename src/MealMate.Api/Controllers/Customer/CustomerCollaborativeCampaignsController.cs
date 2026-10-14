using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.Customer;

[Route("api/v1/customer/campaigns")]
public sealed class CustomerCollaborativeCampaignsController : ApiControllerBase
{
    private ICurrentUser? _currentUser;
    private ICampaignPaymentService? _payments;

    public CustomerCollaborativeCampaignsController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    [ActivatorUtilitiesConstructor]
    public CustomerCollaborativeCampaignsController(ICurrentUser currentUser, ICampaignPaymentService payments)
    {
        _currentUser = currentUser;
        _payments = payments;
    }

    public CustomerCollaborativeCampaignsController() { }

    private ICurrentUser CurrentUser => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
    private ICampaignPaymentService Payments => _payments ??= HttpContext.RequestServices.GetRequiredService<ICampaignPaymentService>();

    [Authorize]
    [HttpPost("{sessionId:guid}/payment")]
    public async Task<IActionResult> StartPayment(Guid sessionId, [FromBody] StartCampaignPaymentRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue) return Forbid();
        return PaymentResult(await Payments.StartAsync(sessionId, CurrentUser.UserId.Value, request, cancellationToken));
    }

    [Authorize]
    [HttpGet("{sessionId:guid}/payment")]
    public async Task<IActionResult> GetPaymentStatus(Guid sessionId, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue) return Forbid();
        return PaymentResult(await Payments.GetStatusAsync(sessionId, CurrentUser.UserId.Value, cancellationToken));
    }

    private IActionResult PaymentResult<T>(Result<T> result)
    {
        var response = HandleResult(result);
        if (result.IsFailure && result.Error.Type == ErrorType.Failure && response is ObjectResult failure)
        {
            failure.StatusCode = StatusCodes.Status503ServiceUnavailable;
            if (failure.Value is ProblemDetails problem) problem.Status = StatusCodes.Status503ServiceUnavailable;
        }
        return response;
    }

    [AllowAnonymous]
    [HttpPost("price-preview")]
    public async Task<IActionResult> GetPricePreview([FromBody] GetCampaignPricePreviewQuery query, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpPost("checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCampaignCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        // The buyer is always the authenticated customer; a client-supplied CustomerId is never trusted.
        if (command.CustomerId != CurrentUser.UserId.Value)
        {
            command = command with { CustomerId = CurrentUser.UserId.Value };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpPost("subscriptions/{id:guid}/cancel")]
    public async Task<IActionResult> CancelSubscription(Guid id, [FromBody] CancelCampaignSubscriptionCommand command, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        if (id != command.SubscriptionId)
        {
            command = command with { SubscriptionId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}
