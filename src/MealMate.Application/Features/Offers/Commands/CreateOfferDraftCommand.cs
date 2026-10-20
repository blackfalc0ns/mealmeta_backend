using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Commands;

public sealed record CreateOfferDraftCommand(
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string? BannerImageUrl = null,
    OfferType Type = OfferType.StandardDiscount,
    DiscountType DiscountType = DiscountType.Percentage,
    OfferFundingMode FundingMode = OfferFundingMode.PlatformFull,
    decimal? DiscountRate = null,
    decimal? DiscountAmount = null,
    decimal? PlatformShareRate = null,
    decimal? RestaurantShareRate = null,
    DateTime StartsAtUtc = default,
    DateTime EndsAtUtc = default,
    decimal? MinOrderAmount = null,
    int? MinSubscriptionDays = null,
    int? MaxSubscriptionDays = null,
    int? TotalUsageLimit = null,
    int PerCustomerLimit = 1,
    CustomerSegmentFilter CustomerSegment = CustomerSegmentFilter.All,
    string? AllowedGovernoratesJson = null,
    Guid? CampaignId = null,
    bool AllowPromoCodeStacking = false,
    string? PromoCode = null,
    string? TermsAndConditionsAr = null,
    string? TermsAndConditionsEn = null,
    List<Guid>? ProgramIds = null,
    List<Guid>? BundleIds = null,
    List<Guid>? RestaurantIds = null,
    List<OfferPackageTargetInput>? PackageTargets = null
) : ICommand<OfferDto>;

public sealed class CreateOfferDraftCommandValidator : AbstractValidator<CreateOfferDraftCommand>
{
    public CreateOfferDraftCommandValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StartsAtUtc).LessThan(x => x.EndsAtUtc);
        RuleFor(x => x.PerCustomerLimit).GreaterThan(0);
    }
}

public sealed class CreateOfferDraftCommandHandler : ICommandHandler<CreateOfferDraftCommand, OfferDto>
{
    private readonly IAppDbContext _context;

    public CreateOfferDraftCommandHandler(IAppDbContext context) => _context = context;

    public async Task<Result<OfferDto>> Handle(CreateOfferDraftCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
        {
            return Error.Validation("Offer.NameRequired", "Offer NameAr and NameEn are required.");
        }

        if (request.StartsAtUtc >= request.EndsAtUtc)
        {
            return Error.Validation("Offer.InvalidDates", "StartsAtUtc must be before EndsAtUtc.");
        }

        var targetValidation = await OfferPackageTargetCommandValidation.ValidateAsync(_context, request.PackageTargets, cancellationToken);
        if (targetValidation is not null) return targetValidation;

        var offer = new Offer
        {
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DescriptionAr = request.DescriptionAr?.Trim(),
            DescriptionEn = request.DescriptionEn?.Trim(),
            BannerImageUrl = request.BannerImageUrl?.Trim(),
            Status = OfferStatus.Draft,
            Type = request.Type,
            DiscountType = request.DiscountType,
            FundingMode = request.FundingMode,
            DiscountRate = request.DiscountRate.HasValue ? Percentage.FromFraction(request.DiscountRate.Value) : null,
            DiscountAmount = request.DiscountAmount.HasValue ? new Money(request.DiscountAmount.Value, "KWD") : null,
            PlatformShareRate = request.PlatformShareRate.HasValue ? Percentage.FromFraction(request.PlatformShareRate.Value) : null,
            RestaurantShareRate = request.RestaurantShareRate.HasValue ? Percentage.FromFraction(request.RestaurantShareRate.Value) : null,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
            MinOrderAmount = request.MinOrderAmount.HasValue ? new Money(request.MinOrderAmount.Value, "KWD") : null,
            MinSubscriptionDays = request.MinSubscriptionDays,
            MaxSubscriptionDays = request.MaxSubscriptionDays,
            TotalUsageLimit = request.TotalUsageLimit,
            PerCustomerLimit = request.PerCustomerLimit,
            CustomerSegment = request.CustomerSegment,
            AllowedGovernoratesJson = request.AllowedGovernoratesJson,
            AllowPromoCodeStacking = request.AllowPromoCodeStacking,
            CampaignId = request.CampaignId,
            PromoCode = request.PromoCode,
            TermsAndConditionsAr = request.TermsAndConditionsAr,
            TermsAndConditionsEn = request.TermsAndConditionsEn
        };

        if (request.ProgramIds != null)
        {
            foreach (var pId in request.ProgramIds.Distinct())
            {
                offer.EligiblePrograms.Add(new OfferProgram { Offer = offer, ProgramId = pId });
            }
        }

        if (request.BundleIds != null)
        {
            foreach (var bId in request.BundleIds.Distinct())
            {
                offer.EligibleBundles.Add(new OfferBundle { Offer = offer, BundleId = bId });
            }
        }

        if (request.RestaurantIds != null)
        {
            foreach (var rId in request.RestaurantIds.Distinct())
            {
                offer.EligibleRestaurants.Add(new OfferRestaurant { Offer = offer, RestaurantId = rId });
            }
        }

        if (request.PackageTargets is not null)
        {
            foreach (var target in request.PackageTargets)
            {
                offer.PackageTargets.Add(new OfferPackageTarget
                {
                    Offer = offer,
                    SubscriptionType = target.SubscriptionType.Trim(),
                    ProgramId = target.ProgramId,
                    BundleId = target.BundleId,
                    Tier = target.Tier,
                    AllowedDurationDays = target.AllowedDurationDays.Distinct().Order().ToArray()
                });
            }
        }

        _context.Offers.Add(offer);
        await _context.SaveChangesAsync(cancellationToken);

        return MapOfferDto(offer);
    }

    public static OfferDto MapOfferDto(Offer o) => new(
        o.Id,
        o.NameAr,
        o.NameEn,
        o.DescriptionAr,
        o.DescriptionEn,
        o.BannerImageUrl,
        o.Status,
        o.Type,
        o.DiscountType,
        o.FundingMode,
        o.DiscountRate?.Value,
        o.DiscountAmount?.Amount,
        o.PlatformShareRate?.Value,
        o.RestaurantShareRate?.Value,
        o.StartsAtUtc,
        o.EndsAtUtc,
        o.MinOrderAmount?.Amount,
        o.MinSubscriptionDays,
        o.MaxSubscriptionDays,
        o.TotalUsageLimit,
        o.CurrentUsageCount,
        o.PerCustomerLimit,
        o.CustomerSegment,
        o.CampaignId,
        o.AllowPromoCodeStacking,
        o.TotalUsageLimit.HasValue ? Math.Max(0, o.TotalUsageLimit.Value - o.CurrentUsageCount) : null,
        o.PromoCode,
        o.TermsAndConditionsAr,
        o.TermsAndConditionsEn,
        o.EligiblePrograms.Select(p => p.ProgramId).Distinct().ToArray(),
        o.EligibleBundles.Select(b => b.BundleId).Distinct().ToArray(),
        o.EligibleRestaurants.Select(r => r.RestaurantId).Distinct().ToArray(),
        o.CampaignId.HasValue ? "Campaign" : "Offer",
        o.PackageTargets.Select(MapPackageTarget).ToArray(),
        PurchaseMode: DTOs.PurchaseModes.Of(o)
    );

    private static OfferPackageTargetDto MapPackageTarget(OfferPackageTarget target) =>
        new(target.SubscriptionType, target.ProgramId, target.BundleId, target.Tier,
            target.AllowedDurationDays.Order().ToArray());
}

internal static class OfferPackageTargetCommandValidation
{
    public static async Task<Error?> ValidateAsync(
        IAppDbContext context,
        IReadOnlyCollection<OfferPackageTargetInput>? targets,
        CancellationToken cancellationToken)
    {
        if (targets is null || targets.Count == 0) return null;

        var duplicate = targets.GroupBy(x => $"{x.SubscriptionType.Trim().ToUpperInvariant()}|{x.ProgramId}|{x.BundleId}|{x.Tier}")
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            return Error.Validation("Offer.PackageTargetDuplicate", "Package targets must be unique for each subscription type, program, bundle and tier.");

        foreach (var target in targets)
        {
            if (!string.Equals(target.SubscriptionType, "Individual", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target.SubscriptionType, "Family", StringComparison.OrdinalIgnoreCase))
                return Error.Validation("Offer.InvalidSubscriptionType", "Package target subscription type must be Individual or Family.");
            if (!Enum.IsDefined(target.Tier))
                return Error.Validation("Offer.InvalidTier", "Package target tier is not supported.");
            if (target.ProgramId == Guid.Empty || target.BundleId == Guid.Empty)
                return Error.Validation("Offer.InvalidPackageTarget", "Package target program and bundle are required.");
            var durations = target.AllowedDurationDays?.Distinct().ToArray() ?? [];
            if (durations.Length == 0 || durations.Any(x => x <= 0))
                return Error.Validation("Offer.InvalidDurations", "Package target durations must contain positive unique values.");
        }

        var programIds = targets.Select(x => x.ProgramId).Distinct().ToArray();
        var bundleIds = targets.Select(x => x.BundleId).Distinct().ToArray();
        var programs = await context.Programs.AsNoTracking().Where(x => programIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken);
        var bundles = await context.Bundles.AsNoTracking().Where(x => bundleIds.Contains(x.Id)).Select(x => new { x.Id, x.ProgramId }).ToListAsync(cancellationToken);
        if (programs.Count != programIds.Length || bundles.Count != bundleIds.Length)
            return Error.Validation("Offer.PackageTargetNotFound", "Every package target program and bundle must exist.");
        if (targets.Any(x => bundles.First(b => b.Id == x.BundleId).ProgramId != x.ProgramId))
            return Error.Validation("Offer.BundleProgramMismatch", "Each package target bundle must belong to its selected program.");

        return null;
    }
}
