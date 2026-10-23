using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.UnifiedSupport.Timing;

public sealed class SupportTimingProcessor : ISupportTimingProcessor
{
    private const int DueCandidateLimit = 100;
    private const int UnroutedCandidateLimit = 25;

    private static readonly SupportCaseStatus[] OpenStatuses =
    [
        SupportCaseStatus.New,
        SupportCaseStatus.Acknowledged,
        SupportCaseStatus.InProgress,
        SupportCaseStatus.WaitingOnRequester,
        SupportCaseStatus.WaitingOnParty,
    ];

    private readonly IAppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SupportTimingProcessor> _logger;

    public SupportTimingProcessor(
        IAppDbContext db,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SupportTimingProcessor> logger)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SupportTimingTickResult> ProcessTimingTickAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var candidateIds = await FindCandidateCaseIdsAsync(now, ct);
        if (candidateIds.Count == 0)
            return new SupportTimingTickResult(0, 0, 0, 0, 0);

        var processed = 0;
        var firstResponseBreaches = 0;
        var resolutionBreaches = 0;
        var escalated = 0;
        var unrouted = 0;

        foreach (var caseId in candidateIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await ProcessCaseAsync(caseId, now, ct);
                if (!result.Committed) continue;

                processed++;
                firstResponseBreaches += result.FirstResponseBreaches;
                resolutionBreaches += result.ResolutionBreaches;
                escalated += result.Escalated;
                unrouted += result.Unrouted;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Support timing processing failed for case {CaseId}; it will be retried on a later tick.", caseId);
            }
        }

        if (processed > 0)
        {
            _logger.LogInformation(
                "SupportTimingProcessor committed {Count} cases. FR breaches: {FR}, resolution breaches: {Res}, escalated: {Esc}, unrouted: {Unr}",
                processed, firstResponseBreaches, resolutionBreaches, escalated, unrouted);
        }

        return new SupportTimingTickResult(processed, firstResponseBreaches, resolutionBreaches, escalated, unrouted);
    }

    private async Task<List<Guid>> FindCandidateCaseIdsAsync(DateTime now, CancellationToken ct)
    {
        var dueIds = await (
            from cycle in _db.SupportCaseTimingCycles.AsNoTracking()
            join supportCase in _db.SupportCases.AsNoTracking() on cycle.SupportCaseId equals supportCase.Id
            where supportCase.ActiveTimingCycleId == cycle.Id
                && OpenStatuses.Contains(supportCase.Status)
                && ((!cycle.FirstResponseAtUtc.HasValue && !cycle.IsFirstResponseBreached
                        && cycle.FirstResponseDeadlineUtc.HasValue && cycle.FirstResponseDeadlineUtc.Value <= now)
                    || (!cycle.ResolvedAtUtc.HasValue && !cycle.IsResolutionBreached
                        && cycle.ResolutionDeadlineUtc.HasValue && cycle.ResolutionDeadlineUtc.Value <= now)
                    || (!cycle.IsEscalated && cycle.EscalationDeadlineUtc.HasValue && cycle.EscalationDeadlineUtc.Value <= now
                        && ((cycle.IsFirstResponseBreached && !cycle.FirstResponseAtUtc.HasValue)
                            || (cycle.IsResolutionBreached && !cycle.ResolvedAtUtc.HasValue))))
            orderby cycle.LastWorkerAttemptAtUtc ?? cycle.StartedAtUtc, supportCase.Id
            select supportCase.Id)
            .Take(DueCandidateLimit)
            .ToListAsync(ct);

        // Keep unrouted checks in a separate bounded lane so they cannot fill every tick's due-case batch.
        var unroutedIds = await (
            from cycle in _db.SupportCaseTimingCycles.AsNoTracking()
            join supportCase in _db.SupportCases.AsNoTracking() on cycle.SupportCaseId equals supportCase.Id
            where supportCase.ActiveTimingCycleId == cycle.Id
                && OpenStatuses.Contains(supportCase.Status)
                && supportCase.OwnerTeam == SupportCaseOwnerTeam.RestaurantDeliveryManager
                && supportCase.RestaurantId.HasValue
                && !cycle.IsUnrouted
            orderby cycle.LastWorkerAttemptAtUtc ?? cycle.StartedAtUtc, supportCase.Id
            select supportCase.Id)
            .Take(UnroutedCandidateLimit)
            .ToListAsync(ct);

        return dueIds.Concat(unroutedIds).Distinct().ToList();
    }

    private async Task<CaseProcessingResult> ProcessCaseAsync(Guid caseId, DateTime now, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<IAppDbContext>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var transactions = services.GetRequiredService<IApplicationTransactionExecutor>();
        var locks = services.GetRequiredService<IApplicationLockManager>();
        var currentUser = services.GetRequiredService<ICurrentUser>();

        return await transactions.ExecuteAsync(
            token => ProcessCaseInTransactionAsync(caseId, now, db, unitOfWork, locks, currentUser, token), ct);
    }

    private static async Task<CaseProcessingResult> ProcessCaseInTransactionAsync(
        Guid caseId,
        DateTime now,
        IAppDbContext db,
        IUnitOfWork unitOfWork,
        IApplicationLockManager locks,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        // The execution strategy can rerun this operation on the same scoped context.
        // Discard only staged effects from a prior attempt, then re-read under the case lock.
        foreach (var priorEvent in db.SupportCaseEvents.Local
                     .Where(x => x.SupportCaseId == caseId).ToArray())
            db.DetachTrackedEntity(priorEvent);
        foreach (var priorNotification in db.AdminNotifications.Local.ToArray())
            db.DetachTrackedEntity(priorNotification);
        foreach (var priorCycle in db.SupportCaseTimingCycles.Local
                     .Where(x => x.SupportCaseId == caseId).ToArray())
            db.DetachTrackedEntity(priorCycle);

        await locks.LockSupportCaseAsync(caseId, ct);

        var supportCase = await db.SupportCases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !OpenStatuses.Contains(supportCase.Status) || !supportCase.ActiveTimingCycleId.HasValue)
            return default;

        // The active cycle is intentionally loaded only after the shared case lock is held.
        var cycle = await db.SupportCaseTimingCycles.SingleOrDefaultAsync(
            x => x.Id == supportCase.ActiveTimingCycleId.Value && x.SupportCaseId == supportCase.Id, ct);
        if (cycle is null) return default;

        var access = new SupportCaseAccess(db, currentUser);
        var firstResponseCount = 0;
        var resolutionCount = 0;
        var escalatedCount = 0;
        var unroutedCount = 0;
        var mutated = false;
        cycle.MarkWorkerAttempt(now);

        if (!cycle.FirstResponseAtUtc.HasValue && cycle.FirstResponseDeadlineUtc.HasValue
            && now >= cycle.FirstResponseDeadlineUtc.Value && !cycle.IsFirstResponseBreached)
        {
            cycle.MarkFirstResponseBreached();
            firstResponseCount++;
            mutated = true;
        }

        if (!cycle.ResolvedAtUtc.HasValue && cycle.ResolutionDeadlineUtc.HasValue
            && now >= cycle.ResolutionDeadlineUtc.Value && !cycle.IsResolutionBreached)
        {
            cycle.MarkResolutionBreached();
            resolutionCount++;
            mutated = true;
        }

        var firstResponseStillOutstanding = cycle.IsFirstResponseBreached && !cycle.FirstResponseAtUtc.HasValue;
        var resolutionStillOutstanding = cycle.IsResolutionBreached && !cycle.ResolvedAtUtc.HasValue;
        if (!cycle.IsEscalated && cycle.EscalationDeadlineUtc.HasValue && now >= cycle.EscalationDeadlineUtc.Value
            && (firstResponseStillOutstanding || resolutionStillOutstanding))
        {
            var recipients = await GetScopedOperatorRecipientIdsAsync(access, supportCase, ct);
            if (recipients.Count > 0)
            {
                var reason = firstResponseStillOutstanding && resolutionStillOutstanding
                    ? "First response and resolution deadlines remain outstanding"
                    : firstResponseStillOutstanding
                        ? "First response deadline remains outstanding"
                        : "Resolution deadline remains outstanding";
                cycle.Escalate(reason, now);
                db.SupportCaseEvents.Add(new SupportCaseEvent
                {
                    SupportCaseId = supportCase.Id,
                    ActorUserId = Guid.Empty,
                    EventType = "timing.escalated",
                    FromValue = "Normal",
                    ToValue = reason,
                    CreatedAtUtc = now,
                });
                AddEscalationNotifications(db, supportCase, reason, recipients, now);
                escalatedCount++;
                mutated = true;
            }
        }

        if (supportCase.OwnerTeam == SupportCaseOwnerTeam.RestaurantDeliveryManager
            && supportCase.RestaurantId is { } restaurantId && !cycle.IsUnrouted)
        {
            var managers = await db.GetDeliveryManagerUserIdsAsync(restaurantId, ct);
            if (managers.Count == 0)
            {
                var recipients = await GetScopedOperatorRecipientIdsAsync(access, supportCase, ct);
                if (recipients.Count > 0)
                {
                    cycle.SetUnrouted(true, "No active delivery manager registered for restaurant.");
                    db.SupportCaseEvents.Add(new SupportCaseEvent
                    {
                        SupportCaseId = supportCase.Id,
                        ActorUserId = Guid.Empty,
                        EventType = "timing.unrouted",
                        ToValue = "Restaurant delivery manager missing",
                        CreatedAtUtc = now,
                    });
                    AddUnroutedNotifications(db, supportCase, recipients, now);
                    unroutedCount++;
                    mutated = true;
                }
            }
        }

        if (!mutated)
        {
            await unitOfWork.SaveChangesAsync(ct);
            return new CaseProcessingResult(true, 0, 0, 0, 0);
        }

        db.MarkSupportCaseModified(supportCase);
        await unitOfWork.SaveChangesAsync(ct);
        return new CaseProcessingResult(true, firstResponseCount, resolutionCount, escalatedCount, unroutedCount);
    }

    private static async Task<List<Guid>> GetScopedOperatorRecipientIdsAsync(
        SupportCaseAccess access, SupportCase supportCase, CancellationToken ct)
    {
        var candidates = await access.GetEligibleOperatorsAsync(supportCase, requireManage: false, ct);
        return candidates.Select(x => x.UserId).Distinct().ToList();
    }

    private static void AddEscalationNotifications(
        IAppDbContext db, SupportCase supportCase, string reason, List<Guid> operatorIds, DateTime now)
    {
        foreach (var recipient in operatorIds)
        {
            db.AdminNotifications.Add(new AdminNotification
            {
                UserId = recipient,
                TitleAr = "تصعيد حالة دعم",
                TitleEn = "Support Case Escalated",
                BodyAr = $"تم تصعيد حالة الدعم رقم {supportCase.Id:D}: {reason}.",
                BodyEn = $"Support case {supportCase.Id:D} escalated: {reason}.",
                Channel = NotificationChannel.InApp,
                IsRead = false,
                SentAtUtc = now,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    deepLink = $"/admin/support/complaints?caseId={supportCase.Id:D}",
                    eventKey = "support.case.escalated",
                    caseId = supportCase.Id,
                    reason,
                }),
                CreatedAtUtc = now,
            });
        }
    }

    private static void AddUnroutedNotifications(
        IAppDbContext db, SupportCase supportCase, List<Guid> operatorIds, DateTime now)
    {
        foreach (var recipient in operatorIds)
        {
            db.AdminNotifications.Add(new AdminNotification
            {
                UserId = recipient,
                TitleAr = "حالة دعم غير موجهة",
                TitleEn = "Unrouted Support Case",
                BodyAr = $"حالة الدعم رقم {supportCase.Id:D} بحاجة للتدخل لعدم وجود مدير توصيل للمطعم.",
                BodyEn = $"Support case {supportCase.Id:D} requires attention: no active delivery manager for restaurant.",
                Channel = NotificationChannel.InApp,
                IsRead = false,
                SentAtUtc = now,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    deepLink = $"/admin/support/complaints?caseId={supportCase.Id:D}",
                    eventKey = "support.case.unrouted",
                    caseId = supportCase.Id,
                }),
                CreatedAtUtc = now,
            });
        }
    }

    private readonly record struct CaseProcessingResult(
        bool Committed,
        int FirstResponseBreaches,
        int ResolutionBreaches,
        int Escalated,
        int Unrouted);
}
