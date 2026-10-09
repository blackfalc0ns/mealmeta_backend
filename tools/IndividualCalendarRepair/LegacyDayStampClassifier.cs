using MealMate.Domain.Enums;

namespace IndividualCalendarRepair;

/// <summary>Classification of a legacy subscription day's confirmation stamp. Pure function over
/// the collected facts so it is unit-testable without a database.</summary>
public enum LegacyDayClassification
{
    /// <summary>Every evidence condition holds and a legacy-fulfillment audit record proves the
    /// stamp came from the old payment path. Only these are apply candidates, and only after
    /// operator review of the report.</summary>
    RepairablePaymentStamp,

    /// <summary>The confirmation is a real restaurant confirmation or a customer pick exists and
    /// the day is not mutable — a correct operational lock that must never be touched.</summary>
    ProtectedOperationalLock,

    /// <summary>A stamp is present but the payment proof is missing — report only.</summary>
    NoPaymentEvidence,

    /// <summary>A stamp is present, mutable, but only timing equality supports the payment-path
    /// theory and no fulfillment audit record exists — report only.</summary>
    AmbiguousEvidence,

    /// <summary>Open day past its deadline without any stamp — not a stamp-repair case.</summary>
    PastDeadlineOpen,

    /// <summary>Family subscription, inactive subscription, or a non-stamped day — out of scope.</summary>
    OutOfScope,
}

public sealed record LegacyDayFacts(
    Guid SubscriptionId,
    Guid DayId,
    DateOnly ServiceDate,
    SubscriptionDayStatus Status,
    DateTime? ConfirmedAtUtc,
    DateTime? LockAtUtc,
    DateTime? SelectionDeadlineUtc,
    bool IndividualSubscription,
    SubscriptionStatus SubscriptionStatus,
    DateTime? PaymentVerifiedAtUtc,
    bool HasDispatchEvidence,
    bool HasLegacyFulfillmentAuditRecord)
{
    /// <summary>Mirrors OperationalDeadlineRules.IsMutable: a day the customer could still edit
    /// if the erroneous stamp were removed.</summary>
    public bool IsMutable
        => LockAtUtc is null
            && Status is SubscriptionDayStatus.Open
                or SubscriptionDayStatus.CustomerSelected
                or SubscriptionDayStatus.AutoSelected
                or SubscriptionDayStatus.ReplacementRequired;
}

public sealed record LegacyDayVerdict(LegacyDayClassification Classification, string Reason)
{
    public bool RepairEligible => Classification == LegacyDayClassification.RepairablePaymentStamp;
}

public static class LegacyDayStampClassifier
{
    public static LegacyDayVerdict Classify(LegacyDayFacts day)
    {
        if (!day.IndividualSubscription)
            return new(LegacyDayClassification.OutOfScope, "not_individual_subscription");
        if (day.SubscriptionStatus is SubscriptionStatus.Cancelled or SubscriptionStatus.Upgraded or SubscriptionStatus.PaymentFailed)
            return new(LegacyDayClassification.OutOfScope, "subscription_not_active");

        var stamped = day.ConfirmedAtUtc.HasValue;
        if (!stamped)
        {
            if (day.Status is SubscriptionDayStatus.Open && !day.IsMutable)
                return new(LegacyDayClassification.PastDeadlineOpen, "open_day_locked_by_deadline");
            return new(LegacyDayClassification.OutOfScope, "day_not_stamped");
        }

        // Anything that reached the kitchen flow keeps its lock: sent/confirmed/preparing/out/
        // delivered days — or any dispatched order — are real operational history.
        if (!day.IsMutable || day.HasDispatchEvidence)
            return new(LegacyDayClassification.ProtectedOperationalLock, "day_reached_kitchen_flow");

        if (day.PaymentVerifiedAtUtc is null)
            return new(LegacyDayClassification.NoPaymentEvidence, "mutable_stamp_without_payment_proof");

        // Timing equality between the stamp and the payment is necessary but never sufficient:
        // without a fulfillment audit record the origin of the stamp is unproven.
        if (!day.HasLegacyFulfillmentAuditRecord)
            return new(LegacyDayClassification.AmbiguousEvidence, "stamp_origin_unproven_without_audit_record");

        return new(LegacyDayClassification.RepairablePaymentStamp, "stamped_by_legacy_payment_fulfillment");
    }

    /// <summary>
    /// The apply-time guard. State may have changed between the report and the approved apply:
    /// the CURRENT facts are re-classified and the repair only proceeds when the fresh verdict
    /// is still a repairable stamp and the stamp is still present — so a newer restaurant
    /// confirmation is never overwritten and a repeat run never edits twice.
    /// </summary>
    public static bool CanApply(LegacyDayFacts currentFacts, out LegacyDayVerdict verdict, out string? skipReason)
    {
        verdict = Classify(currentFacts);
        skipReason = null;
        if (currentFacts.ConfirmedAtUtc is null)
        {
            skipReason = "stamp_already_clear";
            return false;
        }
        if (!verdict.RepairEligible)
        {
            skipReason = $"recheck_classification_{verdict.Classification}";
            return false;
        }
        return true;
    }
}
