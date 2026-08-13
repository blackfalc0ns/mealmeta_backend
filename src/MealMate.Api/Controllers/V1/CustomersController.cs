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

    [HttpPut("me/food-preferences")]
    public async Task<IActionResult> SaveFoodPreferences([FromBody] CustomerFoodPreferencesRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SaveCustomerFoodPreferencesCommand(
            body.DietaryPattern, body.Allergies ?? [], body.DislikedFoods ?? [], body.Notes, body.DietaryPreferenceCodes), cancellationToken));

    /// <summary>Saves only the allergy selection screen without changing dietary preferences or disliked foods.</summary>
    [HttpPut("me/food-preferences/allergies")]
    public async Task<IActionResult> SaveAllergies([FromBody] CustomerAllergiesRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SaveCustomerAllergiesCommand(body.Allergies ?? []), cancellationToken);
        if (result.IsFailure) return HandleResult(result);

        // Keep this page-specific endpoint focused on the allergy selection. The
        // full preferences response includes disliked foods by design, which made
        // mobile clients incorrectly treat the two endpoints as interchangeable.
        return Ok(new CustomerAllergiesDto(result.Value.Allergies));
    }

    [HttpPut("me/food-preferences/dietary")]
    public async Task<IActionResult> SaveDietaryPreferences([FromBody] CustomerDietaryPreferencesRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SaveCustomerDietaryPreferencesCommand(body.DietaryPreferenceCodes ?? []), cancellationToken));

    [HttpPut("me/food-preferences/disliked-ingredients")]
    public async Task<IActionResult> SaveDislikedIngredients([FromBody] CustomerDislikedIngredientsRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SaveCustomerDislikedIngredientsCommand(body.IngredientCodes ?? []), cancellationToken));

    [HttpGet("me/addresses")]
    public async Task<IActionResult> GetAddresses(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCustomerAddressesQuery(), cancellationToken));

    [HttpPost("me/addresses")]
    public async Task<IActionResult> CreateAddress([FromBody] CustomerAddressRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new CreateCustomerAddressCommand(body.Label, body.AddressLine, body.RegionId, body.Latitude, body.Longitude, body.Notes, body.IsDefault, body.NotifyOnDelivery, body.Governorate, body.Area, body.Block, body.Street, body.Building, body.Unit, body.Floor, body.BlockRegionId), cancellationToken));

    [HttpPut("me/addresses/{id:guid}")]
    public async Task<IActionResult> UpdateAddress(Guid id, [FromBody] CustomerAddressRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateCustomerAddressCommand(id, body.Label, body.AddressLine, body.RegionId, body.Latitude, body.Longitude, body.Notes, body.IsDefault, body.NotifyOnDelivery, body.Governorate, body.Area, body.Block, body.Street, body.Building, body.Unit, body.Floor, body.BlockRegionId), cancellationToken));

    [HttpDelete("me/addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteCustomerAddressCommand(id), cancellationToken));
}

public sealed record UpdateCustomerProfileRequest(
    string? FullName,
    string? PreferredLanguage,
    Guid? DefaultRegionId,
    string? DeliveryAddressLine,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    DateOnly? DateOfBirth);

public sealed record CustomerAddressRequest(
    string Label,
    string AddressLine,
    Guid? RegionId,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    bool IsDefault,
    bool NotifyOnDelivery = true,
    string? Governorate = null,
    string? Area = null,
    string? Block = null,
    string? Street = null,
    string? Building = null,
    string? Unit = null,
    string? Floor = null,
    Guid? BlockRegionId = null);

public sealed record ChangeCustomerPasswordRequest(string CurrentPassword, string NewPassword);
public sealed record CustomerAllergiesRequest(IReadOnlyList<FoodPreferenceItemDto>? Allergies);
public sealed record CustomerAllergiesDto(IReadOnlyList<FoodPreferenceItemDto> Allergies);
public sealed record CustomerDietaryPreferencesRequest(IReadOnlyList<string>? DietaryPreferenceCodes);
public sealed record CustomerDislikedIngredientsRequest(IReadOnlyList<string>? IngredientCodes);
public sealed record SetCustomerProfileImageRequest(string StorageKey);

public sealed record CustomerFoodPreferencesRequest(
    string? DietaryPattern,
    IReadOnlyList<FoodPreferenceItemDto>? Allergies,
    IReadOnlyList<FoodPreferenceItemDto>? DislikedFoods,
    string? Notes,
    IReadOnlyList<string>? DietaryPreferenceCodes = null);
