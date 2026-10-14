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
    decimal? EstimatedGrossPayout,
    decimal? EstimatedNetPayout,
    string Currency);

public sealed record RestaurantCampaignInvitationDto(
    Guid Id,
    Guid CampaignId,
    string CampaignNameAr,
    string CampaignNameEn,
    string Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal? TotalDiscountRate,
    decimal? MealMateShareRate,
    decimal? RestaurantShareRate,
    string ParticipationStatus,
    int? DedicatedDailyCapacity,
    string? DeclineReason,
    IReadOnlyList<DurationPriceBreakdownDto> DurationPrices,
    IReadOnlyList<CampaignProgramDto> Programs,
    IReadOnlyList<CampaignParticipatingBoxDto> ParticipatingBoxes,
    DateTime? EnrollmentOpensAtUtc = null,
    DateTime? EnrollmentClosesAtUtc = null,
    DateTime? ReviewStartsAtUtc = null,
    DateTime? ReviewEndsAtUtc = null);

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

        // An invitation exists only when the restaurant has a persisted enrollment.
        var enrollments = await _context.CampaignRestaurantEnrollments
            .AsNoTracking()
            .Include(e => e.Campaign)
            .Where(e => e.RestaurantId == restaurantId.Value &&
                        e.ParticipationStatus == CampaignRestaurantParticipationStatus.Invited &&
                        e.Campaign.Status == CampaignStatus.OpenForJoining)
            .OrderByDescending(e => e.Campaign.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var campaignIds = enrollments.Select(e => e.CampaignId).Distinct().ToList();
        var openCampaigns = await _context.Campaigns
            .AsNoTracking()
            .Include(c => c.Bundles)
                .ThenInclude(b => b.DurationPrices)
            .Where(c => campaignIds.Contains(c.Id))
            .OrderByDescending(c => c.CreatedAtUtc)
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
            var enrollment = enrollments.Single(e => e.CampaignId == c.Id);
            var totalDiscount = c.TotalDiscountRate?.Value;
            var mealMateShare = c.MealMateShareRate?.Value;
            var restaurantShare = c.RestaurantShareRate?.Value;

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
                    durationPrices.Add(new DurationPriceBreakdownDto(bp.DurationDays, refPrice, null, null, bp.ReferenceCustomerPrice.CurrencyCode));
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
                enrollment.Id,
                c.Id,
                c.NameAr,
                c.NameEn,
                c.Status.ToString(),
                c.StartsAtUtc,
                c.EndsAtUtc,
                totalDiscount,
                mealMateShare,
                restaurantShare,
                enrollment.ParticipationStatus.ToString(),
                enrollment.DedicatedDailyCapacity,
                enrollment.DeclineReason,
                durationPrices,
                programs,
                boxes,
                c.EnrollmentOpensAtUtc,
                c.EnrollmentClosesAtUtc,
                c.ReviewStartsAtUtc,
                c.ReviewEndsAtUtc
            ));
        }

        return Result.Success<IReadOnlyList<RestaurantCampaignInvitationDto>>(result);
    }

    private async Task<Guid?> ResolveTargetRestaurantIdAsync(Guid? requestedId, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserType != UserType.Restaurant) return null;
        var actorRestaurantId = _currentUser.RestaurantId;
        if (!actorRestaurantId.HasValue && _currentUser.UserId.HasValue)
        {
            actorRestaurantId = await _context.RestaurantProfiles
                .Where(r => r.UserId == _currentUser.UserId.Value && !r.IsDeleted)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return requestedId.HasValue && requestedId.Value != Guid.Empty
            ? actorRestaurantId == requestedId.Value ? requestedId : null
            : actorRestaurantId;
    }
}
