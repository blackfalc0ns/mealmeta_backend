using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions.Referrals;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.Customer;

[Authorize]
[Route("api/v1/customer/referrals")]
public sealed class CustomerReferralsController : ApiControllerBase
{
    private ICurrentUser? _currentUser;
    private IConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public CustomerReferralsController(ICurrentUser currentUser, IConfiguration configuration)
    {
        _currentUser = currentUser;
        _configuration = configuration;
    }

    public CustomerReferralsController() { }

    private ICurrentUser CurrentUser => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>Attributes the authenticated customer to a referral code (once, never self).</summary>
    [HttpPost("attribute")]
    public async Task<IActionResult> Attribute([FromBody] AttributeReferralCommand command, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    /// <summary>Returns the caller's own referral code, share link and counts. No invited-customer identities.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new GetReferralSummaryQuery(), cancellationToken);
        return HandleResult(result);
    }
}
