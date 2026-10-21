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
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;

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
    private readonly IRestaurantTierClassificationService _classification;
    private readonly IOfferEligibilityEngine? _offerEngine;

    public PricingAndClassificationHandlers(
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IRestaurantTierClassificationService classification,
        IOfferEligibilityEngine? offerEngine = null)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _classification = classification;
        _offerEngine = offerEngine;
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
        // Activation must use the row-versioned maker/checker publication service.
        if (RestaurantPricingActivation.IsCommerciallyActive(request.TargetStatus))
            return Error.Validation("pricing.publication_use_workflow");

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

        if (activating)
        {
            await PersistClassificationRunAsync(pricing.ProgramId, pricing.BundleId, cancellationToken);
        }

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

    public Task<Result<IReadOnlyList<SubscriptionBundleTierPricingDto>>> Handle(
        GetSubscriptionBundleTierPricingsQuery request,
        CancellationToken cancellationToken)
        => BuildTierPricingsAsync(eligibleOnly: false, cancellationToken);

    /// <summary>
    /// Tier averages for a program/bundle. <paramref name="eligibleOnly"/> narrows the
    /// population to restaurants a customer can actually buy from, so customer prices and
    /// the customer restaurant list are always derived from the same restaurants.
    /// </summary>
    private async Task<Result<IReadOnlyList<SubscriptionBundleTierPricingDto>>> BuildTierPricingsAsync(
        bool eligibleOnly,
        CancellationToken cancellationToken)
    {
        var query = _db.RestaurantBundlePricings
            .AsNoTracking()
            .Where(x => x.EffectiveFromUtc <= _clock.UtcNow
                && (x.EffectiveToUtc == null || x.EffectiveToUtc > _clock.UtcNow)
                && x.Program.IsActive
                && x.Bundle.IsActive
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate));

        if (eligibleOnly)
        {
            query = query.Where(x => _db.RestaurantProgramBundleOffers
                    .Any(offer => offer.RestaurantId == x.RestaurantId
                        && offer.ProgramId == x.ProgramId
                        && offer.BundleId == x.BundleId
                        && offer.IsActive)
                && _db.RestaurantProfiles
                    .Any(profile => profile.Id == x.RestaurantId
                        && profile.VerificationStatus == VerificationStatus.Approved
                        && profile.IsOperationallyActive));
        }

        var prices = (await query
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
                x.EffectiveFromUtc,
            })
            .ToListAsync(cancellationToken))
            .GroupBy(x => new { x.RestaurantId, x.ProgramId, x.BundleId })
            .Select(group => group.OrderByDescending(x => x.EffectiveFromUtc).First())
            .ToList();

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
        var tierPricingResult = await BuildTierPricingsAsync(eligibleOnly: true, cancellationToken);

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
        var resolved = await _classification.ResolveAsync(request.ProgramId, request.BundleId, eligibleOnly: false, cancellationToken);

        if (resolved.Assignments.Count == 0)
        {
            return Error.Validation(MessageCodes.Validation.Required, "Cannot run classification without active locked restaurant prices for this program & bundle.");
        }

        var run = new ClassificationRun
        {
            ProgramId = request.ProgramId,
            BundleId = request.BundleId,
            MeanReferenceDaily = resolved.MeanReferenceDaily,
            SdReferenceDaily = resolved.SdReferenceDaily,
            LowerBound = resolved.LowerBound,
            UpperBound = resolved.UpperBound,
            ConfidenceLevel = "95%",
            RunAtUtc = _clock.UtcNow,
        };

        _db.ClassificationRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        var classifications = resolved.Assignments.Select(a => new RestaurantClassification
        {
            ClassificationRunId = run.Id,
            RestaurantId = a.RestaurantId,
            ProgramId = request.ProgramId,
            BundleId = request.BundleId,
            Tier = a.Tier,
            ReferenceDailyBox = a.ReferenceDailyBox,
            IsOutlierFlagged = a.IsOutlierFlagged,
            OutlierReason = a.OutlierReason,
        }).ToList();

        foreach (var item in classifications)
        {
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

    private async Task PersistClassificationRunAsync(Guid programId, Guid bundleId, CancellationToken cancellationToken)
    {
        var resolved = await _classification.ResolveAsync(programId, bundleId, eligibleOnly: false, cancellationToken);
        if (resolved.Assignments.Count == 0)
        {
            return;
        }

        var run = new ClassificationRun
        {
            ProgramId = programId,
            BundleId = bundleId,
            MeanReferenceDaily = resolved.MeanReferenceDaily,
            SdReferenceDaily = resolved.SdReferenceDaily,
            LowerBound = resolved.LowerBound,
            UpperBound = resolved.UpperBound,
            ConfidenceLevel = "95%",
            RunAtUtc = _clock.UtcNow,
        };

        _db.ClassificationRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var assignment in resolved.Assignments)
        {
            _db.RestaurantClassifications.Add(new RestaurantClassification
            {
                ClassificationRunId = run.Id,
                RestaurantId = assignment.RestaurantId,
                ProgramId = programId,
                BundleId = bundleId,
                Tier = assignment.Tier,
                ReferenceDailyBox = assignment.ReferenceDailyBox,
                IsOutlierFlagged = assignment.IsOutlierFlagged,
                OutlierReason = assignment.OutlierReason,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "ClassificationRunAutoPersisted",
            nameof(ClassificationRun),
            run.Id,
            After: new { run.ProgramId, run.BundleId, run.MeanReferenceDaily, RestaurantCount = resolved.Assignments.Count },
            SourceChannel: "admin-dashboard"), cancellationToken);
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
                && x.EffectiveFromUtc <= _clock.UtcNow
                && (x.EffectiveToUtc == null || x.EffectiveToUtc > _clock.UtcNow)
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .Select(x => x.ReferencePrice26.Amount)
            .ToListAsync(cancellationToken);
        if (activePrices.Count == 0)
            return Error.Validation("pricing.unavailable", "No active restaurant pricing data is available for this program and bundle.");
        if (!await _db.PricingRuleVersions.AsNoTracking().AnyAsync(x => x.IsActive, cancellationToken))
            return Error.Validation("pricing.rules_unavailable", "No active pricing rule version is available.");
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

        var undiscountedPrice = PricingEngine.Calculate(new PricingEngineRequest(
            MeanReferenceDailyPrice: meanA_t,
            DurationDays: request.DurationDays,
            DurationRatesPct: new Dictionary<int, decimal> { [request.DurationDays] = durationRate },
            FridayDays: request.IncludeFriday ? Math.Max(0, request.FridayDays) : 0,
            FridayRatePct: profile.FridayRate));
        // Exact-offer cards, summary and checkout evaluate the advertised percentage on
        // the package subtotal. Gateway recovery and final rounding follow the discount.
        // Keep the older code-only preview basis for clients outside the direct-offer flow.
        var grossCartAmount = request.OfferId.HasValue
            ? undiscountedPrice.UnroundedCorePrice
            : undiscountedPrice.FinalCustomerPrice;

        var effectiveCampaignDiscount = request.CampaignDiscountKwd;
        var effectivePromoCodeDiscount = request.PromoCodeDiscountKwd;
        var hasCanonicalAdminCode = !string.IsNullOrWhiteSpace(request.PromoCode)
            && await _db.PromoCodes.AsNoTracking().AnyAsync(x => x.Code.ToUpper() == request.PromoCode.Trim().ToUpper(), cancellationToken);

        if ((request.OfferId.HasValue || (!string.IsNullOrWhiteSpace(request.PromoCode) && !hasCanonicalAdminCode)) && _offerEngine != null)
        {
            Offer? offer = null;
            if (request.OfferId.HasValue)
            {
                offer = await _db.Offers
                    .AsNoTracking()
                    .Include(o => o.EligiblePrograms)
                    .Include(o => o.EligibleBundles)
                    .Include(o => o.PackageTargets)
                    .Include(o => o.EligibleRestaurants)
                    .FirstOrDefaultAsync(o => o.Id == request.OfferId.Value, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(request.PromoCode))
            {
                var codeNormalized = request.PromoCode.Trim().ToUpper();
                offer = await _db.Offers
                    .AsNoTracking()
                    .Include(o => o.EligiblePrograms)
                    .Include(o => o.EligibleBundles)
                    .Include(o => o.PackageTargets)
                    .Include(o => o.EligibleRestaurants)
                    .FirstOrDefaultAsync(o => o.PromoCode != null && o.PromoCode.ToUpper() == codeNormalized, cancellationToken);
            }

            if (offer != null)
            {
                var customerId = request.CustomerId ?? Guid.Empty;
                var priorSubsCount = customerId != Guid.Empty
                    ? await _db.Subscriptions.CountAsync(s => s.CustomerUserId == customerId, cancellationToken)
                    : 0;
                var customerRedemptions = customerId != Guid.Empty
                    ? await _db.OfferRedemptions.CountAsync(r => r.OfferId == offer.Id && r.CustomerId == customerId, cancellationToken)
                    : 0;

                var evalCtx = new OfferEligibilityContext(
                    CustomerId: customerId,
                    PriorSubscriptionCount: priorSubsCount,
                    CustomerOfferRedemptionCount: customerRedemptions,
                    CartAmount: grossCartAmount,
                    DurationDays: request.DurationDays,
                    DeliveryGovernorate: null,
                    ProgramId: request.ProgramId,
                    BundleId: request.BundleId,
                    RestaurantId: null,
                    HasPromoCode: !string.IsNullOrWhiteSpace(request.PromoCode) || request.PromoCodeDiscountKwd > 0,
                    Tier: request.Tier,
                    SubscriptionType: request.CustomerSubscriptionType,
                    IsOfferPromoCode: offer.PromoCode is not null
                        && string.Equals(offer.PromoCode, request.PromoCode, StringComparison.OrdinalIgnoreCase));

                var eval = _offerEngine.Evaluate(offer, evalCtx, _clock.UtcNow);
                if (eval.IsEligible)
                {
                    effectiveCampaignDiscount = eval.CalculatedDiscountAmount;

                    // Stacking enforcement: disallow promo code if stacking not allowed
                    if (!offer.AllowPromoCodeStacking)
                    {
                        effectivePromoCodeDiscount = 0m;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            var code = request.PromoCode.Trim().ToUpperInvariant();
            var promo = await _db.PromoCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Code.ToUpper() == code, cancellationToken);
            if (promo is not null)
            {
                var now = _clock.UtcNow;
                var target = ReadPromoTargeting(promo.TargetingJson);
                var durationAllowed = string.IsNullOrWhiteSpace(promo.EligibleDurationDaysJson)
                    || IsDurationEligible(promo.EligibleDurationDaysJson, request.DurationDays);
                var customerUses = request.CustomerId is { } customerId
                    ? await _db.PromoCodeRedemptions.CountAsync(x => x.PromoCodeId == promo.Id && x.CustomerId == customerId
                        && x.Status != PromoCodeRedemptionStatus.Released
                        && x.SubscriptionId != request.ExcludeSubscriptionId, cancellationToken)
                    : 0;
                var ownHold = request.ExcludeSubscriptionId != Guid.Empty && await _db.PromoCodeRedemptions.AnyAsync(x =>
                    x.PromoCodeId == promo.Id && x.SubscriptionId == request.ExcludeSubscriptionId
                    && x.Status == PromoCodeRedemptionStatus.Held, cancellationToken);
                var eligible = promo.IsActive && promo.StartsAtUtc <= now && (promo.EndsAtUtc is null || promo.EndsAtUtc >= now)
                    && (promo.MaxUses is null || promo.UsedCount - (ownHold ? 1 : 0) < promo.MaxUses)
                    && (promo.PerCustomerLimit is null || customerUses < promo.PerCustomerLimit)
                    && durationAllowed && target is not null
                    && (!target.CustomerUserId.HasValue || target.CustomerUserId == request.CustomerId)
                    && (!target.ProgramId.HasValue || target.ProgramId == request.ProgramId)
                    && (!target.BundleId.HasValue || target.BundleId == request.BundleId)
                    && (string.IsNullOrWhiteSpace(target.SubscriptionType)
                        || string.Equals(target.SubscriptionType, request.CustomerSubscriptionType, StringComparison.Ordinal));
                if (eligible)
                {
                    var discount = promo.DiscountAmount?.Amount
                        ?? Math.Round(grossCartAmount * (promo.DiscountPercent?.Value ?? 0m), 3, MidpointRounding.AwayFromZero);
                    discount = Math.Min(grossCartAmount, Math.Max(0m, discount));
                    effectiveCampaignDiscount = Math.Max(0m, effectiveCampaignDiscount);
                    effectivePromoCodeDiscount = Math.Max(0m, effectivePromoCodeDiscount);
                    // Campaigns and coupon codes do not stack; retain the larger eligible saving.
                    if (discount > effectiveCampaignDiscount + effectivePromoCodeDiscount)
                    {
                        effectiveCampaignDiscount = 0m;
                        effectivePromoCodeDiscount = discount;
                    }
                }
            }
            else
            {
                // Referral codes are the third source: the reward percentage occupies the promo
                // discount slot under the same keep-the-larger-saving policy. The coupon chain
                // (summary and checkout) owns the status verdicts — here we only price.
                var referralCode = await _db.ReferralCodes.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Code.ToUpper() == request.PromoCode.Trim().ToUpper(), cancellationToken);
                if (referralCode is not null && referralCode.IsActive
                    && referralCode.RewardPercent is not null
                    && (!(referralCode.MaxUses is { } maxUses) || referralCode.UsedCount < maxUses))
                {
                    var referralDiscount = Math.Min(grossCartAmount,
                        Math.Round(grossCartAmount * referralCode.RewardPercent.Value, 3, MidpointRounding.AwayFromZero));
                    // Already-attributed customers lose here too: only customers without an
                    // attribution row get the referral price.
                    var alreadyAttributed = request.CustomerId is { } referralCustomerId
                        && await _db.ReferralAttributions.AsNoTracking().AnyAsync(x =>
                            x.ReferredCustomerUserId == referralCustomerId
                            && x.RewardSubscriptionId != request.ExcludeSubscriptionId, cancellationToken);
                    if (referralDiscount > 0m && !alreadyAttributed)
                    {
                        effectiveCampaignDiscount = Math.Max(0m, effectiveCampaignDiscount);
                        effectivePromoCodeDiscount = Math.Max(0m, effectivePromoCodeDiscount);
                        if (referralDiscount > effectiveCampaignDiscount + effectivePromoCodeDiscount)
                        {
                            effectiveCampaignDiscount = 0m;
                            effectivePromoCodeDiscount = referralDiscount;
                        }
                    }
                }
            }
        }

        // A campaign and a coupon are alternative savings. Keep only the larger eligible
        // one so preview, checkout and the eventual payment amount use the same source.
        if (effectiveCampaignDiscount > 0m && effectivePromoCodeDiscount > 0m)
        {
            if (effectivePromoCodeDiscount > effectiveCampaignDiscount) effectiveCampaignDiscount = 0m;
            else effectivePromoCodeDiscount = 0m;
        }

        var pricingReq = new PricingEngineRequest(
            MeanReferenceDailyPrice: meanA_t,
            DurationDays: request.DurationDays,
            CampaignDiscountKwd: effectiveCampaignDiscount,
            PromoCodeDiscountKwd: effectivePromoCodeDiscount,
            WalletPromoCreditKwd: await LoadPromotionalWalletCreditAsync(request, cancellationToken),
            DurationRatesPct: new Dictionary<int, decimal> { [request.DurationDays] = durationRate },
            FridayDays: request.IncludeFriday ? Math.Max(0, request.FridayDays) : 0,
            FridayRatePct: profile.FridayRate);

        var result = PricingEngine.Calculate(pricingReq);
        return result;
    }

    private async Task<decimal> LoadPromotionalWalletCreditAsync(PreviewCustomerPricingQuery request, CancellationToken cancellationToken)
    {
        if (request.CustomerId is not { } walletCustomerId)
            return 0m;
        if (!string.IsNullOrEmpty(request.CustomerSubscriptionType) && request.CustomerSubscriptionType != "Individual")
            return 0m;
        return await _db.CustomerPromotionalWallets.AsNoTracking()
            .Where(x => x.CustomerUserId == walletCustomerId)
            .Select(x => (decimal?)x.Balance.Amount)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;
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

    private static PromoTargeting? ReadPromoTargeting(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(null, null, null, null);
        try { return JsonSerializer.Deserialize<PromoTargeting>(json); }
        catch (JsonException) { return null; }
    }

    private static bool IsDurationEligible(string json, int durationDays)
    {
        try
        {
            var durations = JsonSerializer.Deserialize<HashSet<int>>(json);
            return durations?.Contains(durationDays) == true;
        }
        catch (JsonException) { return false; }
    }

    private sealed record PromoTargeting(Guid? CustomerUserId, Guid? ProgramId, Guid? BundleId, string? SubscriptionType);

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
