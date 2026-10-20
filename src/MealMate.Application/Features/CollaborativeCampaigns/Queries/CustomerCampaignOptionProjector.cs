using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

/// <summary>
/// Projects a campaign into customer-safe purchase options: relationship bundle ids, catalog
/// names, calculator-derived prices and full-duration availability. Funding contributions are
/// never projected; a reference price is shown only under an approved pricing policy.
/// </summary>
public sealed class CustomerCampaignOptionProjector
{
    private readonly IAppDbContext _context;

    public CustomerCampaignOptionProjector(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> LoadApprovedParticipantIdsAsync(
        IReadOnlyList<Guid> campaignIds,
        CancellationToken cancellationToken)
    {
        var participations = await _context.CampaignRestaurantEnrollments.AsNoTracking()
            .Where(e => campaignIds.Contains(e.CampaignId)
                && (e.ParticipationStatus == CampaignRestaurantParticipationStatus.Accepted
                    || e.ParticipationStatus == CampaignRestaurantParticipationStatus.Locked))
            .Select(e => new { e.CampaignId, e.RestaurantId })
            .ToListAsync(cancellationToken);

        return participations
            .GroupBy(x => x.CampaignId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Guid>)g.Select(x => x.RestaurantId).Distinct().ToList());
    }

    public async Task<CustomerCampaignOptionsProjection> ProjectAsync(
        Campaign campaign,
        IReadOnlyList<Guid> approvedParticipantIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var approved = approvedParticipantIds.ToHashSet();
        var today = DateOnly.FromDateTime(nowUtc);

        var campaignBundles = await _context.CampaignBundles.AsNoTracking()
            .Where(b => b.CampaignId == campaign.Id)
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken);
        if (campaignBundles.Count == 0)
        {
            return CustomerCampaignOptionsProjection.Empty;
        }

        var campaignBundleIds = campaignBundles.Select(b => b.Id).ToList();
        var catalogBundleIds = campaignBundles.Select(b => b.BundleId).Distinct().ToList();

        var catalogBundles = await _context.Bundles.AsNoTracking()
            .Where(b => catalogBundleIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);
        var programIds = catalogBundles.Values.Select(b => b.ProgramId).Distinct().ToList();
        var programs = await _context.Programs.AsNoTracking()
            .Where(p => programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var prices = await _context.CampaignBundleDurationPrices.AsNoTracking()
            .Where(p => campaignBundleIds.Contains(p.CampaignBundleId))
            .OrderBy(p => p.DurationDays)
            .ToListAsync(cancellationToken);

        // Full-duration availability: a box free on one day does not serve a multi-day duration;
        // count distinct future service dates with at least one remaining box across participants.
        var capacities = await _context.CampaignRestaurantCapacities.AsNoTracking()
            .Where(c => c.CampaignId == campaign.Id && c.ServiceDate >= today)
            .Select(c => new { c.RestaurantId, c.ServiceDate, c.MaxBoxes, c.ReservedBoxes })
            .ToListAsync(cancellationToken);
        var servableDates = capacities
            .Where(c => c.ServiceDate.HasValue && approved.Contains(c.RestaurantId) && c.MaxBoxes - c.ReservedBoxes >= 1)
            .Select(c => c.ServiceDate!.Value)
            .Distinct()
            .Count();

        var policies = await _context.CampaignPricingPolicies.AsNoTracking()
            .Where(p => p.ApprovedByUserId != null)
            .OrderByDescending(p => p.Version)
            .Select(p => new { p.CurrencyCode })
            .ToListAsync(cancellationToken);
        var approvedCurrencies = policies.Select(p => p.CurrencyCode).ToHashSet();

        var calculator = new CampaignPricingCalculator();
        var options = new List<CustomerCampaignOptionDto>();
        foreach (var campaignBundle in campaignBundles)
        {
            if (!catalogBundles.TryGetValue(campaignBundle.BundleId, out var catalogBundle)
                || !programs.TryGetValue(catalogBundle.ProgramId, out var program))
            {
                // The catalog entry behind this relationship vanished; it is not a sellable option.
                continue;
            }

            foreach (var price in prices.Where(p => p.CampaignBundleId == campaignBundle.Id))
            {
                var split = Domain.CollaborativeCampaigns.ValueObjects.CampaignDiscountSplit.Of(
                    Percentage.FromFraction(campaign.TotalDiscountRate.Value),
                    Percentage.FromFraction(campaign.RestaurantShareRate.Value),
                    Percentage.FromFraction(campaign.MealMateShareRate.Value));
                var calculated = calculator.Calculate(price.ReferenceCustomerPrice, split, price.DurationDays);
                var policyApproved = approvedCurrencies.Contains(price.ReferenceCustomerPrice.CurrencyCode);

                options.Add(new CustomerCampaignOptionDto(
                    campaignBundle.Id,
                    catalogBundle.Id,
                    program.Id,
                    program.NameAr,
                    program.NameEn,
                    catalogBundle.NameAr,
                    catalogBundle.NameEn,
                    catalogBundle.DescriptionAr,
                    catalogBundle.DescriptionEn,
                    price.DurationDays,
                    policyApproved ? price.ReferenceCustomerPrice.Amount : null,
                    calculated.CampaignCustomerPrice.Amount,
                    price.ReferenceCustomerPrice.CurrencyCode,
                    policyApproved ? Math.Round(campaign.TotalDiscountRate.Value * 100m, 3) : null,
                    policyApproved,
                    FullSequenceAvailable: servableDates >= price.DurationDays));
            }
        }

        return new CustomerCampaignOptionsProjection(options);
    }
}

public sealed record CustomerCampaignOptionsProjection(IReadOnlyList<CustomerCampaignOptionDto> Options)
{
    public static readonly CustomerCampaignOptionsProjection Empty = new(Array.Empty<CustomerCampaignOptionDto>());

    /// <summary>
    /// The cheapest confirmable "from" price: a fully-serveable option under an approved policy,
    /// within a single currency — cross-currency comparison is never attempted.
    /// </summary>
    public (decimal? Price, Guid? CampaignBundleId, int? DurationDays, string? CurrencyCode) MinConfirmablePrice()
    {
        CustomerCampaignOptionDto? best = null;
        foreach (var option in Options)
        {
            if (!option.CanShowReferencePrice || !option.FullSequenceAvailable)
            {
                continue;
            }

            if (best is not null && !string.Equals(best.CurrencyCode, option.CurrencyCode, StringComparison.Ordinal))
            {
                // Mixed-currency catalog: no honest single "from" price exists.
                return (null, null, null, null);
            }

            if (best is null || option.FinalPrice < best.FinalPrice)
            {
                best = option;
            }
        }

        return best is null
            ? (null, null, null, null)
            : (best.FinalPrice, best.CampaignBundleId, best.DurationDays, best.CurrencyCode);
    }
}
