using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/pricing")]
[Authorize]
public sealed class RestaurantPricingWorkspaceController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IAppDbContext _db;

    public RestaurantPricingWorkspaceController(ICurrentUser currentUser, IAppDbContext db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId is not { } restaurantId)
            return HandleResult(Error.Forbidden("auth.forbidden", "A restaurant account is required."));

        return HandleResult(await Sender.Send(new GetRestaurantPricingWorkspaceQuery(restaurantId), cancellationToken));
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveMyRestaurantPricingRequest body, CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId is not { } restaurantId)
            return HandleResult(Error.Forbidden("auth.forbidden", "A restaurant account is required."));

        var isOffered = await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
            x => x.RestaurantId == restaurantId
                && x.ProgramId == body.ProgramId
                && x.BundleId == body.BundleId
                && x.IsActive,
            cancellationToken);
        if (!isOffered)
        {
            var isActiveCatalogBundle = await _db.Bundles.AsNoTracking().AnyAsync(
                x => x.Id == body.BundleId && x.ProgramId == body.ProgramId && x.IsActive,
                cancellationToken);
            if (!isActiveCatalogBundle)
                return HandleResult(Error.Forbidden("restaurant_pricing.offer_not_available", "This bundle is not available."));

            // First price for a legacy restaurant is also its explicit choice
            // to offer this program/bundle going forward.
            _db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                RestaurantId = restaurantId,
                ProgramId = body.ProgramId,
                BundleId = body.BundleId,
                IsActive = true,
            });
        }

        var result = await Sender.Send(new SetRestaurantPricingCommand(
            restaurantId,
            body.ProgramId,
            body.BundleId,
            body.ReferencePrice26,
            body.ContractedSupplyPrice26 ?? body.ReferencePrice26,
            body.ChangeRequestReason), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetAvailableCatalog(CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId is not { } restaurantId)
            return HandleResult(Error.Forbidden("auth.forbidden", "A restaurant account is required."));

        var offeredBundleIds = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive)
            .Select(x => x.BundleId)
            .ToListAsync(cancellationToken);

        var offeredBundleIdSet = offeredBundleIds.ToHashSet();

        var rows = await (
            from program in _db.Programs.AsNoTracking()
            join bundle in _db.Bundles.AsNoTracking() on program.Id equals bundle.ProgramId
            where program.IsActive && bundle.IsActive
            orderby program.SortOrder, bundle.SortOrder
            select new RestaurantPricingWorkspaceRowDto(
                program.Id, program.NameAr, program.NameEn,
                bundle.Id, bundle.NameAr, bundle.NameEn,
                null, null, null, null, false)).ToListAsync(cancellationToken);

        return Ok(rows.Select(row => row with { IsAlreadyOffered = offeredBundleIdSet.Contains(row.BundleId) }));
    }

    [HttpPost("offers")]
    public async Task<IActionResult> AddOffer([FromBody] AddRestaurantPricingOfferRequest body, CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId is not { } restaurantId)
            return HandleResult(Error.Forbidden("auth.forbidden", "A restaurant account is required."));

        var valid = await _db.Bundles.AsNoTracking().AnyAsync(
            x => x.Id == body.BundleId && x.ProgramId == body.ProgramId && x.IsActive,
            cancellationToken);
        if (!valid)
            return HandleResult(Error.Validation("restaurant_pricing.invalid_bundle", "The selected bundle is unavailable."));

        var exists = await _db.RestaurantProgramBundleOffers.AnyAsync(
            x => x.RestaurantId == restaurantId && x.BundleId == body.BundleId,
            cancellationToken);
        if (!exists)
        {
            _db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                RestaurantId = restaurantId,
                ProgramId = body.ProgramId,
                BundleId = body.BundleId,
                IsActive = true,
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok();
    }
}

public sealed record SaveMyRestaurantPricingRequest(
    Guid ProgramId,
    Guid BundleId,
    decimal ReferencePrice26,
    decimal? ContractedSupplyPrice26 = null,
    string? ChangeRequestReason = null);

public sealed record AddRestaurantPricingOfferRequest(Guid ProgramId, Guid BundleId);
