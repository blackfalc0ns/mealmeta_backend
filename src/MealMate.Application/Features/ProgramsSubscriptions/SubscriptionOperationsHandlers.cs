using FluentValidation;
using MealMate.Application.Common.Abstractions;
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
    IQueryHandler<GetSubscriptionCancellationRequestsQuery, IReadOnlyList<AdminSubscriptionCancellationRequestDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionTierUpgradeQuoteBuilder _upgradeQuotes;
    private readonly IUserAccountService? _users;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public SubscriptionOperationsHandlers(
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ISubscriptionTierUpgradeQuoteBuilder upgradeQuotes,
        IUserAccountService? users = null,
        ISubscriptionScheduleService? schedule = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _upgradeQuotes = upgradeQuotes;
        _users = users;
        _schedule = schedule ?? new SubscriptionScheduleService(clock);
        _transactions = transactions;
        _locks = locks;
    }

    public async Task<Result<SubscriptionFreezeRequestDto>> Handle(RequestSubscriptionFreezeCommand request, CancellationToken cancellationToken)
    {
        if (_transactions is null) return await RequestFreezeCore(request, cancellationToken);
        return await _transactions.ExecuteResultAsync<SubscriptionFreezeRequestDto>(async ct =>
        {
            if (_locks is not null) await _locks.LockSubscriptionAsync(request.SubscriptionId, ct);
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

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
        {
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, $"Cannot request freeze for subscription in state '{subscription.Status}'.");
        }

        var policy = await SubscriptionFreezePolicyDefaults.ReadAsync(_db, cancellationToken);
        var policyError = await SubscriptionFreezePolicyDefaults.ValidateRequestAsync(_db, subscription.Id,
            request.FreezeStartDate, request.FreezeEndDate, _clock.TodayUtc, policy, cancellationToken);
        if (policyError is not null) return Error.Validation("subscription.freeze_policy", policyError);
        if (request.FreezeStartDate < subscription.StartDate || request.FreezeEndDate > subscription.EndDate)
            return Error.Validation("subscription.freeze_range", "Freeze dates must be within the subscription period.");
        var earliestStart = _clock.TodayUtc.AddDays(policy.StartDelayDays);
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

        if (request.FreezeStartDate <= _clock.TodayUtc && _clock.TodayUtc <= request.FreezeEndDate)
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
            if (_locks is not null) await _locks.LockSubscriptionAsync(subscriptionId.Value, ct);
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
            var currentPolicy = await SubscriptionFreezePolicyDefaults.ReadAsync(_db, cancellationToken);
            var policyError = await SubscriptionFreezePolicyDefaults.ValidateRequestAsync(_db, freezeReq.SubscriptionId,
                freezeReq.FreezeStartDate, freezeReq.FreezeEndDate, _clock.TodayUtc, currentPolicy, cancellationToken, freezeReq.Id);
            if (policyError is not null) return Error.Validation("subscription.freeze_policy", policyError);
            var targetDays = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == freezeReq.SubscriptionId &&
                x.ServiceDate >= freezeReq.FreezeStartDate && x.ServiceDate <= freezeReq.FreezeEndDate).ToListAsync(cancellationToken);
            if (targetDays.Any(x => !MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalDeadlineRules.IsMutable(x)))
                return Error.Conflict("freeze.locked_days_cannot_be_frozen", "Committed service days cannot be frozen.");
            freezeReq.Status = VerificationStatus.Approved;
            freezeReq.ProcessedAtUtc = _clock.UtcNow;

            var policy = await SubscriptionFreezePolicyDefaults.ReadAsync(_db, cancellationToken);

            if (freezeReq.FreezeStartDate <= _clock.TodayUtc && _clock.TodayUtc <= freezeReq.FreezeEndDate)
            {
                freezeReq.Subscription.Status = SubscriptionStatus.Frozen;
            }
            freezeReq.Subscription.LastOperationAtUtc = _clock.UtcNow;

            var frozenCount = await ApplyRecordedFreezeAsync(freezeReq,
                freezeReq.Subscription,
                freezeReq.FreezeStartDate,
                freezeReq.FreezeEndDate,
                policy.ExtendSubscriptionEnd,
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
        var today = DateOnly.FromDateTime(_clock.UtcNow.Date);

        var activeDue = await _db.Subscriptions
            .Where(x => x.Status == SubscriptionStatus.Active && x.EndDate <= today)
            .ToListAsync(cancellationToken);

        int renewed = 0;
        int expired = 0;
        var details = new List<string>();

        foreach (var sub in activeDue)
        {
            sub.Status = SubscriptionStatus.Expired;
            sub.LastOperationAtUtc = _clock.UtcNow;
            expired++;

            var op = new SubscriptionOperation
            {
                SubscriptionId = sub.Id,
                OperationType = "EXPIRED",
                PreviousStatus = SubscriptionStatus.Active,
                NewStatus = SubscriptionStatus.Expired,
                Reason = $"Subscription reached end date {sub.EndDate} and was marked expired",
                PerformedByUserId = Guid.Empty,
                PerformedByUserType = UserType.Admin,
                TimestampUtc = _clock.UtcNow,
            };

            _db.SubscriptionOperations.Add(op);
            details.Add($"Subscription '{sub.Id}' expired on {sub.EndDate}");
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "SubscriptionBatchProcessingCompleted",
            nameof(Subscription),
            Guid.Empty,
            After: new { TotalProcessed = activeDue.Count, Renewed = renewed, Expired = expired },
            SourceChannel: "background-worker"), cancellationToken);

        return new SubscriptionBatchProcessingResultDto(activeDue.Count, renewed, expired, details);
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

    public async Task<Result<IReadOnlyList<AdminSubscriptionCancellationRequestDto>>> Handle(
        GetSubscriptionCancellationRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var items = await _db.SubscriptionOperations
            .Include(x => x.Subscription)
            .Where(x => x.OperationType == "CANCELLATION_PROCESSED" || x.OperationType == "CANCELLATION_REQUESTED")
            .OrderByDescending(x => x.TimestampUtc)
            .ToListAsync(cancellationToken);

        var customerIds = items.Select(x => x.Subscription.CustomerUserId).Distinct().ToList();

        var identityById = _users is null
            ? new Dictionary<Guid, UserIdentityDto>()
            : (await _users.GetUserIdentitiesAsync(customerIds, cancellationToken))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());

        var result = items.Select(op =>
        {
            identityById.TryGetValue(op.Subscription.CustomerUserId, out var user);
            var customerName = user?.FullName ?? "عميل";
            return new AdminSubscriptionCancellationRequestDto(
                op.Id,
                op.SubscriptionId,
                op.Subscription.CustomerUserId,
                customerName,
                op.Reason,
                op.TimestampUtc,
                op.NewStatus.ToString());
        }).ToList();

        return result;
    }

    private async Task<int> ApplyRecordedFreezeAsync(SubscriptionFreezeRequest request, Subscription subscription,
        DateOnly start, DateOnly end, bool extend, CancellationToken ct)
    {
        var before = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id).Select(x => x.ServiceDate).ToListAsync(ct);
        var count = await ApplyFreezeAndExtendDaysAsync(subscription, start, end, extend, ct);
        var trackedDays = (_db as DbContext)?.ChangeTracker.Entries<SubscriptionDay>()
            .Where(x => x.Entity.SubscriptionId == subscription.Id).Select(x => x.Entity).ToList() ?? new();
        var outcome = new Freeze.FreezeAppliedOutcome(
            trackedDays.Where(x => x.Status == SubscriptionDayStatus.Frozen && x.ServiceDate >= start && x.ServiceDate <= end).Select(x => x.ServiceDate).Order().ToList(),
            trackedDays.Where(x => !before.Contains(x.ServiceDate)).Select(x => x.ServiceDate).Order().ToList(), subscription.EndDate);
        _db.SystemSettings.Add(new MealMate.Domain.AdminDashboard.SystemSetting
        { Key = Freeze.FreezeAppliedOutcome.Prefix + request.Id, ValueJson = System.Text.Json.JsonSerializer.Serialize(outcome), CreatedAtUtc = _clock.UtcNow });
        return count;
    }

    private async Task<int> ApplyFreezeAndExtendDaysAsync(
        Subscription subscription,
        DateOnly freezeStartDate,
        DateOnly freezeEndDate,
        bool extendSubscriptionEnd,
        CancellationToken cancellationToken)
    {
        var existingDays = await _db.SubscriptionDays
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(cancellationToken);

        if (existingDays.Count == 0)
        {
            if (extendSubscriptionEnd)
            {
                var days = freezeEndDate.DayNumber - freezeStartDate.DayNumber + 1;
                subscription.EndDate = subscription.EndDate.AddDays(days);
            }
            return 0;
        }

        // 1. Mark existing days within [freezeStartDate, freezeEndDate] as Frozen and clear their meal/restaurant selections
        var frozenDays = existingDays
            .Where(x => x.ServiceDate >= freezeStartDate && x.ServiceDate <= freezeEndDate)
            .ToList();

        var frozenDayIds = frozenDays.Select(x => x.Id).ToList();

        foreach (var day in frozenDays)
        {
            day.Status = SubscriptionDayStatus.Frozen;
            day.RestaurantId = null;
            day.IsAutoSelected = false;
            day.ModifiedAtUtc = _clock.UtcNow;
        }

        if (frozenDayIds.Count > 0)
        {
            var mealSelections = await _db.DailyMealSelections.IgnoreQueryFilters()
                .Where(x => frozenDayIds.Contains(x.SubscriptionDayId))
                .ToListAsync(cancellationToken);
            if (mealSelections.Count > 0)
            {
                _db.DailyMealSelections.RemoveRange(mealSelections);
            }

            var restaurantSelections = await _db.DailyRestaurantSelections
                .Where(x => frozenDayIds.Contains(x.SubscriptionDayId))
                .ToListAsync(cancellationToken);
            if (restaurantSelections.Count > 0)
            {
                _db.DailyRestaurantSelections.RemoveRange(restaurantSelections);
            }
        }

        var frozenDaysCount = frozenDays.Count;
        if (!extendSubscriptionEnd || frozenDaysCount == 0)
        {
            return frozenDaysCount;
        }

        // 2. Generate replacement days at the end of the subscription
        var lastServiceDate = existingDays.Max(x => x.ServiceDate);
        var cursor = lastServiceDate.AddDays(1);
        var addedCount = 0;
        var templateDay = existingDays.Last();

        var existingDaySnapshots = await _db.SubscriptionDayMealSnapshots
            .Where(x => x.SubscriptionId == subscription.Id)
            .ToListAsync(cancellationToken);
        var templateDaySnapshot = existingDaySnapshots.LastOrDefault();

        var existingRestaurantDays = await _db.SubscriptionRestaurantDaySnapshots
            .Where(x => x.SubscriptionId == subscription.Id)
            .ToListAsync(cancellationToken);
        var distinctRestaurantIds = existingRestaurantDays.Select(x => x.RestaurantId).Distinct().ToList();

        while (addedCount < frozenDaysCount)
        {
            // Skip Friday if not included in subscription
            if (!subscription.IncludeFriday && cursor.DayOfWeek == DayOfWeek.Friday)
            {
                cursor = cursor.AddDays(1);
                continue;
            }

            if (!existingDays.Any(x => x.ServiceDate == cursor))
            {
                var newDay = new SubscriptionDay
                {
                    SubscriptionId = subscription.Id,
                    RestaurantId = null,
                    ResolvedMenuPlanVersionId = templateDay.ResolvedMenuPlanVersionId,
                    ServiceDate = cursor,
                    Status = SubscriptionDayStatus.Open,
                    SelectionDeadlineUtc = _schedule.GetSelectionDeadlineUtc(cursor),
                    ConfirmedAtUtc = _clock.UtcNow,
                    IsAutoSelected = false,
                    DeliveryAddressId = templateDay.DeliveryAddressId,
                    DeliveryAddressLine = templateDay.DeliveryAddressLine,
                    DeliveryRegionId = templateDay.DeliveryRegionId,
                    DeliveryBlockRegionId = templateDay.DeliveryBlockRegionId
                };

                _db.SubscriptionDays.Add(newDay);
                existingDays.Add(newDay);

                if (templateDaySnapshot is not null)
                {
                    _db.SubscriptionDayMealSnapshots.Add(new SubscriptionDayMealSnapshot
                    {
                        SubscriptionId = subscription.Id,
                        SubscriptionDayId = newDay.Id,
                        ServiceDate = cursor,
                        RestaurantId = templateDaySnapshot.RestaurantId,
                        ProgramId = subscription.ProgramId,
                        MenuPlanVersionId = templateDaySnapshot.MenuPlanVersionId,
                        RequiredSlotsJson = templateDaySnapshot.RequiredSlotsJson,
                        MealsJson = templateDaySnapshot.MealsJson,
                        CapturedAtUtc = _clock.UtcNow
                    });
                }

                foreach (var restId in distinctRestaurantIds)
                {
                    var restTemplate = existingRestaurantDays.LastOrDefault(x => x.RestaurantId == restId);
                    if (restTemplate is not null)
                    {
                        _db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
                        {
                            RestaurantSnapshotId = restTemplate.RestaurantSnapshotId,
                            SubscriptionId = subscription.Id,
                            RestaurantId = restId,
                            ServiceDate = cursor,
                            MenuPlanVersionId = restTemplate.MenuPlanVersionId,
                            SlotRequirementsJson = restTemplate.SlotRequirementsJson,
                            MealsJson = restTemplate.MealsJson,
                            ContentHash = restTemplate.ContentHash,
                            CapturedAtUtc = _clock.UtcNow
                        });
                    }
                }

                addedCount++;
            }

            cursor = cursor.AddDays(1);
        }

        if (existingDays.Count > 0)
        {
            subscription.EndDate = existingDays.Max(x => x.ServiceDate);
        }

        return frozenDaysCount;
    }
}

