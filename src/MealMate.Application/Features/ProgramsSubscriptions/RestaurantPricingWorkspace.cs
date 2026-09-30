using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record RestaurantPricingWorkspaceRowDto(
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    decimal? ReferencePrice26,
    decimal? ContractedSupplyPrice26,
    PriceLockStatus? LockStatus,
    DateTime? UpdatedAtUtc,
    bool IsAlreadyOffered = false);

public sealed record GetRestaurantPricingWorkspaceQuery(Guid RestaurantId)
    : IQuery<IReadOnlyList<RestaurantPricingWorkspaceRowDto>>;

public sealed class GetRestaurantPricingWorkspaceQueryHandler
    : IQueryHandler<GetRestaurantPricingWorkspaceQuery, IReadOnlyList<RestaurantPricingWorkspaceRowDto>>
{
    private readonly IAppDbContext _db;

    public GetRestaurantPricingWorkspaceQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<RestaurantPricingWorkspaceRowDto>>> Handle(
        GetRestaurantPricingWorkspaceQuery request,
        CancellationToken cancellationToken)
    {
        var offerings = await (
            from offer in _db.RestaurantProgramBundleOffers.AsNoTracking()
            join program in _db.Programs.AsNoTracking() on offer.ProgramId equals program.Id
            join bundle in _db.Bundles.AsNoTracking() on offer.BundleId equals bundle.Id
            where offer.RestaurantId == request.RestaurantId
            orderby program.SortOrder, bundle.SortOrder
            select new RestaurantPricingWorkspaceRowDto(
                offer.ProgramId,
                program.NameAr,
                program.NameEn,
                offer.BundleId,
                bundle.NameAr,
                bundle.NameEn,
                null,
                null,
                null,
                null,
                false)).ToListAsync(cancellationToken);

        // Legacy restaurants were created before onboarding persisted selected
        // offers. Let them see the active catalog as unpriced rows; saving a
        // price below turns that row into an explicit restaurant offer.
        if (offerings.Count == 0)
        {
            offerings = await (
                from program in _db.Programs.AsNoTracking()
                join bundle in _db.Bundles.AsNoTracking() on program.Id equals bundle.ProgramId
                where program.IsActive && bundle.IsActive
                orderby program.SortOrder, bundle.SortOrder
                select new RestaurantPricingWorkspaceRowDto(
                    program.Id,
                    program.NameAr,
                    program.NameEn,
                    bundle.Id,
                    bundle.NameAr,
                    bundle.NameEn,
                    null,
                    null,
                    null,
                    null,
                    false)).ToListAsync(cancellationToken);
        }

        var bundleIds = offerings.Select(x => x.BundleId).ToList();
        var pricings = await _db.RestaurantBundlePricings.AsNoTracking()
            .Where(x => x.RestaurantId == request.RestaurantId
                && bundleIds.Contains(x.BundleId)
                && x.EffectiveToUtc == null)
            .ToListAsync(cancellationToken);

        var byBundle = pricings
            .GroupBy(x => new { x.ProgramId, x.BundleId })
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var active = x.FirstOrDefault(p => RestaurantPricingActivation.IsCommerciallyActive(p.LockStatus));
                    var pending = x.FirstOrDefault(p => RestaurantPricingActivation.IsPendingChange(p.LockStatus)
                        || p.LockStatus == PriceLockStatus.Draft);
                    // Show pending edits when present; otherwise the commercially active row.
                    return pending ?? active ?? x.OrderByDescending(p => p.CreatedAtUtc).First();
                });

        IReadOnlyList<RestaurantPricingWorkspaceRowDto> rows = offerings.Select(offer =>
        {
            byBundle.TryGetValue(new { offer.ProgramId, offer.BundleId }, out var pricing);
            return offer with
            {
                ReferencePrice26 = pricing?.ReferencePrice26.Amount,
                ContractedSupplyPrice26 = pricing?.ContractedSupplyPrice26.Amount,
                LockStatus = pricing?.LockStatus,
                UpdatedAtUtc = pricing?.CreatedAtUtc,
            };
        }).ToList();

        return Result.Success(rows);
    }
}
