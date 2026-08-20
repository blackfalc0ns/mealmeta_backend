using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public interface IFamilyPlanEligibilityService
{
    Task<Result> ValidateAsync(
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        int durationDays,
        bool includeFriday,
        CancellationToken cancellationToken = default);

    Task<Result> ValidateProgramForFamilyAsync(Guid programId, CancellationToken cancellationToken = default);

    Task<Result> ValidateBundleForFamilyAsync(Guid programId, Guid bundleId, CancellationToken cancellationToken = default);

    Task<Result> ValidateDurationAsync(int durationDays, bool includeFriday, CancellationToken cancellationToken = default);

    Task<Result> ValidateTierInCatalogAsync(
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        CancellationToken cancellationToken = default);
}

public sealed class FamilyPlanEligibilityService : IFamilyPlanEligibilityService
{
    private readonly IAppDbContext _db;
    private readonly IAllowedSubscriptionDurationService _durations;
    private readonly IRestaurantTierClassificationService _tiers;
    private readonly ISender _sender;

    public FamilyPlanEligibilityService(
        IAppDbContext db,
        IAllowedSubscriptionDurationService durations,
        IRestaurantTierClassificationService tiers,
        ISender sender)
        => (_db, _durations, _tiers, _sender) = (db, durations, tiers, sender);

    public async Task<Result> ValidateAsync(
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        int durationDays,
        bool includeFriday,
        CancellationToken cancellationToken = default)
    {
        var bundleResult = await ValidateBundleForFamilyAsync(programId, bundleId, cancellationToken);
        if (bundleResult.IsFailure) return bundleResult.Error;

        var durationResult = await ValidateDurationAsync(durationDays, includeFriday, cancellationToken);
        if (durationResult.IsFailure) return durationResult.Error;

        var tierResult = await ValidateTierInCatalogAsync(programId, bundleId, tier, cancellationToken);
        if (tierResult.IsFailure) return tierResult.Error;

        if (includeFriday)
        {
            var fridayCount = await CountFridayRestaurantsAsync(programId, bundleId, tier, cancellationToken);
            if (fridayCount <= 0)
                return Error.Validation("family.friday_unavailable", "Friday is unavailable because no restaurant in this tier operates on Friday.");
        }

        return Result.Success();
    }

    public async Task<Result> ValidateProgramForFamilyAsync(Guid programId, CancellationToken cancellationToken = default)
    {
        var program = await _db.Programs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == programId, cancellationToken);
        if (program is null)
            return Error.NotFound("family.program_not_found", "Program was not found.");
        if (!program.IsActive || program.Status != ProgramStatus.Active || !program.AvailableForFamily)
            return Error.Validation("family.catalog_not_available", "Selected program is not available for family subscriptions.");
        return Result.Success();
    }

    public async Task<Result> ValidateBundleForFamilyAsync(
        Guid programId,
        Guid bundleId,
        CancellationToken cancellationToken = default)
    {
        var bundleOk = await _db.Bundles.AsNoTracking().AnyAsync(
            x => x.Id == bundleId
                && x.ProgramId == programId
                && x.IsActive
                && x.Status == ProgramStatus.Active
                && x.AvailableForFamily
                && x.Program.IsActive
                && x.Program.Status == ProgramStatus.Active
                && x.Program.AvailableForFamily,
            cancellationToken);
        if (!bundleOk)
        {
            var exists = await _db.Bundles.AsNoTracking().AnyAsync(
                x => x.Id == bundleId && x.ProgramId == programId, cancellationToken);
            if (!exists)
                return Error.NotFound("family.bundle_not_found", "Bundle was not found for the selected program.");
            return Error.Validation("family.catalog_not_available", "Selected program or bundle is not available for family subscriptions.");
        }

        return Result.Success();
    }

    public async Task<Result> ValidateDurationAsync(
        int durationDays,
        bool includeFriday,
        CancellationToken cancellationToken = default)
    {
        var allowed = await _durations.EvaluateAsync(durationDays, includeFriday, cancellationToken);
        if (!allowed.IsAllowed)
        {
            var code = string.Equals(allowed.ErrorCode, "subscription_flow.friday_unavailable", StringComparison.Ordinal)
                ? "family.friday_unavailable"
                : "family.duration_not_available";
            return Error.Validation(code, allowed.ErrorMessage ?? "The selected duration is not available.");
        }

        return Result.Success();
    }

    public async Task<Result> ValidateTierInCatalogAsync(
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        CancellationToken cancellationToken = default)
    {
        var catalog = await _sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), cancellationToken);
        if (catalog.IsFailure)
            return catalog.Error;

        var inCatalog = catalog.Value.Any(x =>
            x.ProgramId == programId && x.BundleId == bundleId && x.Tier == tier);
        if (!inCatalog)
            return Error.Validation("family.catalog_unavailable", "Selected program, bundle, and tier are not available.");

        return Result.Success();
    }

    /// <summary>
    /// Same own-tier Friday restaurant count logic as
    /// <c>CustomerSubscriptionFlowController.ResolveFridayAvailabilityAsync</c>.
    /// </summary>
    private async Task<int> CountFridayRestaurantsAsync(
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        CancellationToken cancellationToken)
    {
        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, cancellationToken);
        var classifiedIds = classification.Assignments
            .Where(x => x.Tier == tier)
            .Select(x => x.RestaurantId)
            .ToList();
        if (classifiedIds.Count == 0)
            return 0;

        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking()
            .Where(x => classifiedIds.Contains(x.RestaurantId))
            .ToListAsync(cancellationToken);

        return classifiedIds.Count(id =>
            schedules.Any(x => x.RestaurantId == id && x.DayOfWeek == DayOfWeek.Friday && x.IsWorkingDay));
    }
}
