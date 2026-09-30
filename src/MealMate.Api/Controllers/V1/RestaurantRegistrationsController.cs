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
            id,
            body.LegalNameAr,
            body.LegalNameEn,
            body.TradeNameAr,
            body.TradeNameEn,
            body.ContactPhone,
            body.ContactEmail,
            body.CommercialRegistrationNumber,
            body.CommercialRegistrationExpiry,
            body.CommercialLicenseNumber,
            body.CommercialLicenseExpiry,
            body.FoodLicenseNumber,
            body.FoodLicenseExpiry,
            body.LegalEntityType,
            body.CompanyCivilId,
            body.OwnerName,
            body.AuthorizedSignatoryCivilId,
            body.AuthorizedSignatoryTitle,
            body.PrimaryContactName,
            body.PrimaryContactTitle,
            body.PrimaryContactPhone,
            body.PrimaryContactEmail,
            body.BankName,
            body.BankBeneficiaryName,
            body.Iban,
            body.BankAccountNumber,
            body.SwiftCode,
            body.Address,
            body.CountryId,
            body.RegionId,
            body.Website,
            body.InstagramHandle,
            body.DeliveryByRestaurantConfirmed,
            body.DataResponsibilityConfirmed,
            body.RestaurantNotes,
            body.ServiceRegionIds,
            body.Latitude,
            body.Longitude,
            body.SelectedProgramIds,
            body.SelectedBundleIds), cancellationToken));

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

    [HttpPost("test-submit")]
    [AllowAnonymous]
    public async Task<IActionResult> TestSubmit(
        [FromBody] CreateAndSubmitTestRestaurantCommand? body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(body ?? new CreateAndSubmitTestRestaurantCommand(null), cancellationToken));
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
    public async Task<IActionResult> GetDetail(
        Guid id,
        [FromQuery] bool includeSensitive = false,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new GetAdminRestaurantRegistrationDetailQuery(id, includeSensitive),
            cancellationToken));

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

    [HttpPost("{id:guid}/resend-notification")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> ResendNotification(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ResendRestaurantRegistrationNotificationCommand(id), cancellationToken));
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
    public async Task<IActionResult> Get(
        Guid id,
        [FromQuery] bool includeSensitive = false,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new GetAdminRestaurantProfileDetailQuery(id, includeSensitive),
            cancellationToken));

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

    [HttpPost("{id:guid}/lock-login")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> LockLogin(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "lock-login", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Archive(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "archive", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/close")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Close(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "close", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/unlock-login")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UnlockLogin(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "unlock-login", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/unarchive")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Unarchive(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "unarchive", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/reopen")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] LifecycleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRestaurantLifecycleCommand(id, "reopen", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/settings-changes/approve")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> ApproveSettingsChange(
        Guid id,
        [FromBody] SettingsChangeDecisionRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminApproveRestaurantSettingsChangeCommand(id, body.Reason), cancellationToken));

    [HttpPost("{id:guid}/settings-changes/request-changes")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> RequestSettingsChanges(
        Guid id,
        [FromBody] SettingsChangeDecisionRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminRequestChangesRestaurantSettingsChangeCommand(id, body.Reason ?? string.Empty), cancellationToken));
}

public sealed record UpdateRestaurantRegistrationBody(
    string? LegalNameAr,
    string? LegalNameEn,
    string? TradeNameAr,
    string? TradeNameEn,
    string? ContactPhone,
    string? ContactEmail,
    string? CommercialRegistrationNumber,
    DateOnly? CommercialRegistrationExpiry,
    string? CommercialLicenseNumber,
    DateOnly? CommercialLicenseExpiry,
    string? FoodLicenseNumber,
    DateOnly? FoodLicenseExpiry,
    string? LegalEntityType,
    string? CompanyCivilId,
    string? OwnerName,
    string? AuthorizedSignatoryCivilId,
    string? AuthorizedSignatoryTitle,
    string? PrimaryContactName,
    string? PrimaryContactTitle,
    string? PrimaryContactPhone,
    string? PrimaryContactEmail,
    string? BankName,
    string? BankBeneficiaryName,
    string? Iban,
    string? BankAccountNumber,
    string? Address,
    Guid? CountryId,
    Guid? RegionId,
    bool? DeliveryByRestaurantConfirmed = null,
    bool? DataResponsibilityConfirmed = null,
    List<string>? ServiceRegionIds = null,
    string? Website = null,
    string? InstagramHandle = null,
    string? SwiftCode = null,
    string? RestaurantNotes = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    List<Guid>? SelectedProgramIds = null,
    List<Guid>? SelectedBundleIds = null);

public sealed record DecisionRequest(string Reason);

public sealed record FieldReviewRequest(string Action, string? Reason);

public sealed record LifecycleRequest(string Reason);
