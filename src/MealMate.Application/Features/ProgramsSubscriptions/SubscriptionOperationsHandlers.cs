using System.Text.Json;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.CalendarMeals;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Validators
public sealed class RequestSubscriptionFreezeCommandValidator : AbstractValidator<RequestSubscriptionFreezeCommand>
{
    public RequestSubscriptionFreezeCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.FreezeEndDate).GreaterThanOrEqualTo(x => x.FreezeStartDate);
    }
}

public sealed class RequestSubscriptionUpgradeCommandValidator : AbstractValidator<RequestSubscriptionUpgradeCommand>
{
    public RequestSubscriptionUpgradeCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
    }
}

// Handlers
public sealed class SubscriptionOperationsHandlers :
    ICommandHandler<RequestSubscriptionFreezeCommand, SubscriptionFreezeRequestDto>,
    ICommandHandler<ApproveSubscriptionFreezeCommand, SubscriptionFreezeRequestDto>,
    ICommandHandler<RequestSubscriptionUpgradeCommand, SubscriptionUpgradeDto>,
    ICommandHandler<ApproveSubscriptionUpgradeCommand, SubscriptionUpgradeDto>,
    ICommandHandler<ProcessSubscriptionRenewalsAndExpirationsCommand, SubscriptionBatchProcessingResultDto>,
    IQueryHandler<GetSubscriptionFreezeRequestsQuery, IReadOnlyList<AdminSubscriptionFreezeRequestRowDto>>,
    IQueryHandler<GetSubscriptionUpgradeRequestsQuery, IReadOnlyList<AdminSubscriptionUpgradeRowDto>>,
    IQueryHandler<GetSubscriptionCancellationRequestsQuery, PagedResult<AdminSubscriptionCancellationRequestDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionTierUpgradeQuoteBuilder _upgradeQuotes;
    private readonly IUserAccountService? _users;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;
    private readonly Freeze.ISubscriptionFreezePreparationService? _freezePreparation;
    private readonly IFamilySubscriptionAccess _familyAccess;

    public SubscriptionOperationsHandlers(
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ISubscriptionTierUpgradeQuoteBuilder upgradeQuotes,
        IUserAccountService? users = null,
        ISubscriptionScheduleService? schedule = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null,
        Freeze.ISubscriptionFreezePreparationService? freezePreparation = null,
        IFamilySubscriptionAccess? familyAccess = null)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _upgradeQuotes = upgradeQuotes;
        _users = users;
        _schedule = schedule ?? new SubscriptionScheduleService(clock);
        _transactions = transactions;
        _locks = locks;
        _freezePreparation = freezePreparation;
        _familyAccess = familyAccess ?? new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db));
    }

    public async Task<Result<SubscriptionFreezeRequestDto>> Handle(RequestSubscriptionFreezeCommand request, CancellationToken cancellationToken)
    {
        if (request.CustomerActorUserId.HasValue && await _db.Subscriptions.AsNoTracking()
            .AnyAsync(x => x.Id == request.SubscriptionId && x.FamilyGroupId != null, cancellationToken)
            && (_transactions is null || _locks is null))
            return Error.Conflict("subscription.service_unavailable");
        if (_transactions is null) return await RequestFreezeCore(request, cancellationToken);
        return await _transactions.ExecuteResultAsync<SubscriptionFreezeRequestDto>(async ct =>
        {
            await LockFreezeScopeAsync(request.SubscriptionId, ct);
            await ReloadFreezeStateAsync(request.SubscriptionId, ct);
            return await RequestFreezeCore(request, ct);
        }, cancellationToken);
    }

    private async Task<Result<SubscriptionFreezeRequestDto>> RequestFreezeCore(RequestSubscriptionFreezeCommand request, CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == request.SubscriptionId, cancellationToken);
        if (subscription is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{request.SubscriptionId}' was not found.");
        }

        // Customer authorization is re-evaluated only after the shared family and
        // subscription locks have been acquired. Admin callers use the separate
        // admin endpoint and leave CustomerActorUserId unset.
        if (request.CustomerActorUserId is Guid actorUserId)
        {
            var authorized = await _familyAccess.AuthorizeAsync(subscription, actorUserId, FamilyAction.RequestFreeze, cancellationToken);
            if (authorized.IsFailure) return authorized.Error;
        }

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
        {
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, $"Cannot request freeze for subscription in state '{subscription.Status}'.");
        }

        var businessToday = subscription.FamilyGroupId.HasValue ? BusinessCalendarDate.KuwaitToday(_clock.UtcNow) : _clock.TodayUtc;

        var policy = await SubscriptionFreezePolicyDefaults.ReadAsync(_db, cancellationToken);
        var policyError = await SubscriptionFreezePolicyDefaults.ValidateRequestAsync(_db, subscription.Id,
            request.FreezeStartDate, request.FreezeEndDate, businessToday, policy, cancellationToken);
        if (policyError is not null) return Error.Validation("subscription.freeze_policy", policyError);
        if (request.FreezeStartDate < subscription.StartDate || request.FreezeEndDate > subscription.EndDate)
            return Error.Validation("subscription.freeze_range", "Freeze dates must be within the subscription period.");
        var earliestStart = businessToday.AddDays(policy.StartDelayDays);
        if (request.FreezeStartDate < earliestStart)
            return Error.Validation(MessageCodes.Validation.InclusiveBetween, $"Freeze must start on or after {earliestStart:yyyy-MM-dd}.");

        var days = request.FreezeEndDate.DayNumber - request.FreezeStartDate.DayNumber + 1;
        var maxAllowedFreeze = policy.DefaultDurationDays;

        if (days > maxAllowedFreeze)
        {
            return Error.Validation(MessageCodes.Validation.InclusiveBetween, $"Maximum allowed freeze days for a {subscription.DurationDays}-day subscription is {maxAllowedFreeze} days.");
        }

        var lockedDays = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == request.SubscriptionId &&
                        x.ServiceDate >= request.FreezeStartDate &&
                        x.ServiceDate <= request.FreezeEndDate &&
                        (x.LockAtUtc != null || x.Status == SubscriptionDayStatus.Locked72h ||
                         x.Status == SubscriptionDayStatus.SentToRestaurant ||
                         x.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                         x.Status == SubscriptionDayStatus.Preparing || x.Status == SubscriptionDayStatus.OutForDelivery ||
                         x.Status == SubscriptionDayStatus.Delivered || x.Status == SubscriptionDayStatus.Cancelled ||
                         x.Status == SubscriptionDayStatus.Frozen || x.Status == SubscriptionDayStatus.Failed))
            .Select(x => x.ServiceDate)
            .ToListAsync(cancellationToken);

        if (lockedDays.Count > 0)
        {
            var latestLocked = lockedDays.Max();
            var suggestedStart = latestLocked.AddDays(1);
            return Error.Validation("freeze.locked_days_cannot_be_frozen",
                $"لا يمكن تجميد الأيام المقفولة لتحضير المطبخ ({lockedDays.Count} يوم). أقرب تاريخ متاح لبدء التجميد هو {suggestedStart:yyyy-MM-dd}.");
        }

        var renewalOverlap = await ValidateFreezeRenewalOverlapAsync(subscription, request.FreezeStartDate,
            request.FreezeEndDate, (await SubscriptionFreezePolicyDefaults.ReadAsync(_db, cancellationToken)).ExtendSubscriptionEnd,
            cancellationToken);
        if (renewalOverlap is not null) return renewalOverlap;
        var lineagePolicy = await SubscriptionFreezePolicyDefaults.ReadAsync(_db, cancellationToken);
        var lineageError = await ValidateFreezeDeliveryLineageAsync(subscription, request.FreezeStartDate,
            request.FreezeEndDate, lineagePolicy.ExtendSubscriptionEnd, cancellationToken);
        if (lineageError is not null) return lineageError;

        var selectedDayIds = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == request.SubscriptionId &&
                        x.ServiceDate >= request.FreezeStartDate &&
                        x.ServiceDate <= request.FreezeEndDate &&
                        (x.Status == SubscriptionDayStatus.CustomerSelected ||
                         x.Status == SubscriptionDayStatus.AutoSelected))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var daysWithSelections = selectedDayIds.Count;
        if (daysWithSelections == 0)
        {
            var dayIdsInRange = await _db.SubscriptionDays.AsNoTracking()
                .Where(x => x.SubscriptionId == request.SubscriptionId &&
                            x.ServiceDate >= request.FreezeStartDate &&
                            x.ServiceDate <= request.FreezeEndDate)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            if (dayIdsInRange.Count > 0)
            {
                daysWithSelections = await _db.DailyMealSelections.AsNoTracking()
                    .Where(m => dayIdsInRange.Contains(m.SubscriptionDayId))
                    .Select(m => m.SubscriptionDayId)
                    .Distinct()
                    .CountAsync(cancellationToken);
            }
        }

        if (daysWithSelections > 0 && !request.AcknowledgeLostSelections)
        {
            var replacementNotice = policy.ExtendSubscriptionEnd
                ? " وإضافة أيام خدمة تعويضية في نهاية الاشتراك"
                : " دون إضافة أيام تعويضية أو تمديد نهاية الاشتراك";
            return Error.Validation("freeze.confirm_overwrite_selections",
                $"أنت قمت باختيار وجبات في {daysWithSelections} يوم ضمن فترة التجميد، وسيتم إلغاء هذه الاختيارات{replacementNotice}. يرجى تأكيد الموافقة للمتابعة.");
        }

        var preparedFreeze = await PrepareFreezeAsync(subscription, request.FreezeStartDate, request.FreezeEndDate, policy.ExtendSubscriptionEnd, cancellationToken);
        if (preparedFreeze.IsFailure) return preparedFreeze.Error;

        var freezeReq = new SubscriptionFreezeRequest
        {
            SubscriptionId = request.SubscriptionId,
            FreezeStartDate = request.FreezeStartDate,
            FreezeEndDate = request.FreezeEndDate,
            FreezeDays = days,
            Reason = request.Reason?.Trim(),
            Status = VerificationStatus.Approved,
            ProcessedAtUtc = _clock.UtcNow,
        };

        if (request.FreezeStartDate <= businessToday && businessToday <= request.FreezeEndDate)
        {
            subscription.Status = SubscriptionStatus.Frozen;
        }

        subscription.LastOperationAtUtc = _clock.UtcNow;

        _db.SubscriptionFreezeRequests.Add(freezeReq);

        var frozenCount = await ApplyRecordedFreezeAsync(freezeReq,
            subscription,
            request.FreezeStartDate,
            request.FreezeEndDate,
            policy.ExtendSubscriptionEnd,
            preparedFreeze.Value,
            cancellationToken);

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "FREEZE_APPLIED",
            PreviousStatus = subscription.Status,
            NewStatus = subscription.Status,
            Reason = $"Freeze applied automatically for {days} days from {request.FreezeStartDate} to {request.FreezeEndDate}. Locked {frozenCount} days, extended end date to {subscription.EndDate}",
            PerformedByUserId = subscription.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "SubscriptionFreezeApplied",
            nameof(SubscriptionFreezeRequest),
            freezeReq.Id,
            After: new { freezeReq.SubscriptionId, freezeReq.FreezeStartDate, freezeReq.FreezeEndDate, freezeReq.FreezeDays, NewEndDate = subscription.EndDate },
            SourceChannel: "customer-app"), cancellationToken);

        return MapFreezeDto(freezeReq);
    }

    public async Task<Result<SubscriptionFreezeRequestDto>> Handle(ApproveSubscriptionFreezeCommand request, CancellationToken cancellationToken)
    {
        if (_transactions is null) return await ApproveFreezeCore(request, cancellationToken);
        var subscriptionId = await _db.SubscriptionFreezeRequests.AsNoTracking()
            .Where(x => x.Id == request.FreezeRequestId).Select(x => (Guid?)x.SubscriptionId).FirstOrDefaultAsync(cancellationToken);
        if (subscriptionId is null) return Error.NotFound(MessageCodes.Common.NotFound);
        return await _transactions.ExecuteResultAsync<SubscriptionFreezeRequestDto>(async ct =>
        {
            await LockFreezeScopeAsync(subscriptionId.Value, ct);
            await ReloadFreezeStateAsync(subscriptionId.Value, ct);
            if (_db is DbContext context)
                foreach (var entry in context.ChangeTracker.Entries<SubscriptionFreezeRequest>().Where(x => x.Entity.Id == request.FreezeRequestId).ToList()) await entry.ReloadAsync(ct);
            return await ApproveFreezeCore(request, ct);
        }, cancellationToken);
    }

    private async Task<Result<SubscriptionFreezeRequestDto>> ApproveFreezeCore(ApproveSubscriptionFreezeCommand request, CancellationToken cancellationToken)
    {
        var freezeReq = await _db.SubscriptionFreezeRequests
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.Id == request.FreezeRequestId, cancellationToken);

        if (freezeReq is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Freeze request with ID '{request.FreezeRequestId}' was not found.");
        }

        if (freezeReq.Status == VerificationStatus.Approved)
        {
            return MapFreezeDto(freezeReq);
        }

        if (freezeReq.Status != VerificationStatus.Submitted)
        {
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, "Freeze request has already been processed.");
        }

        if (request.Approved)
        {
            if (freezeReq.Subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
                return Error.Validation(MessageCodes.Validation.InvalidStateTransition);
            if (freezeReq.FreezeStartDate < freezeReq.Subscription.StartDate || freezeReq.FreezeEndDate > freezeReq.Subscription.EndDate)
                return Error.Validation("subscription.freeze_range");
            var businessToday = freezeReq.Subscription.FamilyGroupId.HasValue
                ? BusinessCalendarDate.KuwaitToday(_clock.UtcNow) : _clock.TodayUtc;
            var currentPolicy = await SubscriptionFreezePolicyDefaults.ReadAsync(_db, cancellationToken);
            var policyError = await SubscriptionFreezePolicyDefaults.ValidateRequestAsync(_db, freezeReq.SubscriptionId,
                freezeReq.FreezeStartDate, freezeReq.FreezeEndDate, businessToday, currentPolicy, cancellationToken, freezeReq.Id);
            if (policyError is not null) return Error.Validation("subscription.freeze_policy", policyError);
            var targetDays = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == freezeReq.SubscriptionId &&
                x.ServiceDate >= freezeReq.FreezeStartDate && x.ServiceDate <= freezeReq.FreezeEndDate).ToListAsync(cancellationToken);
            if (targetDays.Any(x => !MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalDeadlineRules.IsMutable(x)))
                return Error.Conflict("freeze.locked_days_cannot_be_frozen", "Committed service days cannot be frozen.");
                        var renewalOverlap = await ValidateFreezeRenewalOverlapAsync(freezeReq.Subscription, freezeReq.FreezeStartDate,
                freezeReq.FreezeEndDate, currentPolicy.ExtendSubscriptionEnd, cancellationToken);
            if (renewalOverlap is not null) return renewalOverlap;
            var lineageError = await ValidateFreezeDeliveryLineageAsync(freezeReq.Subscription, freezeReq.FreezeStartDate,
                freezeReq.FreezeEndDate, currentPolicy.ExtendSubscriptionEnd, cancellationToken);
            if (lineageError is not null) return lineageError;
            var preparedFreeze = await PrepareFreezeAsync(freezeReq.Subscription, freezeReq.FreezeStartDate, freezeReq.FreezeEndDate, currentPolicy.ExtendSubscriptionEnd, cancellationToken);
            if (preparedFreeze.IsFailure) return preparedFreeze.Error;
            freezeReq.Status = VerificationStatus.Approved;
            freezeReq.ProcessedAtUtc = _clock.UtcNow;

            if (freezeReq.FreezeStartDate <= businessToday && businessToday <= freezeReq.FreezeEndDate)
            {
                freezeReq.Subscription.Status = SubscriptionStatus.Frozen;
            }
            freezeReq.Subscription.LastOperationAtUtc = _clock.UtcNow;

            var frozenCount = await ApplyRecordedFreezeAsync(freezeReq,
                freezeReq.Subscription,
                freezeReq.FreezeStartDate,
                freezeReq.FreezeEndDate,
                currentPolicy.ExtendSubscriptionEnd,
                preparedFreeze.Value,
                cancellationToken);

            var op = new SubscriptionOperation
            {
                SubscriptionId = freezeReq.SubscriptionId,
                OperationType = "FREEZE_APPROVED",
                PreviousStatus = SubscriptionStatus.Active,
                NewStatus = freezeReq.Subscription.Status,
                Reason = $"Freeze approved for {freezeReq.FreezeDays} days. Locked {frozenCount} days, new end date: {freezeReq.Subscription.EndDate}",
                PerformedByUserId = Guid.Empty,
                PerformedByUserType = UserType.Admin,
                TimestampUtc = _clock.UtcNow,
            };

            _db.SubscriptionOperations.Add(op);
        }
        else
        {
            freezeReq.Status = VerificationStatus.Rejected;
            freezeReq.ProcessedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            request.Approved ? "SubscriptionFreezeApproved" : "SubscriptionFreezeRejected",
            nameof(SubscriptionFreezeRequest),
            freezeReq.Id,
            After: new { freezeReq.SubscriptionId, freezeReq.Status, freezeReq.FreezeDays },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapFreezeDto(freezeReq);
    }

    public async Task<Result<SubscriptionUpgradeDto>> Handle(RequestSubscriptionUpgradeCommand request, CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == request.SubscriptionId, cancellationToken);
        if (subscription is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{request.SubscriptionId}' was not found.");
        }

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
        {
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, "Can only upgrade active or scheduled subscriptions.");
        }

        if (request.TargetTier <= subscription.SelectedTier)
        {
            return Error.Validation(MessageCodes.Validation.InclusiveBetween, "Target tier must be higher than current tier.");
        }

        var option = await _upgradeQuotes.BuildForTargetAsync(subscription, request.TargetTier, cancellationToken);
        if (option is null)
        {
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, "Upgrade pricing is unavailable for the requested tier.");
        }

        var currency = subscription.PaidAmount.CurrencyCode;
        var upgrade = new SubscriptionTierUpgrade
        {
            SubscriptionId = subscription.Id,
            FromTier = subscription.SelectedTier,
            ToTier = request.TargetTier,
            PriceDifference = new Money(option.PriceDifference, currency),
            QuotedRemainingDays = option.RemainingServiceDays,
            QuotedTotalDue = new Money(option.PriceDifference, currency),
            QuoteJson = SubscriptionTierUpgradeQuoteBuilder.SerializeQuoteJson(option),
            Status = VerificationStatus.Submitted,
        };

        _db.SubscriptionTierUpgrades.Add(upgrade);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "SubscriptionUpgradeRequested",
            nameof(SubscriptionTierUpgrade),
            upgrade.Id,
            After: new { upgrade.SubscriptionId, upgrade.FromTier, upgrade.ToTier, ProRataDelta = option.PriceDifference },
            SourceChannel: "customer-app"), cancellationToken);

        return MapUpgradeDto(upgrade);
    }

    public async Task<Result<SubscriptionUpgradeDto>> Handle(ApproveSubscriptionUpgradeCommand request, CancellationToken cancellationToken)
    {
        var upgrade = await _db.SubscriptionTierUpgrades
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.Id == request.UpgradeId, cancellationToken);

        if (upgrade is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Upgrade record with ID '{request.UpgradeId}' was not found.");
        }

        if (upgrade.Status != VerificationStatus.Submitted)
        {
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, "Upgrade request has already been processed.");
        }

        if (!string.IsNullOrWhiteSpace(upgrade.PaymentId))
        {
            return Error.Validation("upgrade.payment_required", "This upgrade requires customer payment and cannot be approved manually.");
        }

        if (request.Approved)
        {
            upgrade.Status = VerificationStatus.Approved;
            upgrade.EffectiveAtUtc = _clock.UtcNow;

            var oldTier = upgrade.Subscription.SelectedTier;
            upgrade.Subscription.SelectedTier = upgrade.ToTier;
            upgrade.Subscription.LastOperationAtUtc = _clock.UtcNow;

            // Log operation
            var op = new SubscriptionOperation
            {
                SubscriptionId = upgrade.SubscriptionId,
                OperationType = "UPGRADE_APPROVED",
                PreviousStatus = upgrade.Subscription.Status,
                NewStatus = upgrade.Subscription.Status,
                Reason = $"Tier upgraded from {oldTier} to {upgrade.ToTier}",
                PerformedByUserId = Guid.Empty,
                PerformedByUserType = UserType.Admin,
                TimestampUtc = _clock.UtcNow,
            };

            _db.SubscriptionOperations.Add(op);
        }
        else
        {
            upgrade.Status = VerificationStatus.Rejected;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            request.Approved ? "SubscriptionUpgradeApproved" : "SubscriptionUpgradeRejected",
            nameof(SubscriptionTierUpgrade),
            upgrade.Id,
            After: new { upgrade.SubscriptionId, upgrade.Status, upgrade.ToTier },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapUpgradeDto(upgrade);
    }

    public async Task<Result<SubscriptionBatchProcessingResultDto>> Handle(ProcessSubscriptionRenewalsAndExpirationsCommand request, CancellationToken cancellationToken)
    {
        var utcToday = DateOnly.FromDateTime(_clock.UtcNow.Date);
        var kuwaitToday = BusinessCalendarDate.KuwaitToday(_clock.UtcNow);
        var activeDueIds = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.Active
                && (x.FamilyGroupId.HasValue ? x.EndDate < kuwaitToday : x.EndDate <= utcToday))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        int renewed = 0;
        int expired = 0;
        var details = new List<string>();

        foreach (var subscriptionId in activeDueIds)
        {
            async Task<bool> ExpireIfStillDueAsync(CancellationToken token)
            {
                var familyGroupId = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == subscriptionId)
                    .Select(x => x.FamilyGroupId).SingleOrDefaultAsync(token);
                if (familyGroupId is Guid groupId && _locks is not null)
                    await _locks.LockFamilyGroupAsync(groupId, token);
                if (_locks is not null) await _locks.LockSubscriptionAsync(subscriptionId, token);

                var sub = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, token);
                if (sub is null) return false;
                if (_db is DbContext context) await context.Entry(sub).ReloadAsync(token);
                if (sub.Status != SubscriptionStatus.Active) return false;

                var now = _clock.UtcNow;
                var operationDate = sub.FamilyGroupId.HasValue
                    ? BusinessCalendarDate.KuwaitToday(now)
                    : DateOnly.FromDateTime(now.Date);
                var due = sub.FamilyGroupId.HasValue ? sub.EndDate < operationDate : sub.EndDate <= operationDate;
                if (!due) return false;

                sub.Status = SubscriptionStatus.Expired;
                sub.LastOperationAtUtc = now;
                _db.SubscriptionOperations.Add(new SubscriptionOperation
                {
                    SubscriptionId = sub.Id,
                    OperationType = "EXPIRED",
                    PreviousStatus = SubscriptionStatus.Active,
                    NewStatus = SubscriptionStatus.Expired,
                    Reason = $"Subscription reached end date {sub.EndDate} and was marked expired",
                    PerformedByUserId = Guid.Empty,
                    PerformedByUserType = UserType.Admin,
                    TimestampUtc = now,
                });
                await _db.SaveChangesAsync(token);
                details.Add($"Subscription '{sub.Id}' expired on {sub.EndDate}");
                return true;
            }

            var didExpire = _transactions is null
                ? await ExpireIfStillDueAsync(cancellationToken)
                : await _transactions.ExecuteAsync(ExpireIfStillDueAsync, cancellationToken);
            if (didExpire) expired++;
        }

        await _audit.LogAsync(new AuditEntry(
            "SubscriptionBatchProcessingCompleted",
            nameof(Subscription),
            Guid.Empty,
            After: new { TotalProcessed = expired, Renewed = renewed, Expired = expired },
            SourceChannel: "background-worker"), cancellationToken);

        return new SubscriptionBatchProcessingResultDto(expired, renewed, expired, details);
    }

    private static SubscriptionFreezeRequestDto MapFreezeDto(SubscriptionFreezeRequest f) => new(
        f.Id,
        f.SubscriptionId,
        f.FreezeStartDate,
        f.FreezeEndDate,
        f.FreezeDays,
        f.Reason,
        f.Status,
        f.ApprovedByUserId,
        f.ProcessedAtUtc,
        f.CreatedAtUtc);

    private static SubscriptionUpgradeDto MapUpgradeDto(SubscriptionTierUpgrade u) => new(
        u.Id,
        u.SubscriptionId,
        u.FromTier,
        u.ToTier,
        u.PriceDifference.Amount,
        u.Status,
        u.EffectiveAtUtc,
        u.CreatedAtUtc);

    public async Task<Result<IReadOnlyList<AdminSubscriptionFreezeRequestRowDto>>> Handle(
        GetSubscriptionFreezeRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.SubscriptionFreezeRequests
            .Include(x => x.Subscription)
            .AsNoTracking()
            .AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status.Value);
        }

        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var customerIds = items.Select(x => x.Subscription.CustomerUserId).Distinct().ToList();

        var identityById = _users is null
            ? new Dictionary<Guid, UserIdentityDto>()
            : (await _users.GetUserIdentitiesAsync(customerIds, cancellationToken))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());

        var result = items.Select(f =>
        {
            identityById.TryGetValue(f.Subscription.CustomerUserId, out var user);
            var customerName = user?.FullName ?? "عميل";
            return new AdminSubscriptionFreezeRequestRowDto(
                f.Id,
                f.SubscriptionId,
                f.Subscription.CustomerUserId,
                customerName,
                f.FreezeStartDate,
                f.FreezeEndDate,
                f.FreezeDays,
                f.Reason,
                f.Status,
                f.ApprovedByUserId,
                f.ProcessedAtUtc,
                f.CreatedAtUtc);
        }).ToList();

        return result;
    }

    public async Task<Result<IReadOnlyList<AdminSubscriptionUpgradeRowDto>>> Handle(
        GetSubscriptionUpgradeRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.SubscriptionTierUpgrades
            .Include(x => x.Subscription)
            .AsNoTracking()
            .AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status.Value);
        }

        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var customerIds = items.Select(x => x.Subscription.CustomerUserId).Distinct().ToList();

        var identityById = _users is null
            ? new Dictionary<Guid, UserIdentityDto>()
            : (await _users.GetUserIdentitiesAsync(customerIds, cancellationToken))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());

        var result = items.Select(u =>
        {
            identityById.TryGetValue(u.Subscription.CustomerUserId, out var user);
            var customerName = user?.FullName ?? "عميل";
            return new AdminSubscriptionUpgradeRowDto(
                u.Id,
                u.SubscriptionId,
                u.Subscription.CustomerUserId,
                customerName,
                u.FromTier,
                u.ToTier,
                u.PriceDifference.Amount,
                u.Status,
                u.EffectiveAtUtc,
                u.CreatedAtUtc);
        }).ToList();

        return result;
    }

    public async Task<Result<PagedResult<AdminSubscriptionCancellationRequestDto>>> Handle(
        GetSubscriptionCancellationRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var legacySource = from operation in _db.SubscriptionOperations.AsNoTracking()
            join subscription in _db.Subscriptions.AsNoTracking() on operation.SubscriptionId equals subscription.Id
            where operation.OperationType == "CANCELLATION_PROCESSED" || operation.OperationType == "CANCELLATION_REQUESTED"
            select new { Id = operation.Id, SubscriptionId = operation.SubscriptionId, CustomerUserId = subscription.CustomerUserId,
                MemberId = subscription.FamilyMemberId, FamilyGroupId = (Guid?)null, BatchId = (Guid?)null,
                BatchLineId = (Guid?)null, Reason = operation.Reason, RequestedAtUtc = operation.TimestampUtc,
                StatusText = (string?)null, LegacyStatus = (int?)operation.NewStatus, IsFamilySettlement = false,
                ActorUserId = (Guid?)null, PayerCustomerUserId = (Guid?)null };
        var familySource = from cancellation in _db.SubscriptionCancellations.AsNoTracking()
            join subscription in _db.Subscriptions.AsNoTracking() on cancellation.SubscriptionId equals subscription.Id
            where cancellation.SettlementKind == "FamilyCancellation" && cancellation.FamilyGroupId.HasValue
            select new { Id = cancellation.Id, SubscriptionId = cancellation.SubscriptionId, CustomerUserId = subscription.CustomerUserId,
                MemberId = subscription.FamilyMemberId, FamilyGroupId = cancellation.FamilyGroupId, BatchId = cancellation.BatchId,
                BatchLineId = cancellation.BatchLineId, Reason = (string?)null, RequestedAtUtc = cancellation.RequestedAtUtc,
                StatusText = cancellation.Status, LegacyStatus = (int?)null, IsFamilySettlement = true,
                ActorUserId = cancellation.ActorUserId, PayerCustomerUserId = cancellation.PayerCustomerUserId };
        var merged = legacySource.Concat(familySource);
        var totalCount = await merged.CountAsync(cancellationToken);
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var safeSkip = (int)Math.Min(int.MaxValue, ((long)page.NormalizedPage - 1) * page.NormalizedPageSize);
        var selectedItems = await merged.OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id).ThenBy(x => x.IsFamilySettlement)
            .Skip(safeSkip).Take(page.NormalizedPageSize).ToListAsync(cancellationToken);
        var items = selectedItems.Select(x => new AdminCancellationProjection(x.Id, x.SubscriptionId, x.CustomerUserId,
            x.MemberId, x.FamilyGroupId, x.BatchId, x.BatchLineId, x.Reason, x.RequestedAtUtc, x.StatusText,
            x.LegacyStatus, x.IsFamilySettlement, x.ActorUserId, x.PayerCustomerUserId)).ToList();
        var familyCancellationIds = items.Where(x => x.IsFamilySettlement).Select(x => x.Id).ToArray();
        var refundRows = familyCancellationIds.Length == 0 ? [] : await _db.RefundCalculations.AsNoTracking()
            .Where(x => familyCancellationIds.Contains(x.CancellationId))
            .Select(x => new { x.Id, x.CancellationId, x.CalculatedAtUtc, x.FinalCashRefund.Amount, x.FinalCashRefund.CurrencyCode })
            .ToListAsync(cancellationToken);
        var latestRefundByCancellation = refundRows.GroupBy(x => x.CancellationId).ToDictionary(g => g.Key,
            g => g.OrderByDescending(x => x.CalculatedAtUtc).ThenBy(x => x.Id).First());
        var customerIds = items.Select(x => x.CustomerUserId).Distinct().ToList();

        var identityById = _users is null
            ? new Dictionary<Guid, UserIdentityDto>()
            : (await _users.GetUserIdentitiesAsync(customerIds, cancellationToken))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());

        var result = items.Select(op =>
        {
            identityById.TryGetValue(op.CustomerUserId, out var user);
            var customerName = user?.FullName ?? "عميل";
            latestRefundByCancellation.TryGetValue(op.Id, out var refund);
            return new AdminSubscriptionCancellationRequestDto(
                op.Id, op.SubscriptionId, op.CustomerUserId,
                customerName,
                op.Reason,
                op.RequestedAtUtc,
                op.StatusText ?? ((SubscriptionStatus)op.LegacyStatus!.Value).ToString(),
                op.IsFamilySettlement, op.FamilyGroupId, op.MemberId, op.BatchId, op.BatchLineId,
                op.ActorUserId, op.PayerCustomerUserId, refund?.Amount, refund?.CurrencyCode, refund?.Id);
        }).ToList();

        return new PagedResult<AdminSubscriptionCancellationRequestDto>(result, totalCount,
            page.NormalizedPage, page.NormalizedPageSize);
    }

    private sealed record AdminCancellationProjection(Guid Id, Guid SubscriptionId, Guid CustomerUserId,
        Guid? MemberId, Guid? FamilyGroupId, Guid? BatchId, Guid? BatchLineId, string? Reason,
        DateTime RequestedAtUtc, string? StatusText, int? LegacyStatus, bool IsFamilySettlement, Guid? ActorUserId,
        Guid? PayerCustomerUserId);

    private async Task<Error?> ValidateFreezeRenewalOverlapAsync(Subscription subscription, DateOnly freezeStart,
        DateOnly freezeEnd, bool extend, CancellationToken ct)
    {
        if (!extend || subscription.FamilyGroupId is not Guid groupId || subscription.FamilyMemberId is not Guid memberId)
            return null;
        var serviceDates = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate).Select(x => x.ServiceDate).ToListAsync(ct);
        var frozenCount = serviceDates.Count(x => x >= freezeStart && x <= freezeEnd);
        if (frozenCount == 0 || serviceDates.Count == 0) return null;
        var replacements = _schedule.GetServiceDates(serviceDates[serviceDates.Count - 1].AddDays(1), frozenCount,
            subscription.IncludeFriday);
        if (replacements.Count == 0) return Error.Conflict("family.renewal_dates_overlap");
        var paidStatuses = new[] { SubscriptionStatus.Paid, SubscriptionStatus.Active, SubscriptionStatus.Scheduled,
            SubscriptionStatus.Frozen, SubscriptionStatus.SuspendedForReview, SubscriptionStatus.CancellationPending };
        var overlaps = await _db.SubscriptionDays.AsNoTracking().AnyAsync(day => replacements.Contains(day.ServiceDate)
            && day.Status != SubscriptionDayStatus.Cancelled && day.Status != SubscriptionDayStatus.Frozen
            && _db.Subscriptions.Any(other => other.Id == day.SubscriptionId && other.FamilyGroupId == groupId
                && other.FamilyMemberId == memberId && other.Id != subscription.Id && other.PaymentVerifiedAtUtc.HasValue
                && paidStatuses.Contains(other.Status)), ct);
        return overlaps ? Error.Conflict("family.renewal_dates_overlap") : null;
    }

    private async Task<Error?> ValidateFreezeDeliveryLineageAsync(Subscription subscription, DateOnly freezeStart,
        DateOnly freezeEnd, bool extend, CancellationToken ct)
    {
        if (!extend || subscription.FamilyGroupId is null) return null;
        var days = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate).ToListAsync(ct);
        if (days.Count == 0) return null;
        var frozenCount = days.Count(x => x.ServiceDate >= freezeStart && x.ServiceDate <= freezeEnd);
        if (frozenCount == 0) return null;
        var replacementDates = _schedule.GetServiceDates(days[days.Count - 1].ServiceDate.AddDays(1), frozenCount, subscription.IncludeFriday);
        if (replacementDates.Count == 0) return Error.Conflict("family.delivery_snapshot_review_required");
        var amendments = await _db.FamilySubscriptionDeliveryAmendments.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.EffectiveFromDate <= replacementDates[replacementDates.Count - 1])
            .OrderByDescending(x => x.Version).ToListAsync(ct);
        foreach (var amendment in amendments)
        {
            try
            {
                if (JsonSerializer.Deserialize<List<Guid>>(amendment.AffectedDayIdsJson) is null)
                    return Error.Conflict("family.delivery_snapshot_review_required");
            }
            catch (JsonException)
            {
                return Error.Conflict("family.delivery_snapshot_review_required");
            }
        }
        return null;
    }

    private async Task LockFreezeScopeAsync(Guid subscriptionId, CancellationToken ct)
    {
        if (_locks is null) return;
        var groupId = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == subscriptionId)
            .Select(x => x.FamilyGroupId).SingleOrDefaultAsync(ct);
        if (groupId is Guid id) await _locks.LockFamilyGroupAsync(id, ct);
        await _locks.LockSubscriptionAsync(subscriptionId, ct);
        await _locks.LockSubscriptionDaysAsync(subscriptionId, ct);
    }

    private async Task<int> ApplyRecordedFreezeAsync(SubscriptionFreezeRequest request, Subscription subscription,
        DateOnly start, DateOnly end, bool extend, Freeze.PreparedSubscriptionFreeze preparation, CancellationToken ct)
    {
        var before = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id).Select(x => x.ServiceDate).ToListAsync(ct);
        var count = await _freezePreparation!.ApplyAsync(subscription, preparation, extend, ct);
        var trackedDays = (_db as DbContext)?.ChangeTracker.Entries<SubscriptionDay>()
            .Where(x => x.Entity.SubscriptionId == subscription.Id).Select(x => x.Entity).ToList() ?? new();
        var outcome = new Freeze.FreezeAppliedOutcome(
            trackedDays.Where(x => x.Status == SubscriptionDayStatus.Frozen && x.ServiceDate >= start && x.ServiceDate <= end).Select(x => x.ServiceDate).Order().ToList(),
            trackedDays.Where(x => !before.Contains(x.ServiceDate)).Select(x => x.ServiceDate).Order().ToList(), subscription.EndDate);
        _db.SystemSettings.Add(new MealMate.Domain.AdminDashboard.SystemSetting
        { Key = Freeze.FreezeAppliedOutcome.Prefix + request.Id, ValueJson = System.Text.Json.JsonSerializer.Serialize(outcome), CreatedAtUtc = _clock.UtcNow });
        return count;
    }

    private async Task ReloadFreezeStateAsync(Guid subscriptionId, CancellationToken ct)
    {
        if (_db is not DbContext context) return;
        foreach (var entry in context.ChangeTracker.Entries<Subscription>().Where(x => x.Entity.Id == subscriptionId).ToList())
            await entry.ReloadAsync(ct);
        foreach (var entry in context.ChangeTracker.Entries<SubscriptionDay>().Where(x => x.Entity.SubscriptionId == subscriptionId).ToList())
            await entry.ReloadAsync(ct);
        foreach (var entry in context.ChangeTracker.Entries<MealMate.Domain.RestaurantOperations.CapacityHold>().Where(x => x.Entity.SubscriptionId == subscriptionId).ToList())
            await entry.ReloadAsync(ct);
    }
    private async Task<Result<Freeze.PreparedSubscriptionFreeze>> PrepareFreezeAsync(
        Subscription subscription, DateOnly start, DateOnly end, bool extend, CancellationToken ct)
    {
        if (_freezePreparation is null)
            return Error.Conflict("freeze.preparation_unavailable");
        return await _freezePreparation.PrepareAsync(subscription, start, end, extend, ct);
    }
}
