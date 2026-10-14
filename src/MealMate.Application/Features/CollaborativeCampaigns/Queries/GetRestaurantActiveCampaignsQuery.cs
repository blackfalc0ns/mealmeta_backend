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

public sealed record RestaurantActiveCampaignDto(
    Guid Id,
    Guid CampaignId,
    Guid RestaurantId,
    string CampaignNameAr,
    string CampaignNameEn,
    string Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int? DedicatedDailyCapacity,
    int? ReservedDailyBoxes,
    decimal? TotalDiscountRate,
    decimal? MealMateShareRate,
    decimal? RestaurantShareRate,
    decimal? GrossSettlementEarned,
    decimal? NetSettlementEarned,
    decimal? PlatformWholesaleSpread,
    decimal? PendingPayout,
    string? Currency,
    IReadOnlyList<CampaignProgramDto> Programs,
    IReadOnlyList<CampaignParticipatingBoxDto> ParticipatingBoxes,
    DateTime? EnrollmentOpensAtUtc = null,
    DateTime? EnrollmentClosesAtUtc = null,
    DateTime? ReviewStartsAtUtc = null,
    DateTime? ReviewEndsAtUtc = null,
    bool TermsChanged = false,
    bool ApprovalInvalidated = false);

public sealed record GetRestaurantActiveCampaignsQuery(Guid? RestaurantId = null) : IQuery<IReadOnlyList<RestaurantActiveCampaignDto>>;

public sealed class GetRestaurantActiveCampaignsQueryHandler : IQueryHandler<GetRestaurantActiveCampaignsQuery, IReadOnlyList<RestaurantActiveCampaignDto>>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantActiveCampaignsQueryHandler(IAppDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<RestaurantActiveCampaignDto>>> Handle(
        GetRestaurantActiveCampaignsQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveTargetRestaurantIdAsync(request.RestaurantId, cancellationToken);
        if (!restaurantId.HasValue)
        {
            return Result.Success<IReadOnlyList<RestaurantActiveCampaignDto>>(Array.Empty<RestaurantActiveCampaignDto>());
        }

        var enrollments = await _context.CampaignRestaurantEnrollments
            .AsNoTracking()
            .Include(e => e.Campaign)
            .Where(e => e.RestaurantId == restaurantId.Value &&
                        e.ParticipationStatus == CampaignRestaurantParticipationStatus.Accepted &&
                        (e.Campaign.Status == CampaignStatus.Active || e.Campaign.Status == CampaignStatus.Scheduled))
            .OrderByDescending(e => e.Campaign.StartsAtUtc)
            .ToListAsync(cancellationToken);

        var campaignIds = enrollments.Select(e => e.CampaignId).Distinct().ToList();

        var (programsDict, bundlesDict, campaignPrograms, campaignBundles, restaurantOffers) =
            await LoadRelationalMetadataAsync(campaignIds, restaurantId.Value, cancellationToken);

        var ledgers = await _context.CampaignFinancialLedgers
            .AsNoTracking()
            .Where(l => campaignIds.Contains(l.CampaignId) && l.RestaurantId == restaurantId.Value)
            .ToListAsync(cancellationToken);

        var result = new List<RestaurantActiveCampaignDto>();

        foreach (var e in enrollments)
        {
            var campaign = e.Campaign;
            var dedicated = e.DedicatedDailyCapacity ?? e.CapacityOffered;

            var campaignLedgers = ledgers.Where(l => l.CampaignId == campaign.Id).ToList();
            var settlements = campaignLedgers.Where(l => l.EntryType == CampaignLedgerEntryType.RestaurantSettlement).ToList();
            var reversals = campaignLedgers.Where(l => l.EntryType == CampaignLedgerEntryType.RestaurantSettlementReversal).ToList();
            var grossEarned = settlements.Count > 0 ? settlements.Sum(l => l.Amount) : (decimal?)null;
            var netEarned = settlements.Count + reversals.Count > 0
                ? settlements.Sum(l => l.Amount) - reversals.Sum(l => l.Amount)
                : (decimal?)null;
            var currencies = campaignLedgers.Select(l => l.Currency).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            var currency = currencies.Count == 1 ? currencies[0] : null;
            if (currencies.Count > 1)
            {
                grossEarned = null;
                netEarned = null;
            }

            var (programs, boxes) = ResolveProgramsAndBoxes(
                campaign.Id,
                campaignPrograms,
                campaignBundles,
                programsDict,
                bundlesDict,
                restaurantOffers);

            var termsChanged = campaign.ApprovedSnapshotVersion.HasValue && campaign.CurrentSnapshotVersion != campaign.ApprovedSnapshotVersion.Value;
            var approvalInvalidated = termsChanged;

            result.Add(new RestaurantActiveCampaignDto(
                e.Id,
                campaign.Id,
                e.RestaurantId,
                campaign.NameAr,
                campaign.NameEn,
                campaign.Status.ToString(),
                campaign.StartsAtUtc,
                campaign.EndsAtUtc,
                dedicated,
                null,
                campaign.TotalDiscountRate?.Value,
                campaign.MealMateShareRate?.Value,
                campaign.RestaurantShareRate?.Value,
                grossEarned,
                netEarned,
                null,
                null,
                currency,
                programs,
                boxes,
                campaign.EnrollmentOpensAtUtc,
                campaign.EnrollmentClosesAtUtc,
                campaign.ReviewStartsAtUtc,
                campaign.ReviewEndsAtUtc,
                termsChanged,
                approvalInvalidated
            ));
        }

        return Result.Success<IReadOnlyList<RestaurantActiveCampaignDto>>(result);
    }

    private async Task<(
        Dictionary<Guid, Program> ProgramsDict,
        Dictionary<Guid, Bundle> BundlesDict,
        List<CampaignProgram> CampaignPrograms,
        List<CampaignBundle> CampaignBundles,
        List<RestaurantProgramBundleOffer> RestaurantOffers)> LoadRelationalMetadataAsync(
            List<Guid> campaignIds,
            Guid restaurantId,
            CancellationToken cancellationToken)
    {
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
            .Where(r => r.RestaurantId == restaurantId && r.IsActive)
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

        return (programsDict, bundlesDict, campaignPrograms, campaignBundles, restaurantOffers);
    }

    public static (List<CampaignProgramDto> Programs, List<CampaignParticipatingBoxDto> Boxes) ResolveProgramsAndBoxes(
        Guid campaignId,
        IReadOnlyList<CampaignProgram> allCampaignPrograms,
        IReadOnlyList<CampaignBundle> allCampaignBundles,
        IReadOnlyDictionary<Guid, Program> programsDict,
        IReadOnlyDictionary<Guid, Bundle> bundlesDict,
        IReadOnlyList<RestaurantProgramBundleOffer> restaurantOffers)
    {
        var campProgs = allCampaignPrograms.Where(cp => cp.CampaignId == campaignId).ToList();
        var campBundles = allCampaignBundles.Where(cb => cb.CampaignId == campaignId).ToList();

        var programDtos = new List<CampaignProgramDto>();
        var progIds = campProgs.Select(cp => cp.ProgramId).ToHashSet();

        foreach (var pid in progIds)
        {
            if (programsDict.TryGetValue(pid, out var p))
            {
                programDtos.Add(new CampaignProgramDto(p.Id, p.NameAr, p.NameEn, p.Code));
            }
        }

        var participatingBoxes = new List<CampaignParticipatingBoxDto>();
        var bundleIds = campBundles.Select(cb => cb.BundleId).ToHashSet();

        // 1. Match from restaurant's offered bundles for the campaign's programs
        var matchedOffers = restaurantOffers
            .Where(ro => progIds.Contains(ro.ProgramId) && (bundleIds.Count == 0 || bundleIds.Contains(ro.BundleId)))
            .ToList();

        foreach (var offer in matchedOffers)
        {
            if (bundlesDict.TryGetValue(offer.BundleId, out var b))
            {
                programsDict.TryGetValue(b.ProgramId, out var p);
                participatingBoxes.Add(new CampaignParticipatingBoxDto(
                    b.Id,
                    b.ProgramId,
                    p?.NameAr ?? "",
                    p?.NameEn ?? "",
                    b.NameAr,
                    b.NameEn,
                    b.MealsPerDay,
                    b.BreakfastCount,
                    b.LunchCount,
                    b.DinnerCount,
                    b.SnackCount
                ));
            }
        }

        // 2. If restaurant offers didn't match directly, fall back to campaign's bundles
        if (participatingBoxes.Count == 0 && bundleIds.Count > 0)
        {
            foreach (var bid in bundleIds)
            {
                if (bundlesDict.TryGetValue(bid, out var b))
                {
                    programsDict.TryGetValue(b.ProgramId, out var p);
                    participatingBoxes.Add(new CampaignParticipatingBoxDto(
                        b.Id,
                        b.ProgramId,
                        p?.NameAr ?? "",
                        p?.NameEn ?? "",
                        b.NameAr,
                        b.NameEn,
                        b.MealsPerDay,
                        b.BreakfastCount,
                        b.LunchCount,
                        b.DinnerCount,
                        b.SnackCount
                    ));
                }
            }
        }

        return (programDtos, participatingBoxes);
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
