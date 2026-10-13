using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

/// <summary>Persisted family seat and funded-participant state for quote and checkout policy.</summary>
public sealed record FamilyParticipationContext(
    int OccupiedSeats,
    int AvailableSeats,
    int MaxSeats,
    int DeferredMemberCount,
    int QualifyingParticipantCount,
    int CurrentPurchaseCount,
    int ExistingLiveFamilyParticipantCount,
    bool ManagerSeatExempt,
    bool ManagerIndividualIsLive,
    Guid? ManagerIndividualSubscriptionId,
    Guid? ManagerLiveIndividualSubscriptionId,
    IReadOnlySet<Guid> PurchasableMemberIds,
    IReadOnlySet<Guid> LiveFamilyMemberIds,
    bool IsInitialPurchaseOrReactivation,
    bool ManagerMustBeFunded);

public sealed class FamilyParticipationContextReader
{
    private static readonly SubscriptionStatus[] QualifyingStatuses =
    [
        SubscriptionStatus.Paid,
        SubscriptionStatus.Scheduled,
        SubscriptionStatus.Active,
        SubscriptionStatus.Frozen,
        SubscriptionStatus.SuspendedForReview,
    ];

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IFamilyPricingSettingsReader _settingsReader;

    public FamilyParticipationContextReader(
        IAppDbContext db,
        IDateTimeProvider clock,
        IFamilyPricingSettingsReader settingsReader)
        => (_db, _clock, _settingsReader) = (db, clock, settingsReader);

    public async Task<FamilyParticipationContext> GetAsync(
        FamilyGroup group,
        IReadOnlySet<Guid>? selectedMemberIds,
        CancellationToken cancellationToken)
    {
        var activeMemberIds = await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(_db, group, cancellationToken);
        var members = group.Members.Where(x => activeMemberIds.Contains(x.Id)).ToList();
        selectedMemberIds ??= await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(
            _db, group.Id, cancellationToken);
        selectedMemberIds ??= new HashSet<Guid>();

        var manager = members.FirstOrDefault(x => x.Role == FamilyMemberRole.Manager);
        Guid? legacyIndividualId = group.ConvertedFromSubscriptionId is Guid convertedId
            && manager?.MemberSubscriptionId == convertedId ? convertedId : null;
        var personalSubscriptionId = group.ManagerIndividualSubscriptionId ?? legacyIndividualId
            ?? (group.ManagerIndividualSeatExempt == true ? manager?.MemberSubscriptionId : null);
        var subscriptionIds = members.Where(x => x.MemberSubscriptionId.HasValue)
            .Select(x => x.MemberSubscriptionId!.Value)
            .Append(personalSubscriptionId ?? Guid.Empty)
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        var subscriptions = subscriptionIds.Count == 0
            ? new Dictionary<Guid, Subscription>()
            : await _db.Subscriptions.AsNoTracking()
                .Where(x => subscriptionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var today = BusinessCalendarDate.KuwaitToday(_clock.UtcNow);
        var livePersonal = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.CustomerUserId == group.ManagerCustomerUserId
                && x.FamilyGroupId == null && x.FamilyMemberId == null
                && QualifyingStatuses.Contains(x.Status) && x.EndDate >= today)
            .OrderByDescending(x => x.EndDate).ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var legacyPersonal = personalSubscriptionId is Guid legacyId
            ? subscriptions.GetValueOrDefault(legacyId)
            : null;
        var legacyIdentityValid = legacyPersonal is not null
            && legacyPersonal.CustomerUserId == group.ManagerCustomerUserId
            && legacyPersonal.Id == group.ConvertedFromSubscriptionId
            && legacyPersonal.FamilyGroupId == group.Id
            && legacyPersonal.FamilyMemberId == manager?.Id;
        var managerIndividualLive = livePersonal is not null
            || (legacyIdentityValid && IsQualifying(legacyPersonal!, today));
        var livePersonalId = livePersonal?.Id
            ?? (legacyIdentityValid && IsQualifying(legacyPersonal!, today) ? legacyPersonal!.Id : null);
        var managerSeatExempt = group.ManagerIndividualSeatExempt
            ?? (legacyIdentityValid || personalSubscriptionId is not null && group.ConvertedFromSubscriptionId == personalSubscriptionId);

        var linkedUserByMember = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => activeMemberIds.Contains(x.FamilyMemberId) && x.RemovedAtUtc == null)
            .Select(x => new { x.FamilyMemberId, x.LinkedCustomerUserId })
            .ToListAsync(cancellationToken);
        var owners = linkedUserByMember.GroupBy(x => x.FamilyMemberId)
            .ToDictionary(x => x.Key, x => x.Select(a => a.LinkedCustomerUserId).FirstOrDefault());

        var liveFamilyIds = new HashSet<Guid>();
        var reusableMemberIds = new HashSet<Guid>();
        foreach (var member in members)
        {
            if (member.MemberSubscriptionId is not Guid id)
                continue;

            if (!subscriptions.TryGetValue(id, out var subscription))
                continue;
            var isPersonal = member.Role == FamilyMemberRole.Manager && id == personalSubscriptionId;
            if (isPersonal)
            {
                if (!IsRetainedManagerIndividual(subscription, group, member))
                    continue;
                if (managerIndividualLive)
                    continue;
                if (IsTerminalOrExpired(subscription, today))
                    reusableMemberIds.Add(member.Id);
                continue;
            }

            var expectedOwnerId = member.Role == FamilyMemberRole.Manager
                ? group.ManagerCustomerUserId
                : owners.GetValueOrDefault(member.Id);
            if (expectedOwnerId is null || subscription.CustomerUserId != expectedOwnerId)
                continue;
            if (subscription.FamilyGroupId != group.Id || subscription.FamilyMemberId != member.Id)
                continue;
            if (IsQualifying(subscription, today))
                liveFamilyIds.Add(member.Id);
            else if (IsTerminalOrExpired(subscription, today))
                reusableMemberIds.Add(member.Id);
        }

        var currentPurchaseMembers = members.Where(x => selectedMemberIds.Contains(x.Id)
                && !liveFamilyIds.Contains(x.Id)
                && (x.Role != FamilyMemberRole.Manager || !managerSeatExempt || !managerIndividualLive)
                && (x.MemberSubscriptionId is null || reusableMemberIds.Contains(x.Id))
                && FamilyMemberPlanRules.HasPricablePlan(x))
            .ToList();
        var occupiedSeats = members.Count - (managerSeatExempt && manager is not null ? 1 : 0);
        var qualifyingCount = liveFamilyIds.Count + currentPurchaseMembers.Count
            + (managerSeatExempt && managerIndividualLive ? 1 : 0);
        var deferredCount = members.Count(x => !selectedMemberIds.Contains(x.Id)
            && !liveFamilyIds.Contains(x.Id)
            && x.MemberSubscriptionId is null);

        var settings = await _settingsReader.GetAsync(cancellationToken);
        var maxSeats = FamilyPackageResolver.FindActive(settings, group.SelectedFamilyPackageId)?.MaxMembers ?? 0;
        var hasLiveFamilyService = liveFamilyIds.Count > 0;
        var managerMustBeFunded = group.Status == FamilyGroupStatus.Draft
            && !hasLiveFamilyService
            && !(managerSeatExempt && managerIndividualLive);
        return new FamilyParticipationContext(
            occupiedSeats,
            Math.Max(0, maxSeats - occupiedSeats),
            maxSeats,
            deferredCount,
            qualifyingCount,
            currentPurchaseMembers.Count,
            liveFamilyIds.Count,
            managerSeatExempt,
            managerIndividualLive,
            personalSubscriptionId,
            livePersonalId,
            currentPurchaseMembers.Select(x => x.Id).ToHashSet(),
            liveFamilyIds,
            !hasLiveFamilyService,
            managerMustBeFunded);
    }

    private static bool IsQualifying(Subscription subscription, DateOnly today)
        => QualifyingStatuses.Contains(subscription.Status) && subscription.EndDate >= today;

    private static bool IsTerminalOrExpired(Subscription subscription, DateOnly today)
        => subscription.Status is SubscriptionStatus.Completed or SubscriptionStatus.Cancelled
            or SubscriptionStatus.Expired or SubscriptionStatus.Upgraded or SubscriptionStatus.PaymentFailed
            || subscription.EndDate < today;

    private static bool IsRetainedManagerIndividual(Subscription subscription, FamilyGroup group, FamilyMember manager)
        => subscription.CustomerUserId == group.ManagerCustomerUserId
            && ((subscription.FamilyGroupId is null && subscription.FamilyMemberId is null)
                || (subscription.Id == group.ConvertedFromSubscriptionId
                    && subscription.FamilyGroupId == group.Id
                    && subscription.FamilyMemberId == manager.Id));
}
