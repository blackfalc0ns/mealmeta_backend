using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/catalog")]
public sealed class RegionCatalogController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    public RegionCatalogController(ICurrentUser currentUser) => _currentUser = currentUser;
    [HttpGet("calling-codes")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<CallingCodeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCallingCodes(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetArabicCallingCodesQuery(), cancellationToken));

    [HttpGet("nationalities")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<NationalityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNationalities(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetNationalitiesQuery(), cancellationToken));

    [HttpGet("vehicles/types")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<VehicleTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVehicleTypes(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetVehicleTypesQuery(), cancellationToken));

    [HttpGet("vehicles/colors")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<VehicleColorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVehicleColors(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetVehicleColorsQuery(), cancellationToken));

    [HttpGet("vehicles/models")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<VehicleModelSearchResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchVehicleModels(
        [FromQuery] string? search = null,
        [FromQuery] string? vehicleType = null,
        [FromQuery] int limit = 40,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new SearchVehicleModelsQuery(search, vehicleType, limit), cancellationToken));

    [HttpGet("vehicles/makes")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<VehicleMakeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVehicleMakes(
        [FromQuery] string? vehicleType = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetVehicleMakesQuery(vehicleType), cancellationToken));

    [HttpGet("countries/{countryCode}/regions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRegions(string countryCode, [FromQuery] bool leafOnly = false, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetCountryRegionCatalogQuery(countryCode, leafOnly), cancellationToken));

    [HttpGet("locations")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLocations(
        [FromQuery] string countryCode = "KW",
        [FromQuery] Guid? areaId = null,
        CancellationToken cancellationToken = default)
        // Restaurants never receive blocks. Customers request blocks lazily after
        // choosing an area, which avoids returning every Kuwait block on app start.
        => HandleResult(await Sender.Send(new GetLocationCatalogQuery(
            countryCode,
            _currentUser.UserType == UserType.Restaurant ? null : areaId), cancellationToken));
}

[ApiController]
[Route("api/v1/admin/catalog/regions")]
public sealed class AdminRegionCatalogController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> Get([FromQuery] string countryCode = "KW", [FromQuery] bool includeInactive = true, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetCountryRegionCatalogQuery(countryCode, false, includeInactive), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Create([FromBody] AdminCreateRegionRequest body, CancellationToken cancellationToken = default)
        => HandleCreated(await Sender.Send(new AdminCreateRegionCommand(body.CountryId, body.Code, body.NameAr, body.NameEn, body.Type, body.ParentId), cancellationToken), "/api/v1/admin/catalog/regions");

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Update(Guid id, [FromBody] AdminUpdateRegionRequest body, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new AdminUpdateRegionCommand(id, body.Code, body.NameAr, body.NameEn, body.Type, body.ParentId, body.IsActive), cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new AdminDeleteRegionCommand(id), cancellationToken));
}

public sealed record AdminCreateRegionRequest(Guid CountryId, string Code, string NameAr, string NameEn, RegionType Type, Guid? ParentId);
public sealed record AdminUpdateRegionRequest(string Code, string NameAr, string NameEn, RegionType Type, Guid? ParentId, bool IsActive);
