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
        var existing = await _db.RestaurantBundlePricings
            .Where(x => x.RestaurantId == request.RestaurantId && x.ProgramId == request.ProgramId && x.BundleId == request.BundleId && x.EffectiveToUtc == null)
            .OrderByDescending(x => x.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        RestaurantBundlePricing newPricing;

        if (existing is not null && existing.LockStatus == PriceLockStatus.Draft)
        {
            // Edit existing draft pricing
            existing.ReferencePrice26 = new Money(request.ReferencePrice26, "KWD");
            existing.ContractedSupplyPrice26 = new Money(request.ContractedSupplyPrice26, "KWD");
            existing.ChangeRequestReason = request.ChangeRequestReason;
            newPricing = existing;
        }
        else
        {
            // Create a new version if existing is locked/approved
            if (existing is not null)
            {
                existing.EffectiveToUtc = _clock.UtcNow;
            }

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

    public async Task<Result<ClassificationRunDto>> Handle(RunClassificationCommand request, CancellationToken cancellationToken)
    {
        var activePricings = await _db.RestaurantBundlePricings
            .Where(x => x.ProgramId == request.ProgramId && x.BundleId == request.BundleId && x.EffectiveToUtc == null && x.LockStatus != PriceLockStatus.Draft)
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
        // Retrieve latest mean reference daily price (A_t) for requested Tier
        var run = await _db.ClassificationRuns
            .Include(x => x.Classifications)
            .Where(x => x.ProgramId == request.ProgramId && x.BundleId == request.BundleId)
            .OrderByDescending(x => x.RunAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        decimal meanA_t;
        if (run is not null)
        {
            var tierItems = run.Classifications.Where(x => x.Tier == request.Tier).ToList();
            meanA_t = tierItems.Any() ? tierItems.Average(x => x.ReferenceDailyBox) : run.MeanReferenceDaily;
        }
        else
        {
            meanA_t = 3.500m; // Default baseline if classification hasn't been run yet
        }

        var pricingReq = new PricingEngineRequest(
            MeanReferenceDailyPrice: meanA_t,
            DurationDays: request.DurationDays,
            CampaignDiscountKwd: request.CampaignDiscountKwd,
            PromoCodeDiscountKwd: request.PromoCodeDiscountKwd);

        var result = PricingEngine.Calculate(pricingReq);
        return result;
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
