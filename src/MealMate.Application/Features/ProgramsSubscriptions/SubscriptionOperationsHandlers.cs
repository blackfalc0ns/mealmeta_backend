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
    ICommandHandler<ProcessSubscriptionRenewalsAndExpirationsCommand, SubscriptionBatchProcessingResultDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionTierUpgradeQuoteBuilder _upgradeQuotes;

    public SubscriptionOperationsHandlers(
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ISubscriptionTierUpgradeQuoteBuilder upgradeQuotes)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _upgradeQuotes = upgradeQuotes;
    }

    public async Task<Result<SubscriptionFreezeRequestDto>> Handle(RequestSubscriptionFreezeCommand request, CancellationToken cancellationToken)
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

        var policyRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, cancellationToken);
        var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);
        var earliestStart = _clock.TodayUtc.AddDays(policy.StartDelayDays);
        if (request.FreezeStartDate < earliestStart)
            return Error.Validation(MessageCodes.Validation.InclusiveBetween, $"Freeze must start on or after {earliestStart:yyyy-MM-dd}.");

        var days = request.FreezeEndDate.DayNumber - request.FreezeStartDate.DayNumber + 1;
        var maxAllowedFreeze = policy.DefaultDurationDays;

        if (days > maxAllowedFreeze)
        {
            return Error.Validation(MessageCodes.Validation.InclusiveBetween, $"Maximum allowed freeze days for a {subscription.DurationDays}-day subscription is {maxAllowedFreeze} days.");
        }

        var lockedDaysCount = await _db.SubscriptionDays.AsNoTracking()
            .CountAsync(x => x.SubscriptionId == request.SubscriptionId &&
                             x.ServiceDate >= request.FreezeStartDate &&
                             x.ServiceDate <= request.FreezeEndDate &&
                             (x.Status == SubscriptionDayStatus.Locked72h ||
                              x.Status == SubscriptionDayStatus.SentToRestaurant ||
                              x.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                              x.Status == SubscriptionDayStatus.Preparing), cancellationToken);

        if (lockedDaysCount > 0)
        {
            return Error.Validation("freeze.locked_days_cannot_be_frozen",
                $"Cannot freeze subscription for dates with locked or confirmed kitchen orders ({lockedDaysCount} locked day(s) found within 72h window).");
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

        if (policy.ExtendSubscriptionEnd)
        {
            subscription.EndDate = subscription.EndDate.AddDays(days);
        }

        if (request.FreezeStartDate <= _clock.TodayUtc)
        {
            subscription.Status = SubscriptionStatus.Frozen;
        }

        subscription.LastOperationAtUtc = _clock.UtcNow;

        _db.SubscriptionFreezeRequests.Add(freezeReq);
        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "FREEZE_APPLIED",
            PreviousStatus = subscription.Status,
            NewStatus = subscription.Status,
            Reason = $"Freeze applied automatically for {days} days from {request.FreezeStartDate} to {request.FreezeEndDate}. Extended end date to {subscription.EndDate}",
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
        var freezeReq = await _db.SubscriptionFreezeRequests
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.Id == request.FreezeRequestId, cancellationToken);

        if (freezeReq is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Freeze request with ID '{request.FreezeRequestId}' was not found.");
        }

        if (freezeReq.Status == VerificationStatus.Approved)
        {
            if (request.Approved)
            {
                freezeReq.Subscription.Status = SubscriptionStatus.Frozen;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return MapFreezeDto(freezeReq);
        }

        if (freezeReq.Status != VerificationStatus.Submitted)
        {
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, "Freeze request has already been processed.");
        }

        if (request.Approved)
        {
            freezeReq.Status = VerificationStatus.Approved;
            freezeReq.ProcessedAtUtc = _clock.UtcNow;

            var policyRow = await _db.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, cancellationToken);
            var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);

            // Extend only when the configured policy allows it.
            var oldEndDate = freezeReq.Subscription.EndDate;
            if (policy.ExtendSubscriptionEnd)
                freezeReq.Subscription.EndDate = oldEndDate.AddDays(freezeReq.FreezeDays);
            freezeReq.Subscription.Status = SubscriptionStatus.Frozen;
            freezeReq.Subscription.LastOperationAtUtc = _clock.UtcNow;

            var op = new SubscriptionOperation
            {
                SubscriptionId = freezeReq.SubscriptionId,
                OperationType = "FREEZE_APPROVED",
                PreviousStatus = SubscriptionStatus.Active,
                NewStatus = SubscriptionStatus.Frozen,
                Reason = $"Freeze approved for {freezeReq.FreezeDays} days. New end date: {freezeReq.Subscription.EndDate}",
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
}
