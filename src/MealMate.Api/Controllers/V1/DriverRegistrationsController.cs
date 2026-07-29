using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/drivers/registrations")]
public sealed class DriverRegistrationsController : ApiControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create([FromBody] CreateDriverRegistrationCommand body, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(body, cancellationToken), "/api/v1/drivers/registrations");

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDriverRegistrationRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateDriverRegistrationCommand(
            id,
            body.FullNameAr,
            body.FullNameEn,
            body.Phone,
            body.Email,
            body.NationalId,
            body.LicenseNumber,
            body.VehiclePlate,
            body.VehicleType,
            body.CountryId,
            body.RegionId), cancellationToken));

    [HttpPost("{id:guid}/profile-image")]
    [Authorize]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadProfileImage(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return HandleResult(await Sender.Send(new UploadDriverProfileImageCommand(id, file.FileName, file.ContentType, stream), cancellationToken));
    }

    [HttpPost("{id:guid}/documents")]
    [Authorize]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(
        Guid id,
        [FromForm] DocumentType documentType,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(new UploadDriverRegistrationDocumentCommand(
            id,
            documentType,
            file.FileName,
            file.ContentType,
            stream), cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/documents/{result.Value.Id}", result.Value)
            : HandleResult(result);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SubmitDriverRegistrationCommand(id), cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetDriverRegistrationQuery(id), cancellationToken));
}

public sealed record UpdateDriverRegistrationRequest(
    string? FullNameAr,
    string? FullNameEn,
    string? Phone,
    string? Email,
    string? NationalId,
    string? LicenseNumber,
    string? VehiclePlate,
    string? VehicleType,
    Guid? CountryId,
    Guid? RegionId);

[Route("api/v1/admin/drivers/registrations")]
public sealed class AdminDriverRegistrationsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.DriverRegView)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] VerificationStatus? status = null, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListDriverRegistrationsQuery(page, pageSize, status), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.DriverRegView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetDriverRegistrationQuery(id), cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.DriverRegApprove)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminDriverDecisionCommand(id, "approve", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [HasPermission(PermissionCodes.DriverRegReject)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminDriverDecisionCommand(id, "reject", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/request-changes")]
    [HasPermission(PermissionCodes.DriverRegApprove)]
    public async Task<IActionResult> RequestChanges(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminDriverDecisionCommand(id, "request-changes", body.Reason), cancellationToken));
}
