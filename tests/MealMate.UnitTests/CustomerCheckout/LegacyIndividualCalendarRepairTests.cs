using IndividualCalendarRepair;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

/// <summary>
/// Classification contract for the legacy individual-calendar stamp repair: only days whose
/// stamp is PROVEN to come from the old payment fulfillment path are repair candidates, real
/// operational locks are never touched, ambiguous evidence stays report-only, and the apply-time
/// re-check protects against state changed between report and apply and against repeat runs.
/// </summary>
public sealed class LegacyIndividualCalendarRepairTests
{
    private static readonly DateTime PaymentAt = new(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StampAt = new(2026, 9, 20, 9, 0, 5, DateTimeKind.Utc);
    private static readonly DateOnly ServiceDate = new(2026, 9, 24);

    private static LegacyDayFacts Day(
        SubscriptionDayStatus status,
        DateTime? confirmedAtUtc,
        DateTime? lockAtUtc,
        bool individual,
        SubscriptionStatus subscriptionStatus,
        DateTime? paymentVerifiedAtUtc,
        bool hasDispatchEvidence,
        bool hasFulfillmentAudit)
        => new(
            Guid.NewGuid(), Guid.NewGuid(), ServiceDate, status, confirmedAtUtc, lockAtUtc,
            SelectionDeadlineUtc: PaymentAt.AddHours(24),
            IndividualSubscription: individual,
            SubscriptionStatus: subscriptionStatus,
            PaymentVerifiedAtUtc: paymentVerifiedAtUtc,
            HasDispatchEvidence: hasDispatchEvidence,
            HasLegacyFulfillmentAuditRecord: hasFulfillmentAudit);

    private static LegacyDayFacts StampedOpen(
        bool hasDispatchEvidence = false,
        bool hasFulfillmentAudit = true,
        bool individual = true,
        SubscriptionStatus subscriptionStatus = SubscriptionStatus.Active,
        bool withPaymentEvidence = true)
        => Day(SubscriptionDayStatus.Open, StampAt, null, individual, subscriptionStatus,
            withPaymentEvidence ? PaymentAt : null, hasDispatchEvidence, hasFulfillmentAudit);

    [Fact]
    public void Open_day_with_proven_payment_stamp_is_the_only_repair_candidate()
    {
        var verdict = LegacyDayStampClassifier.Classify(StampedOpen());

        Assert.Equal(LegacyDayClassification.RepairablePaymentStamp, verdict.Classification);
        Assert.True(verdict.RepairEligible);
    }

    [Fact]
    public void Restaurant_confirmed_day_is_protected()
    {
        var confirmed = LegacyDayStampClassifier.Classify(
            Day(SubscriptionDayStatus.ConfirmedByRestaurant, StampAt, StampAt.AddHours(1), true, SubscriptionStatus.Active, PaymentAt, false, true));

        Assert.Equal(LegacyDayClassification.ProtectedOperationalLock, confirmed.Classification);
    }

    [Theory]
    [InlineData(SubscriptionDayStatus.SentToRestaurant)]
    [InlineData(SubscriptionDayStatus.Preparing)]
    [InlineData(SubscriptionDayStatus.OutForDelivery)]
    [InlineData(SubscriptionDayStatus.Delivered)]
    public void Kitchen_flow_days_are_never_reopened(SubscriptionDayStatus status)
    {
        var verdict = LegacyDayStampClassifier.Classify(
            Day(status, StampAt, StampAt.AddHours(1), true, SubscriptionStatus.Active, PaymentAt, false, true));

        Assert.Equal(LegacyDayClassification.ProtectedOperationalLock, verdict.Classification);
    }

    [Fact]
    public void Dispatched_order_blocks_the_repair_even_if_the_day_status_looks_mutable()
    {
        var verdict = LegacyDayStampClassifier.Classify(StampedOpen(hasDispatchEvidence: true));

        Assert.Equal(LegacyDayClassification.ProtectedOperationalLock, verdict.Classification);
    }

    [Fact]
    public void Cancelled_subscription_days_are_out_of_scope()
    {
        var verdict = LegacyDayStampClassifier.Classify(StampedOpen(subscriptionStatus: SubscriptionStatus.Cancelled));

        Assert.Equal(LegacyDayClassification.OutOfScope, verdict.Classification);
    }

    [Fact]
    public void Frozen_days_are_protected()
    {
        var verdict = LegacyDayStampClassifier.Classify(
            Day(SubscriptionDayStatus.Frozen, StampAt, null, true, SubscriptionStatus.Active, PaymentAt, false, true));

        Assert.Equal(LegacyDayClassification.ProtectedOperationalLock, verdict.Classification);
    }

    [Fact]
    public void Family_subscription_days_are_out_of_scope()
    {
        var verdict = LegacyDayStampClassifier.Classify(StampedOpen(individual: false));

        Assert.Equal(LegacyDayClassification.OutOfScope, verdict.Classification);
    }

    [Fact]
    public void Open_day_past_deadline_without_stamp_is_not_a_stamp_case()
    {
        var verdict = LegacyDayStampClassifier.Classify(
            Day(SubscriptionDayStatus.Open, null, StampAt, true, SubscriptionStatus.Active, PaymentAt, false, true));

        Assert.Equal(LegacyDayClassification.PastDeadlineOpen, verdict.Classification);
        Assert.False(verdict.RepairEligible);
    }

    [Fact]
    public void Mutable_stamp_without_payment_evidence_stays_report_only()
    {
        var verdict = LegacyDayStampClassifier.Classify(StampedOpen(withPaymentEvidence: false));

        Assert.Equal(LegacyDayClassification.NoPaymentEvidence, verdict.Classification);
        Assert.False(verdict.RepairEligible);
    }

    [Fact]
    public void Timing_equality_without_a_fulfillment_audit_record_is_never_sufficient()
    {
        var verdict = LegacyDayStampClassifier.Classify(StampedOpen(hasFulfillmentAudit: false));

        Assert.Equal(LegacyDayClassification.AmbiguousEvidence, verdict.Classification);
        Assert.False(verdict.RepairEligible);
    }

    [Fact]
    public void Apply_recheck_rejects_state_changed_between_report_and_apply()
    {
        // The report classified the day as repairable, but a restaurant confirmed it meanwhile.
        var current = Day(SubscriptionDayStatus.ConfirmedByRestaurant, StampAt, StampAt.AddHours(1), true, SubscriptionStatus.Active, PaymentAt, false, true);

        var canApply = LegacyDayStampClassifier.CanApply(current, out var verdict, out var skipReason);

        Assert.False(canApply);
        Assert.Equal(LegacyDayClassification.ProtectedOperationalLock, verdict.Classification);
        Assert.Equal("recheck_classification_ProtectedOperationalLock", skipReason);
    }

    [Fact]
    public void Apply_recheck_makes_repeated_runs_idempotent()
    {
        var alreadyCleared = Day(SubscriptionDayStatus.Open, null, null, true, SubscriptionStatus.Active, PaymentAt, false, true);

        var canApply = LegacyDayStampClassifier.CanApply(alreadyCleared, out _, out var skipReason);

        Assert.False(canApply);
        Assert.Equal("stamp_already_clear", skipReason);
    }

    [Fact]
    public void Apply_recheck_passes_only_for_still_proven_candidates()
    {
        var canApply = LegacyDayStampClassifier.CanApply(StampedOpen(), out var verdict, out var skipReason);

        Assert.True(canApply);
        Assert.Null(skipReason);
        Assert.Equal(LegacyDayClassification.RepairablePaymentStamp, verdict.Classification);
    }
}
