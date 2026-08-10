using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/catalog")]
public sealed class RegionCatalogController : ApiControllerBase
{
    [HttpGet("calling-codes")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<CallingCodeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCallingCodes(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetArabicCallingCodesQuery(), cancellationToken));

    [HttpGet("countries/{countryCode}/regions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRegions(string countryCode, [FromQuery] bool leafOnly = false, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetCountryRegionCatalogQuery(countryCode, leafOnly), cancellationToken));
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
