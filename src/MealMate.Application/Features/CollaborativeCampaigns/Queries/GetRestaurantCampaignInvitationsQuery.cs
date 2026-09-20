using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record DurationPriceBreakdownDto(
    int DurationDays,
    decimal ReferenceCustomerPrice,
    decimal EstimatedGrossPayout,
    decimal EstimatedNetPayout);

public sealed record RestaurantCampaignInvitationDto(
    Guid Id,
    Guid CampaignId,
    string CampaignNameAr,
    string CampaignNameEn,
    string Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal TotalDiscountRate,
    decimal MealMateShareRate,
    decimal RestaurantShareRate,
    string ParticipationStatus,
    int? DedicatedDailyCapacity,
    string? DeclineReason,
    IReadOnlyList<DurationPriceBreakdownDto> DurationPrices,
    IReadOnlyList<CampaignProgramDto> Programs,
    IReadOnlyList<CampaignParticipatingBoxDto> ParticipatingBoxes);

public sealed record GetRestaurantCampaignInvitationsQuery(Guid? RestaurantId = null) : IQuery<IReadOnlyList<RestaurantCampaignInvitationDto>>;

public sealed class GetRestaurantCampaignInvitationsQueryHandler : IQueryHandler<GetRestaurantCampaignInvitationsQuery, IReadOnlyList<RestaurantCampaignInvitationDto>>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantCampaignInvitationsQueryHandler(IAppDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<RestaurantCampaignInvitationDto>>> Handle(
        GetRestaurantCampaignInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveTargetRestaurantIdAsync(request.RestaurantId, cancellationToken);
        if (!restaurantId.HasValue)
        {
            return Result.Success<IReadOnlyList<RestaurantCampaignInvitationDto>>(Array.Empty<RestaurantCampaignInvitationDto>());
        }

        // Open campaigns for joining
        var openCampaigns = await _context.Campaigns
            .AsNoTracking()
            .Include(c => c.Bundles)
                .ThenInclude(b => b.DurationPrices)
            .Where(c => c.Status == CampaignStatus.OpenForJoining)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var campaignIds = openCampaigns.Select(c => c.Id).ToList();

        var existingEnrollments = await _context.CampaignRestaurantEnrollments
            .AsNoTracking()
            .Where(e => campaignIds.Contains(e.CampaignId) && e.RestaurantId == restaurantId.Value)
            .ToListAsync(cancellationToken);

        var campaignPrograms = await _context.CampaignPrograms
            .AsNoTracking()
            .Where(cp => campaignIds.Contains(cp.CampaignId))
            .ToListAsync(cancellationToken);

        var campaignBundles = await _context.CampaignBundles
            .AsNoTracking()
            .Where(cb => campaignIds.Contains(cb.CampaignId))
            .ToListAsync(cancellationToken);

        var restaurantOffers = await _context.RestaurantProgramBundleOffers
            .AsNoTracking()
            .Where(r => r.RestaurantId == restaurantId.Value && r.IsActive)
            .ToListAsync(cancellationToken);

        var allProgIds = campaignPrograms.Select(cp => cp.ProgramId)
            .Union(restaurantOffers.Select(ro => ro.ProgramId))
            .Distinct()
            .ToList();

        var programsDict = await _context.Programs
            .AsNoTracking()
            .Where(p => allProgIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var allBundleIds = campaignBundles.Select(cb => cb.BundleId)
            .Union(restaurantOffers.Select(ro => ro.BundleId))
            .Distinct()
            .ToList();

        var bundlesDict = await _context.Bundles
            .AsNoTracking()
            .Where(b => allBundleIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var result = new List<RestaurantCampaignInvitationDto>();

        foreach (var c in openCampaigns)
        {
            var enrollment = existingEnrollments.FirstOrDefault(e => e.CampaignId == c.Id);

            Guid enrollmentId;
            string participationStatus;
            int? dedicatedCapacity = null;
            string? declineReason = null;

            if (enrollment != null)
            {
                enrollmentId = enrollment.Id;
                participationStatus = enrollment.ParticipationStatus.ToString();
                dedicatedCapacity = enrollment.DedicatedDailyCapacity;
                declineReason = enrollment.DeclineReason;
            }
            else
            {
                enrollmentId = Guid.NewGuid();
                participationStatus = "Invited";
            }

            var totalDiscount = c.TotalDiscountRate?.Value ?? 0.20m;
            var mealMateShare = c.MealMateShareRate?.Value ?? 0.10m;
            var restaurantShare = c.RestaurantShareRate?.Value ?? 0.10m;

            var durationPrices = new List<DurationPriceBreakdownDto>();

            var allBundlePrices = c.Bundles
                .SelectMany(b => b.DurationPrices)
                .OrderBy(p => p.DurationDays)
                .ToList();

            if (allBundlePrices.Count > 0)
            {
                foreach (var bp in allBundlePrices)
                {
                    var refPrice = bp.ReferenceCustomerPrice.Amount;
                    var gross = Math.Round(refPrice * (1m - (restaurantShare * 0.5m)), 2);
                    var net = Math.Round(refPrice * (1m - restaurantShare), 2);
                    durationPrices.Add(new DurationPriceBreakdownDto(bp.DurationDays, refPrice, gross, net));
                }
            }
            else
            {
                var standardTiers = new[]
                {
                    (Days: 7, RefPrice: 42.0m),
                    (Days: 14, RefPrice: 79.0m),
                    (Days: 28, RefPrice: 150.0m),
                };

                foreach (var tier in standardTiers)
                {
                    var gross = Math.Round(tier.RefPrice * (1m - (restaurantShare * 0.5m)), 2);
                    var net = Math.Round(tier.RefPrice * (1m - restaurantShare), 2);
                    durationPrices.Add(new DurationPriceBreakdownDto(tier.Days, tier.RefPrice, gross, net));
                }
            }

            var (programs, boxes) = GetRestaurantActiveCampaignsQueryHandler.ResolveProgramsAndBoxes(
                c.Id,
                campaignPrograms,
                campaignBundles,
                programsDict,
                bundlesDict,
                restaurantOffers);

            result.Add(new RestaurantCampaignInvitationDto(
                enrollmentId,
                c.Id,
                c.NameAr,
                c.NameEn,
                c.Status.ToString(),
                c.StartsAtUtc,
                c.EndsAtUtc,
                totalDiscount,
                mealMateShare,
                restaurantShare,
                participationStatus,
                dedicatedCapacity,
                declineReason,
                durationPrices,
                programs,
                boxes
            ));
        }

        return Result.Success<IReadOnlyList<RestaurantCampaignInvitationDto>>(result);
    }

    private async Task<Guid?> ResolveTargetRestaurantIdAsync(Guid? requestedId, CancellationToken cancellationToken)
    {
        if (requestedId.HasValue && requestedId.Value != Guid.Empty)
        {
            return requestedId.Value;
        }

        if (_currentUser.RestaurantId.HasValue)
        {
            return _currentUser.RestaurantId.Value;
        }

        if (_currentUser.UserId.HasValue)
        {
            var fromUser = await _context.RestaurantProfiles
                .Where(r => r.UserId == _currentUser.UserId.Value)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (fromUser.HasValue)
            {
                return fromUser.Value;
            }
        }

        return await _context.RestaurantProfiles
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
