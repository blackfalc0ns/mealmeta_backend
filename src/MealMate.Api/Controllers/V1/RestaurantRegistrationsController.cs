using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/registrations")]
public sealed class RestaurantRegistrationsController : ApiControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateRestaurantRegistrationCommand body, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(body, cancellationToken), "/api/v1/restaurants/registrations");

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRestaurantRegistrationBody body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateRestaurantRegistrationCommand(
            id, body.LegalNameAr, body.LegalNameEn, body.TradeNameAr, body.TradeNameEn,
            body.ContactPhone, body.ContactEmail, body.CommercialRegistrationNumber,
            body.Address, body.OwnerName, body.CountryId, body.RegionId), cancellationToken));

    [HttpPost("{id:guid}/submit")]
    [Authorize]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SubmitRestaurantRegistrationCommand(id), cancellationToken));

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMyRegistration(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetMyRestaurantRegistrationQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantRegistrationQuery(id), cancellationToken));
}

[Route("api/v1/admin/restaurants/registrations")]
public sealed class AdminRestaurantRegistrationsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] VerificationStatus? status = null, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListRestaurantRegistrationsQuery(page, pageSize, status), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantRegistrationQuery(id), cancellationToken));

    [HttpGet("{id:guid}/detail")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdminRestaurantRegistrationDetailQuery(id), cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantDecisionCommand(id, "approve", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [HasPermission(PermissionCodes.RestaurantRegReject)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantDecisionCommand(id, "reject", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/fields/{fieldKey}/review")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> FieldReview(Guid id, string fieldKey, [FromBody] FieldReviewRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminFieldReviewCommand(id, fieldKey, body.Action, body.Reason), cancellationToken));

    [HttpPost("{id:guid}/request-changes")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> RequestChanges(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantDecisionCommand(id, "request-changes", body.Reason), cancellationToken));
}

[Route("api/v1/admin/restaurants")]
public sealed class AdminRestaurantsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListAdminRestaurantsQuery(page, pageSize, isActive), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdminRestaurantProfileDetailQuery(id), cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Activate(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "activate", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Suspend(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "suspend", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Reactivate(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "reactivate", body.Reason), cancellationToken));
}

public sealed record UpdateRestaurantRegistrationBody(
    string? LegalNameAr,
    string? LegalNameEn,
    string? TradeNameAr,
    string? TradeNameEn,
    string? ContactPhone,
    string? ContactEmail,
    string? CommercialRegistrationNumber,
    string? Address,
    string? OwnerName,
    Guid? CountryId,
    Guid? RegionId);

public sealed record DecisionRequest(string Reason);

public sealed record FieldReviewRequest(string Action, string? Reason);

public sealed record LifecycleRequest(string Reason);
