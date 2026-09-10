using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.ProgramsSubscriptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

public sealed class RestaurantPricingWorkspaceService : IRestaurantPricingWorkspaceService
{
    private readonly IAppDbContext _db;
    private readonly ISender _sender;

    public RestaurantPricingWorkspaceService(IAppDbContext db, ISender sender)
    {
        _db = db;
        _sender = sender;
    }

    public Task<Result<IReadOnlyList<RestaurantPricingWorkspaceRowDto>>> GetWorkspaceAsync(Guid restaurantId, CancellationToken ct = default)
        => _sender.Send(new GetRestaurantPricingWorkspaceQuery(restaurantId), ct);

    public async Task<Result<RestaurantBundlePricingDto>> SavePricingAsync(
        Guid restaurantId,
        SaveMyRestaurantPricingRequest body,
        CancellationToken ct = default)
    {
        var isOffered = await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
            x => x.RestaurantId == restaurantId
                && x.ProgramId == body.ProgramId
                && x.BundleId == body.BundleId
                && x.IsActive,
            ct);

        if (!isOffered)
        {
            var isActiveCatalogBundle = await _db.Bundles.AsNoTracking().AnyAsync(
                x => x.Id == body.BundleId && x.ProgramId == body.ProgramId && x.IsActive,
                ct);
            if (!isActiveCatalogBundle)
                return Error.Forbidden("restaurant_pricing.offer_not_available", "This bundle is not available.");

            _db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                RestaurantId = restaurantId,
                ProgramId = body.ProgramId,
                BundleId = body.BundleId,
                IsActive = true,
            });
            await _db.SaveChangesAsync(ct);
        }

        return await _sender.Send(new SetRestaurantPricingCommand(
            restaurantId,
            body.ProgramId,
            body.BundleId,
            body.ReferencePrice26,
            body.ContractedSupplyPrice26 ?? body.ReferencePrice26,
            body.ChangeRequestReason), ct);
    }

    public async Task<Result<IReadOnlyList<RestaurantPricingWorkspaceRowDto>>> GetAvailableCatalogAsync(
        Guid restaurantId,
        CancellationToken ct = default)
    {
        var offeredBundleIds = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive)
            .Select(x => x.BundleId)
            .ToListAsync(ct);

        var offeredBundleIdSet = offeredBundleIds.ToHashSet();

        var rows = await (
            from program in _db.Programs.AsNoTracking()
            join bundle in _db.Bundles.AsNoTracking() on program.Id equals bundle.ProgramId
            where program.IsActive && bundle.IsActive
            orderby program.SortOrder, bundle.SortOrder
            select new RestaurantPricingWorkspaceRowDto(
                program.Id, program.NameAr, program.NameEn,
                bundle.Id, bundle.NameAr, bundle.NameEn,
                null, null, null, null, false)).ToListAsync(ct);

        IReadOnlyList<RestaurantPricingWorkspaceRowDto> result = rows
            .Select(row => row with { IsAlreadyOffered = offeredBundleIdSet.Contains(row.BundleId) })
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result> AddOfferAsync(
        Guid restaurantId,
        AddRestaurantPricingOfferRequest body,
        CancellationToken ct = default)
    {
        var current = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive)
            .Select(x => new RestaurantOfferingDto(x.ProgramId, x.BundleId, true))
            .ToListAsync(ct);

        if (current.All(x => x.BundleId != body.BundleId))
            current.Add(new RestaurantOfferingDto(body.ProgramId, body.BundleId, true));

        return await _sender.Send(new SaveRestaurantOfferingsCommand(restaurantId, current), ct);
    }
}
