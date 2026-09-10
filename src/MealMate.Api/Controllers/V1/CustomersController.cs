using MealMate.Api.Common;
using MealMate.Application.Features.CustomerPush;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.Enums;
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

    [HttpDelete("me/profile-image")]
    public async Task<IActionResult> DeleteProfileImage(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteCustomerProfileImageCommand(), cancellationToken));

    [HttpPost("me/profile-image")]
    public async Task<IActionResult> SetProfileImage([FromBody] SetCustomerProfileImageRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SetCustomerProfileImageCommand(body.StorageKey), cancellationToken));

    [HttpPut("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangeCustomerPasswordRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ChangeCustomerPasswordCommand(body.CurrentPassword, body.NewPassword), cancellationToken));

    [HttpGet("me/food-preferences")]
    public async Task<IActionResult> GetFoodPreferences(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCustomerFoodPreferencesQuery(), cancellationToken));

    [HttpGet("me/food-preferences/catalog")]
    public async Task<IActionResult> GetFoodPreferenceCatalog(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCustomerFoodPreferenceCatalogQuery(), cancellationToken));

    [HttpGet("me/food-preferences/allergies")]
    public async Task<IActionResult> GetAllergies([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetCustomerAllergiesQuery(page, pageSize, search), cancellationToken));

    [HttpGet("me/food-preferences/disliked-ingredients")]
    public async Task<IActionResult> GetDislikedIngredients([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetCustomerDislikedIngredientsQuery(page, pageSize, search), cancellationToken));

    [HttpGet("me/food-preferences/dietary")]
    public async Task<IActionResult> GetDietaryPreferences([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetCustomerDietaryPreferencesQuery(page, pageSize, search), cancellationToken));

    [HttpPut("me/food-preferences")]
    public async Task<IActionResult> SaveFoodPreferences([FromBody] CustomerFoodPreferencesRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SaveCustomerFoodPreferencesCommand(
            body.DietaryPattern, body.Allergies ?? [], body.DislikedFoods ?? [], body.Notes, body.DietaryPreferenceCodes), cancellationToken));

    /// <summary>Saves only the allergy selection screen without changing dietary preferences or disliked foods.</summary>
    [HttpPut("me/food-preferences/allergies")]
    public async Task<IActionResult> SaveAllergies([FromBody] CustomerAllergiesRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SaveCustomerAllergiesCommand(body.Allergies ?? []), cancellationToken);
        return result.IsFailure
            ? HandleResult(result)
            : HandleResult(await Sender.Send(new GetCustomerAllergiesQuery(), cancellationToken));
    }

    [HttpPut("me/food-preferences/dietary")]
    public async Task<IActionResult> SaveDietaryPreferences([FromBody] CustomerDietaryPreferencesRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SaveCustomerDietaryPreferencesCommand(body.DietaryPreferenceCodes ?? []), cancellationToken);
        return result.IsFailure
            ? HandleResult(result)
            : HandleResult(await Sender.Send(new GetCustomerDietaryPreferencesQuery(), cancellationToken));
    }

    [HttpPut("me/food-preferences/disliked-ingredients")]
    public async Task<IActionResult> SaveDislikedIngredients([FromBody] CustomerDislikedIngredientsRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SaveCustomerDislikedIngredientsCommand(body.IngredientCodes ?? []), cancellationToken);
        return result.IsFailure
            ? HandleResult(result)
            : HandleResult(await Sender.Send(new GetCustomerDislikedIngredientsQuery(), cancellationToken));
    }

    [HttpGet("me/addresses")]
    public async Task<IActionResult> GetAddresses(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCustomerAddressesQuery(), cancellationToken));

    [HttpPost("me/addresses")]
    public async Task<IActionResult> CreateAddress([FromBody] CustomerAddressRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new CreateCustomerAddressCommand(body.Label, body.AddressLine, body.RegionId, body.Latitude, body.Longitude, body.Notes, body.IsDefault, body.NotifyOnDelivery, body.Governorate, body.Area, body.Block, body.Street, body.Avenue, body.Building, body.Unit, body.Floor, body.BlockRegionId), cancellationToken));

    [HttpPut("me/addresses/{id:guid}")]
    public async Task<IActionResult> UpdateAddress(Guid id, [FromBody] CustomerAddressRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateCustomerAddressCommand(id, body.Label, body.AddressLine, body.RegionId, body.Latitude, body.Longitude, body.Notes, body.IsDefault, body.NotifyOnDelivery, body.Governorate, body.Area, body.Block, body.Street, body.Avenue, body.Building, body.Unit, body.Floor, body.BlockRegionId), cancellationToken));

    [HttpDelete("me/addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteCustomerAddressCommand(id), cancellationToken));

    [HttpPost("me/addresses/preview-coverage")]
    public async Task<IActionResult> PreviewAddressCoverage(
        [FromBody] PreviewAddressCoverageRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new PreviewCustomerAddressCoverageCommand(
            body.AddressId,
            body.RegionId,
            body.BlockRegionId,
            body.Latitude,
            body.Longitude,
            body.Governorate,
            body.Area,
            body.Block,
            body.IsDefault), cancellationToken));

    [HttpGet("me/addresses/{id:guid}/preview-coverage")]
    public async Task<IActionResult> PreviewAddressCoverageById(
        Guid id,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new PreviewCustomerAddressCoverageCommand(AddressId: id), cancellationToken));

    [HttpPost("me/addresses/{id:guid}/set-default")]
    public async Task<IActionResult> SetDefaultAddress(
        Guid id,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SetDefaultCustomerAddressCommand(id), cancellationToken));

    [HttpPut("me/device-tokens")]
    public async Task<IActionResult> RegisterDeviceToken(
        [FromBody] RegisterCustomerDeviceTokenRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RegisterCustomerDeviceTokenCommand(
            body.Token,
            body.Platform,
            body.DeviceId), cancellationToken));

    [HttpDelete("me/device-tokens")]
    public async Task<IActionResult> UnregisterDeviceToken(
        [FromBody] UnregisterCustomerDeviceTokenRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UnregisterCustomerDeviceTokenCommand(body.Token), cancellationToken));

    /// <summary>Development-only: sends a test push to the current customer's registered device tokens.</summary>
    [HttpPost("me/device-tokens/test")]
    public async Task<IActionResult> SendTestPush(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SendCustomerTestPushCommand(), cancellationToken));
}

public sealed record UpdateCustomerProfileRequest(
    string? FullName,
    string? PreferredLanguage,
    Guid? DefaultRegionId,
    string? DeliveryAddressLine,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    DateOnly? DateOfBirth);

public sealed record ChangeCustomerPasswordRequest(string CurrentPassword, string NewPassword);
public sealed record CustomerAllergiesRequest(IReadOnlyList<FoodPreferenceItemDto>? Allergies);
public sealed record CustomerDietaryPreferencesRequest(IReadOnlyList<string>? DietaryPreferenceCodes);
public sealed record CustomerDislikedIngredientsRequest(IReadOnlyList<string>? IngredientCodes);
public sealed record SetCustomerProfileImageRequest(string StorageKey);

public sealed record RegisterCustomerDeviceTokenRequest(
    string Token,
    MobilePlatform Platform,
    string? DeviceId);

public sealed record UnregisterCustomerDeviceTokenRequest(string Token);

public sealed record CustomerFoodPreferencesRequest(
    string? DietaryPattern,
    IReadOnlyList<FoodPreferenceItemDto>? Allergies,
    IReadOnlyList<FoodPreferenceItemDto>? DislikedFoods,
    string? Notes,
    IReadOnlyList<string>? DietaryPreferenceCodes = null);
