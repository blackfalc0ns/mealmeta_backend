using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/customers")]
[Authorize]
public sealed class CustomersController : ApiControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCustomerProfileQuery(), cancellationToken));

    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateCustomerProfileRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateCustomerProfileCommand(
            body.FullName,
            body.PreferredLanguage,
            body.DefaultRegionId,
            body.DeliveryAddressLine,
            body.DeliveryLatitude,
            body.DeliveryLongitude,
            body.DateOfBirth), cancellationToken));

    [HttpPost("me/profile-image")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadProfileImage(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return HandleResult(await Sender.Send(new UploadCustomerProfileImageCommand(
            file.FileName,
            file.ContentType,
            stream), cancellationToken));
    }

    [HttpDelete("me/profile-image")]
    public async Task<IActionResult> DeleteProfileImage(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteCustomerProfileImageCommand(), cancellationToken));
}

public sealed record UpdateCustomerProfileRequest(
    string? FullName,
    string? PreferredLanguage,
    Guid? DefaultRegionId,
    string? DeliveryAddressLine,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    DateOnly? DateOfBirth);
