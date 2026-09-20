using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Api.Common;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.Catalog;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using MealMate.Application.Features.Uploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth/staff/driver-registration")]
[EnableRateLimiting("auth")]
public sealed class StaffDriverRegistrationController : ApiControllerBase
{
    [HttpGet("restaurants")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRestaurants(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetStaffRegistrationRestaurantsQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("nationalities")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<NationalityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNationalities(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetNationalitiesQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("upload")]
    [AllowAnonymous]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: Messages.GetMessage(MessageCodes.Titles.ValidationFailed),
                detail: Messages.GetMessage(MessageCodes.Driver.FileRequired));
        }

        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(new UploadFileCommand(
            file.FileName,
            file.ContentType,
            stream,
            "driver-registration",
            "drivers"), cancellationToken);

        if (result.IsFailure)
        {
            return HandleResult(result);
        }

        return Ok(new
        {
            storageKey = result.Value.StorageKey,
            readUrl = result.Value.ReadUrl,
            fileName = result.Value.FileName,
            contentType = result.Value.ContentType,
            sizeBytes = result.Value.SizeBytes,
            message = Messages.GetMessage(MessageCodes.Driver.FileUploadSuccess)
        });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterDriver(
        [FromBody] StaffDriverSelfRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatus(
        [FromQuery] string? phone,
        [FromQuery] System.Guid? registrationId,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetDriverApplicationStatusQuery(phone, registrationId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/resubmit")]
    [AllowAnonymous]
    public async Task<IActionResult> Resubmit(
        System.Guid id,
        [FromBody] ResubmitDriverRegistrationApiRequest body,
        CancellationToken cancellationToken)
    {
        var command = new ResubmitDriverRegistrationCommand(
            id,
            body.LicenseNumber,
            body.LicenseExpiry,
            body.VehiclePlate,
            body.VehicleModel,
            body.VehicleType,
            body.VehicleYear,
            body.VehicleColor,
            body.IsVehicleOwned,
            body.VehicleLicenseExpiry,
            body.ContractExpiry,
            body.NationalIdFrontStorageKey,
            body.NationalIdBackStorageKey,
            body.DrivingLicenseFrontStorageKey,
            body.DrivingLicenseBackStorageKey,
            body.VehicleRegistrationStorageKey,
            body.ProfileImageStorageKey,
            body.VehiclePhotoStorageKey,
            body.ContractStorageKey);

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}

public sealed record ResubmitDriverRegistrationApiRequest(
    string? LicenseNumber = null,
    System.DateTime? LicenseExpiry = null,
    string? VehiclePlate = null,
    string? VehicleModel = null,
    string? VehicleType = null,
    int? VehicleYear = null,
    string? VehicleColor = null,
    bool? IsVehicleOwned = null,
    System.DateTime? VehicleLicenseExpiry = null,
    System.DateTime? ContractExpiry = null,
    string? NationalIdFrontStorageKey = null,
    string? NationalIdBackStorageKey = null,
    string? DrivingLicenseFrontStorageKey = null,
    string? DrivingLicenseBackStorageKey = null,
    string? VehicleRegistrationStorageKey = null,
    string? ProfileImageStorageKey = null,
    string? VehiclePhotoStorageKey = null,
    string? ContractStorageKey = null);
