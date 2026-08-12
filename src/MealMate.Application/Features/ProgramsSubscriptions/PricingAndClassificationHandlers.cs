using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Validators
public sealed class SetRestaurantPricingCommandValidator : AbstractValidator<SetRestaurantPricingCommand>
{
    public SetRestaurantPricingCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.ProgramId).NotEmpty();
        RuleFor(x => x.BundleId).NotEmpty();
        RuleFor(x => x.ReferencePrice26).GreaterThan(0);
        RuleFor(x => x.ContractedSupplyPrice26).GreaterThan(0);
    }
}

public sealed class LockRestaurantPricingCommandValidator : AbstractValidator<LockRestaurantPricingCommand>
{
    public LockRestaurantPricingCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class RunClassificationCommandValidator : AbstractValidator<RunClassificationCommand>
{
    public RunClassificationCommandValidator()
    {
        RuleFor(x => x.ProgramId).NotEmpty();
        RuleFor(x => x.BundleId).NotEmpty();
    }
}

// Handlers
public sealed class PricingAndClassificationHandlers :
    ICommandHandler<SetRestaurantPricingCommand, RestaurantBundlePricingDto>,
    ICommandHandler<LockRestaurantPricingCommand, RestaurantBundlePricingDto>,
    IQueryHandler<GetRestaurantPricingQuery, RestaurantBundlePricingDto>,
    IQueryHandler<GetRestaurantPricingsListQuery, PagedResult<RestaurantBundlePricingDto>>,
    IQueryHandler<GetPendingRestaurantPricingChangesQuery, IReadOnlyList<RestaurantPricingChangeRequestDto>>,
    IQueryHandler<GetSubscriptionBundleTierPricingsQuery, IReadOnlyList<SubscriptionBundleTierPricingDto>>,
    IQueryHandler<GetCustomerSubscriptionPricingCatalogQuery, IReadOnlyList<CustomerSubscriptionCatalogItemDto>>,
    ICommandHandler<RunClassificationCommand, ClassificationRunDto>,
    IQueryHandler<GetLatestClassificationRunQuery, ClassificationRunDto>,
    IQueryHandler<PreviewCustomerPricingQuery, PricingEngineResult>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public PricingAndClassificationHandlers(IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<RestaurantBundlePricingDto>> Handle(SetRestaurantPricingCommand request, CancellationToken cancellationToken)
    {
        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        var settings = SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson);
        var bundleCap = settings.PerBundle.TryGetValue(request.BundleId, out var bundleProfile)
            ? bundleProfile.MaxRestaurantBoxPrice26
            : 0m;
        var manualCeiling = bundleCap > 0m ? bundleCap : settings.Global.MaxRestaurantBoxPrice26;

        // C04 auto-cap from peer reference prices in the same program+bundle (no admin intervention required).
        var peerPrices = await _db.RestaurantBundlePricings
            .AsNoTracking()
            .Where(x => x.ProgramId == request.ProgramId
                && x.BundleId == request.BundleId
                && x.RestaurantId != request.RestaurantId
                && x.EffectiveToUtc == null
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .Select(x => x.ReferencePrice26.Amount)
            .ToListAsync(cancellationToken);

        var autoCap = RestaurantReferencePriceGuard.Evaluate(peerPrices);
        var effectiveCeiling = RestaurantReferencePriceGuard.ResolveEffectiveCeiling(manualCeiling, autoCap);

        if (effectiveCeiling is decimal maxAllowed && request.ReferencePrice26 > maxAllowed)
        {
            var source = manualCeiling > 0m && autoCap.CanEnforce && maxAllowed == Math.Min(manualCeiling, autoCap.MaxReferencePrice26!.Value)
                ? "manual+statistical"
                : manualCeiling > 0m && (!autoCap.CanEnforce || maxAllowed == manualCeiling)
                    ? "manual"
                    : "statistical";

            return Error.Validation(
                "restaurant_pricing.maximum_box_price_exceeded",
                source == "manual"
                    ? $"The maximum allowed 26-day box price for this bundle is {maxAllowed:0.###} KWD."
                    : $"Reference price {request.ReferencePrice26:0.###} KWD exceeds the automatic market ceiling {maxAllowed:0.###} KWD for this program/bundle (C04 {autoCap.Method}, n={autoCap.SampleSize}).");
        }

        if (request.ContractedSupplyPrice26 > request.ReferencePrice26)
        {
            return Error.Validation(
                "restaurant_pricing.supply_above_reference",
                "Contracted supply price cannot exceed the reference price without an approved exception.");
        }

        var openVersions = await _db.RestaurantBundlePricings
            .Where(x => x.RestaurantId == request.RestaurantId
                && x.ProgramId == request.ProgramId
                && x.BundleId == request.BundleId
                && x.EffectiveToUtc == null)
            .OrderByDescending(x => x.EffectiveFromUtc)
            .ToListAsync(cancellationToken);

        var active = openVersions.FirstOrDefault(x => RestaurantPricingActivation.IsCommerciallyActive(x.LockStatus));
        var pending = openVersions.FirstOrDefault(x => RestaurantPricingActivation.IsPendingChange(x.LockStatus)
            || x.LockStatus == PriceLockStatus.Draft);
        var existing = pending ?? active ?? openVersions.FirstOrDefault();

        RestaurantBundlePricing newPricing;

        if (existing is not null && RestaurantPricingActivation.IsEditableDraft(existing.LockStatus))
        {
            // Edit draft / pending change in place. Locked commercial supply stays active.
            existing.ReferencePrice26 = new Money(request.ReferencePrice26, "KWD");
            existing.ContractedSupplyPrice26 = new Money(request.ContractedSupplyPrice26, "KWD");
            existing.ChangeRequestReason = request.ChangeRequestReason;
            if (active is not null && existing.Id != active.Id)
            {
                existing.LockStatus = string.IsNullOrWhiteSpace(request.ChangeRequestReason)
                    ? PriceLockStatus.PendingApproval
                    : PriceLockStatus.ChangeRequested;
                existing.PreviousPricingId ??= active.Id;
            }

            newPricing = existing;
        }
        else if (existing is not null && RestaurantPricingActivation.IsCommerciallyActive(existing.LockStatus))
        {
            // MM-PRC-AMD-01: locked supply cannot be edited directly — open a change request.
            newPricing = new RestaurantBundlePricing
            {
                RestaurantId = request.RestaurantId,
                ProgramId = request.ProgramId,
                BundleId = request.BundleId,
                ReferencePrice26 = new Money(request.ReferencePrice26, "KWD"),
                ContractedSupplyPrice26 = new Money(request.ContractedSupplyPrice26, "KWD"),
                LockStatus = string.IsNullOrWhiteSpace(request.ChangeRequestReason)
                    ? PriceLockStatus.PendingApproval
                    : PriceLockStatus.ChangeRequested,
                EffectiveFromUtc = _clock.UtcNow,
                EffectiveToUtc = null,
                ChangeRequestReason = request.ChangeRequestReason,
                PreviousPricingId = existing.Id,
            };

            _db.RestaurantBundlePricings.Add(newPricing);
        }
        else
        {
            newPricing = new RestaurantBundlePricing
            {
                RestaurantId = request.RestaurantId,
                ProgramId = request.ProgramId,
                BundleId = request.BundleId,
                ReferencePrice26 = new Money(request.ReferencePrice26, "KWD"),
                ContractedSupplyPrice26 = new Money(request.ContractedSupplyPrice26, "KWD"),
                LockStatus = PriceLockStatus.Draft,
                EffectiveFromUtc = _clock.UtcNow,
                EffectiveToUtc = null,
                ChangeRequestReason = request.ChangeRequestReason,
                PreviousPricingId = existing?.Id,
            };

            _db.RestaurantBundlePricings.Add(newPricing);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "RestaurantPricingSet",
            nameof(RestaurantBundlePricing),
            newPricing.Id,
            After: new { request.RestaurantId, request.ProgramId, request.BundleId, request.ReferencePrice26, request.ContractedSupplyPrice26 },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapPricingDto(newPricing);
    }

    public async Task<Result<RestaurantBundlePricingDto>> Handle(LockRestaurantPricingCommand request, CancellationToken cancellationToken)
    {
        var pricing = await _db.RestaurantBundlePricings.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (pricing is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Restaurant bundle pricing with ID '{request.Id}' was not found.");
        }

        var activating = RestaurantPricingActivation.IsCommerciallyActive(request.TargetStatus);
        if (activating)
        {
            // Supersede any other commercially-active version for the same offer.
            var siblings = await _db.RestaurantBundlePricings
                .Where(x => x.RestaurantId == pricing.RestaurantId
                    && x.ProgramId == pricing.ProgramId
                    && x.BundleId == pricing.BundleId
                    && x.EffectiveToUtc == null
                    && x.Id != pricing.Id)
                .ToListAsync(cancellationToken);

            foreach (var sibling in siblings.Where(x => RestaurantPricingActivation.IsCommerciallyActive(x.LockStatus)
                         || RestaurantPricingActivation.IsPendingChange(x.LockStatus)
                         || x.LockStatus == PriceLockStatus.Draft))
            {
                sibling.EffectiveToUtc = _clock.UtcNow;
                if (RestaurantPricingActivation.IsCommerciallyActive(sibling.LockStatus))
                {
                    sibling.LockStatus = PriceLockStatus.Superseded;
                }
            }

            pricing.EffectiveFromUtc = pricing.EffectiveFromUtc == default ? _clock.UtcNow : pricing.EffectiveFromUtc;
            pricing.EffectiveToUtc = null;
        }

        pricing.LockStatus = request.TargetStatus;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "RestaurantPricingLocked",
            nameof(RestaurantBundlePricing),
            pricing.Id,
            After: new { LockStatus = request.TargetStatus },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapPricingDto(pricing);
    }

    public async Task<Result<RestaurantBundlePricingDto>> Handle(GetRestaurantPricingQuery request, CancellationToken cancellationToken)
    {
        var pricing = await _db.RestaurantBundlePricings
            .Where(x => x.RestaurantId == request.RestaurantId && x.ProgramId == request.ProgramId && x.BundleId == request.BundleId && x.EffectiveToUtc == null)
            .OrderByDescending(x => x.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pricing is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "No active pricing record found for the specified restaurant, program, and bundle.");
        }

        return MapPricingDto(pricing);
    }

    public async Task<Result<PagedResult<RestaurantBundlePricingDto>>> Handle(GetRestaurantPricingsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.RestaurantBundlePricings.AsNoTracking().AsQueryable();

        if (request.RestaurantId.HasValue) query = query.Where(x => x.RestaurantId == request.RestaurantId.Value);
        if (request.ProgramId.HasValue) query = query.Where(x => x.ProgramId == request.ProgramId.Value);
        if (request.BundleId.HasValue) query = query.Where(x => x.BundleId == request.BundleId.Value);
        if (request.LockStatus.HasValue) query = query.Where(x => x.LockStatus == request.LockStatus.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var pageReq = new PageRequest { Page = request.Page, PageSize = request.PageSize };

        var items = await query
            .OrderByDescending(x => x.EffectiveFromUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(MapPricingDto).ToList();

        return new PagedResult<RestaurantBundlePricingDto>(dtos, totalCount, pageReq.NormalizedPage, pageReq.NormalizedPageSize);
    }

    public async Task<Result<IReadOnlyList<RestaurantPricingChangeRequestDto>>> Handle(
        GetPendingRestaurantPricingChangesQuery request,
        CancellationToken cancellationToken)
    {
        var pending = await _db.RestaurantBundlePricings
            .AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => x.EffectiveToUtc == null
                && (x.LockStatus == PriceLockStatus.PendingApproval
                    || x.LockStatus == PriceLockStatus.ChangeRequested))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return Array.Empty<RestaurantPricingChangeRequestDto>();

        var restaurantIds = pending.Select(x => x.RestaurantId).Distinct().ToList();
        var previousIds = pending
            .Where(x => x.PreviousPricingId.HasValue)
            .Select(x => x.PreviousPricingId!.Value)
            .Distinct()
            .ToList();

        var profiles = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(x => restaurantIds.Contains(x.Id))
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn, x.LegalNameAr, x.LegalNameEn })
            .ToListAsync(cancellationToken);
        var profileById = profiles.ToDictionary(x => x.Id);

        var previousById = previousIds.Count == 0
            ? new Dictionary<Guid, RestaurantBundlePricing>()
            : await _db.RestaurantBundlePricings
                .AsNoTracking()
                .Where(x => previousIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        IReadOnlyList<RestaurantPricingChangeRequestDto> rows = pending.Select(p =>
        {
            profileById.TryGetValue(p.RestaurantId, out var profile);
            previousById.TryGetValue(p.PreviousPricingId ?? Guid.Empty, out var previous);
            var nameAr = FirstNonEmpty(profile?.TradeNameAr, profile?.LegalNameAr) ?? string.Empty;
            var nameEn = FirstNonEmpty(profile?.TradeNameEn, profile?.LegalNameEn) ?? string.Empty;
            return new RestaurantPricingChangeRequestDto(
                p.Id,
                p.RestaurantId,
                nameAr,
                nameEn,
                p.ProgramId,
                p.Program.NameAr,
                p.Program.NameEn,
                p.BundleId,
                p.Bundle.NameAr,
                p.Bundle.NameEn,
                p.ReferencePrice26.Amount,
                p.ContractedSupplyPrice26.Amount,
                previous?.ReferencePrice26.Amount,
                previous?.ContractedSupplyPrice26.Amount,
                p.LockStatus,
                p.ChangeRequestReason,
                p.EffectiveFromUtc,
                p.CreatedAtUtc);
        }).ToList();

        return Result.Success(rows);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    public async Task<Result<IReadOnlyList<SubscriptionBundleTierPricingDto>>> Handle(
        GetSubscriptionBundleTierPricingsQuery request,
        CancellationToken cancellationToken)
    {
        var prices = await _db.RestaurantBundlePricings
            .AsNoTracking()
            .Where(x => x.EffectiveToUtc == null
                && x.Program.IsActive
                && x.Bundle.IsActive
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .Select(x => new
            {
                x.RestaurantId,
                x.ProgramId,
                ProgramNameAr = x.Program.NameAr,
                ProgramNameEn = x.Program.NameEn,
                x.BundleId,
                BundleNameAr = x.Bundle.NameAr,
                BundleNameEn = x.Bundle.NameEn,
                Price26 = x.ReferencePrice26.Amount,
            })
            .ToListAsync(cancellationToken);

        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        var settings = SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson);
        var result = new List<SubscriptionBundleTierPricingDto>();

        foreach (var bundleGroup in prices.GroupBy(x => new
                 {
                     x.ProgramId,
                     x.ProgramNameAr,
                     x.ProgramNameEn,
                     x.BundleId,
                     x.BundleNameAr,
                     x.BundleNameEn,
                 }))
        {
            var profile = settings.Mode == "per_bundle"
                && settings.PerBundle.TryGetValue(bundleGroup.Key.BundleId, out var bundleProfile)
                    ? bundleProfile
                    : settings.Global;

            var tierAverages = SubscriptionPackagePricingCalculator.CalculateTierAverages(
                bundleGroup.Select(row => row.Price26));

            foreach (var tierAverage in tierAverages)
            {
                var price6 = CalculateCustomerPrice(tierAverage.AverageDailyPriceKwd, 6, profile);
                var price12 = CalculateCustomerPrice(tierAverage.AverageDailyPriceKwd, 12, profile);
                var price26 = CalculateCustomerPrice(tierAverage.AverageDailyPriceKwd, 26, profile);

                result.Add(new SubscriptionBundleTierPricingDto(
                    bundleGroup.Key.ProgramId,
                    bundleGroup.Key.ProgramNameAr,
                    bundleGroup.Key.ProgramNameEn,
                    bundleGroup.Key.BundleId,
                    bundleGroup.Key.BundleNameAr,
                    bundleGroup.Key.BundleNameEn,
                    tierAverage.Tier,
                    tierAverage.RestaurantCount,
                    tierAverage.AveragePrice26DaysKwd,
                    tierAverage.AverageDailyPriceKwd,
                    price6,
                    price12,
                    price26,
                    profile.ResolveRate(6),
                    profile.ResolveRate(12),
                    profile.ResolveRate(26)));
            }
        }

        return result
            .OrderBy(x => x.ProgramNameEn)
            .ThenBy(x => x.BundleNameEn)
            .ThenBy(x => x.Tier)
            .ToList();
    }

    public async Task<Result<IReadOnlyList<CustomerSubscriptionCatalogItemDto>>> Handle(
        GetCustomerSubscriptionPricingCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var tierPricingResult = await Handle(
            new GetSubscriptionBundleTierPricingsQuery(),
            cancellationToken);

        if (tierPricingResult.IsFailure)
        {
            return tierPricingResult.Error;
        }

        var settingsRow = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive,
                cancellationToken);
        var tierLabels = SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson).TierLabels;

        return tierPricingResult.Value
            .Select(item => new CustomerSubscriptionCatalogItemDto(
                item.ProgramId,
                item.ProgramNameAr,
                item.ProgramNameEn,
                item.BundleId,
                item.BundleNameAr,
                item.BundleNameEn,
                item.Tier,
                item.Tier switch
                {
                    RestaurantTier.Basic => tierLabels.BasicAr,
                    RestaurantTier.Platinum => tierLabels.PlatinumAr,
                    _ => tierLabels.EliteAr,
                },
                item.Tier switch
                {
                    RestaurantTier.Basic => tierLabels.BasicEn,
                    RestaurantTier.Platinum => tierLabels.PlatinumEn,
                    _ => tierLabels.EliteEn,
                },
                [
                    CreateDurationPrice(6, item.CustomerPrice6DaysKwd),
                    CreateDurationPrice(12, item.CustomerPrice12DaysKwd),
                    CreateDurationPrice(26, item.CustomerPrice26DaysKwd),
                ]))
            .ToList();
    }

    public async Task<Result<ClassificationRunDto>> Handle(RunClassificationCommand request, CancellationToken cancellationToken)
    {
        var activePricings = await _db.RestaurantBundlePricings
            .Where(x => x.ProgramId == request.ProgramId
                && x.BundleId == request.BundleId
                && x.EffectiveToUtc == null
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .ToListAsync(cancellationToken);

        if (!activePricings.Any())
        {
            return Error.Validation(MessageCodes.Validation.Required, "Cannot run classification without active locked restaurant prices for this program & bundle.");
        }

        // Calculate daily reference box price per restaurant (ReferencePrice26 / 26)
        var dailyBoxes = activePricings.Select(p => p.ReferencePrice26.Amount / 26m).ToList();
        var meanA_t = dailyBoxes.Average();
        var variance = dailyBoxes.Sum(x => (x - meanA_t) * (x - meanA_t)) / dailyBoxes.Count;
        var stdDev = (decimal)Math.Sqrt((double)variance);

        var lowerBound = meanA_t - (2m * stdDev);
        var upperBound = meanA_t + (2m * stdDev);

        var run = new ClassificationRun
        {
            ProgramId = request.ProgramId,
            BundleId = request.BundleId,
            MeanReferenceDaily = meanA_t,
            SdReferenceDaily = stdDev,
            LowerBound = lowerBound,
            UpperBound = upperBound,
            ConfidenceLevel = "95%",
            RunAtUtc = _clock.UtcNow,
        };

        _db.ClassificationRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        var classifications = new List<RestaurantClassification>();

        foreach (var p in activePricings)
        {
            var refDaily = p.ReferencePrice26.Amount / 26m;
            var isOutlier = refDaily < lowerBound || refDaily > upperBound;

            // Tier assignment logic
            RestaurantTier tier;
            if (refDaily < (meanA_t - 0.5m * stdDev))
            {
                tier = RestaurantTier.Basic;
            }
            else if (refDaily > (meanA_t + 0.5m * stdDev))
            {
                tier = RestaurantTier.Elite;
            }
            else
            {
                tier = RestaurantTier.Platinum;
            }

            var item = new RestaurantClassification
            {
                ClassificationRunId = run.Id,
                RestaurantId = p.RestaurantId,
                ProgramId = p.ProgramId,
                BundleId = p.BundleId,
                Tier = tier,
                ReferenceDailyBox = refDaily,
                IsOutlierFlagged = isOutlier,
                OutlierReason = isOutlier ? $"Daily reference price {refDaily:F3} KWD is outside 2-sigma bounds [{lowerBound:F3}, {upperBound:F3}]" : null,
            };

            classifications.Add(item);
            _db.RestaurantClassifications.Add(item);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "ClassificationRunExecuted",
            nameof(ClassificationRun),
            run.Id,
            After: new { run.ProgramId, run.BundleId, run.MeanReferenceDaily, RestaurantCount = classifications.Count },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapClassificationRunDto(run, classifications);
    }

    public async Task<Result<ClassificationRunDto>> Handle(GetLatestClassificationRunQuery request, CancellationToken cancellationToken)
    {
        var run = await _db.ClassificationRuns
            .Include(x => x.Classifications)
            .Where(x => x.ProgramId == request.ProgramId && x.BundleId == request.BundleId)
            .OrderByDescending(x => x.RunAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "No classification run found for this program and bundle.");
        }

        return MapClassificationRunDto(run, run.Classifications.ToList());
    }

    public async Task<Result<PricingEngineResult>> Handle(PreviewCustomerPricingQuery request, CancellationToken cancellationToken)
    {
        var activePrices = await _db.RestaurantBundlePricings
            .AsNoTracking()
            .Where(x => x.ProgramId == request.ProgramId
                && x.BundleId == request.BundleId
                && x.EffectiveToUtc == null
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .Select(x => x.ReferencePrice26.Amount)
            .ToListAsync(cancellationToken);
        var meanA_t = SubscriptionPackagePricingCalculator.ResolveAverageDailyPrice(
            activePrices,
            request.Tier);

        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        var settings = SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson);
        var profile = settings.Mode == "per_bundle" && settings.PerBundle.TryGetValue(request.BundleId, out var customProfile)
            ? customProfile
            : settings.Global;
        var durationRate = profile.ResolveRate(request.DurationDays);

        var pricingReq = new PricingEngineRequest(
            MeanReferenceDailyPrice: meanA_t,
            DurationDays: request.DurationDays,
            CampaignDiscountKwd: request.CampaignDiscountKwd,
            PromoCodeDiscountKwd: request.PromoCodeDiscountKwd,
            DurationRatesPct: new Dictionary<int, decimal> { [request.DurationDays] = durationRate },
            FridayDays: request.IncludeFriday ? Math.Max(0, request.FridayDays) : 0,
            FridayRatePct: profile.FridayRate);

        var result = PricingEngine.Calculate(pricingReq);
        return result;
    }

    private static CustomerSubscriptionDurationPriceDto CreateDurationPrice(int durationDays, decimal totalPriceKwd)
        => new(durationDays, totalPriceKwd, Math.Round(totalPriceKwd / durationDays, 3));

    private static decimal CalculateCustomerPrice(decimal averageDailyPrice, int durationDays, PricingProfile profile)
    {
        var result = PricingEngine.Calculate(new PricingEngineRequest(
            MeanReferenceDailyPrice: averageDailyPrice,
            DurationDays: durationDays,
            DurationRatesPct: new Dictionary<int, decimal>
            {
                [durationDays] = profile.ResolveRate(durationDays),
            }));

        return result.FinalCustomerPrice;
    }

    private static RestaurantBundlePricingDto MapPricingDto(RestaurantBundlePricing p) => new(
        p.Id,
        p.RestaurantId,
        p.ProgramId,
        p.BundleId,
        p.ReferencePrice26.Amount,
        p.ContractedSupplyPrice26.Amount,
        p.LockStatus,
        p.EffectiveFromUtc,
        p.EffectiveToUtc,
        p.ChangeRequestReason,
        p.PreviousPricingId,
        p.CreatedAtUtc);

    private static ClassificationRunDto MapClassificationRunDto(ClassificationRun run, IReadOnlyList<RestaurantClassification> items) => new(
        run.Id,
        run.ProgramId,
        run.BundleId,
        run.MeanReferenceDaily,
        run.SdReferenceDaily,
        run.LowerBound,
        run.UpperBound,
        run.ConfidenceLevel,
        run.RunAtUtc,
        items.Select(c => new RestaurantClassificationDto(
            c.Id,
            c.ClassificationRunId,
            c.RestaurantId,
            c.ProgramId,
            c.BundleId,
            c.Tier,
            c.ReferenceDailyBox,
            c.IsOutlierFlagged,
            c.OutlierReason)).ToList());
}
