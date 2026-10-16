using MealMate.Domain.Enums;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

/// <summary>
/// Customer-facing delivery lifecycle codes returned by the widget and the calendar.
/// These are additive presentation codes: SubscriptionDayStatus keeps its existing
/// values (no Ready value is added); readiness is derived from order evidence.
/// </summary>
public static class CustomerDeliveryStatusCodes
{
    public const string Preparing = "Preparing";
    public const string ReadyForPickup = "ReadyForPickup";
    public const string OutForDelivery = "OutForDelivery";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
    public const string ReturnedToKitchen = "ReturnedToKitchen";
    public const string Frozen = "Frozen";
    public const string Cancelled = "Cancelled";
    public const string NotStarted = "NotStarted";

    /// <summary>Ambiguous multiple current attempts: no driver/proof data is exposed
    /// and the caller receives the diagnostic reason instead of an arbitrary pick.</summary>
    public const string TrackingUnavailable = "tracking_unavailable";
}

/// <summary>
/// Why the widget cannot show live tracking for an otherwise visible box.
/// </summary>
public static class CustomerDeliveryUnavailableReasons
{
    public const string DriverNotAssigned = "driver_not_assigned";
    public const string TrackingUnavailable = "tracking_unavailable";
    public const string NotTrackableState = "not_trackable_state";
}

/// <summary>Where a reported ETA came from. Null etaSource means no ETA is claimable.</summary>
public static class CustomerDeliveryEtaSources
{
    /// <summary>Successful real routing result that respects the customer's position
    /// in the remaining ordered stops of the trip.</summary>
    public const string RoadRoute = "road_route";

    /// <summary>The assigned driver already confirmed arrival at this stop; ETA is 0.</summary>
    public const string Arrival = "arrival";
}

/// <summary>The selected current delivery attempt for one subscription day.</summary>
public sealed record CustomerDeliveryAttemptInput(
    Guid TripStopId,
    Guid TripId,
    int SequenceNumber,
    TripStopStatus Status,
    DateTime? ArrivedAtUtc,
    DateTime? DeliveredAtUtc);

/// <summary>
/// Pure inputs of <see cref="CustomerDeliveryStatusResolver"/>: the day's current status,
/// the current restaurant daily order status and its readiness evidence, the selected
/// attempt and its event timestamps, plus the pickup instant from the current non-revoked
/// handover (null when no trustworthy handover exists — legacy rows never get invented times).
/// </summary>
public sealed record CustomerDeliveryStatusInput(
    SubscriptionDayStatus DayStatus,
    OrderStatus? OrderStatus,
    DateTime? ReadyAtUtc,
    DateTime? PickedUpAtUtc,
    CustomerDeliveryAttemptInput? CurrentAttempt,
    bool HasAmbiguousAttempts,
    string? AmbiguityReason = null);

/// <summary>
/// Resolved customer delivery state for one subscription day. Texts are always provided
/// in both languages so the mobile widget can switch without a refetch; callers pick the
/// current-language field. Wording comes from <see cref="CustomerDeliveryStatusTexts"/>.
/// </summary>
public sealed record CustomerDeliveryStatusSnapshot(
    string DeliveryStatus,
    string StatusTextAr,
    string StatusTextEn,
    bool IsTerminal,
    bool IsTrackable,
    string? UnavailableReasonCode,
    string? InconsistencyReason,
    DateTime? ReadyAtUtc,
    DateTime? PickedUpAtUtc,
    DateTime? ArrivedAtUtc,
    DateTime? DeliveredAtUtc)
{
    public static CustomerDeliveryStatusSnapshot Unavailable(string reason, string? inconsistency)
        => new(
            CustomerDeliveryStatusCodes.TrackingUnavailable,
            CustomerDeliveryStatusTexts.TrackingUnavailable.Ar,
            CustomerDeliveryStatusTexts.TrackingUnavailable.En,
            IsTerminal: false,
            IsTrackable: false,
            UnavailableReasonCode: CustomerDeliveryUnavailableReasons.TrackingUnavailable,
            InconsistencyReason: inconsistency,
            ReadyAtUtc: null,
            PickedUpAtUtc: null,
            ArrivedAtUtc: null,
            DeliveredAtUtc: null);
}

/// <summary>
/// Pure, explicit-case projection from operational rows to the customer delivery status.
/// Explicit named cases only — no numeric enum ordering decides any event or completion.
/// A terminal delivered day is never rewound by a ready or assignment request, and a
/// failed/returned/cancelled/frozen outcome never completes the success timeline.
/// Display texts come from the shared bilingual catalog; decision rules never depend on them.
/// </summary>
public static class CustomerDeliveryStatusResolver
{
    public static CustomerDeliveryStatusSnapshot Resolve(CustomerDeliveryStatusInput input)
    {
        // Hard day-level outcomes first; they always win regardless of any stale attempt.
        switch (input.DayStatus)
        {
            case SubscriptionDayStatus.Cancelled:
                return Snapshot(CustomerDeliveryStatusCodes.Cancelled, isTerminal: true);
            case SubscriptionDayStatus.Frozen:
                return Snapshot(CustomerDeliveryStatusCodes.Frozen, isTerminal: true);
        }

        // Ambiguous current attempts: refuse to select an arbitrary box or timeline.
        if (input.HasAmbiguousAttempts)
        {
            return CustomerDeliveryStatusSnapshot.Unavailable(
                CustomerDeliveryUnavailableReasons.TrackingUnavailable,
                input.AmbiguityReason ?? "multiple_current_attempts");
        }

        // Terminal day outcomes always win over any contradicting attempt: a delivered
        // day is never displayed as in-transit again, and a failed day never looks active.
        if (input.DayStatus == SubscriptionDayStatus.Delivered)
        {
            return Snapshot(
                CustomerDeliveryStatusCodes.Delivered,
                isTerminal: true,
                readyAtUtc: input.ReadyAtUtc,
                pickedUpAtUtc: input.PickedUpAtUtc,
                arrivedAtUtc: input.CurrentAttempt?.ArrivedAtUtc,
                deliveredAtUtc: input.CurrentAttempt?.DeliveredAtUtc,
                inconsistency: input.CurrentAttempt is { Status: not TripStopStatus.Delivered }
                    ? "day_delivered_with_active_attempt"
                    : input.CurrentAttempt is null
                        ? "delivery_timestamp_missing"
                        : null);
        }

        if (input.DayStatus == SubscriptionDayStatus.Failed && input.CurrentAttempt?.Status is not TripStopStatus.Delivered)
        {
            return Snapshot(
                CustomerDeliveryStatusCodes.Failed,
                isTerminal: true,
                readyAtUtc: input.ReadyAtUtc,
                pickedUpAtUtc: input.PickedUpAtUtc,
                arrivedAtUtc: input.CurrentAttempt?.ArrivedAtUtc,
                inconsistency: input.CurrentAttempt is { Status: TripStopStatus.PickedUp or TripStopStatus.Pending }
                    ? "day_failed_with_active_attempt"
                    : null);
        }

        var attempt = input.CurrentAttempt;

        // The selected attempt is the freshest operational truth for this box.
        if (attempt is not null)
        {
            switch (attempt.Status)
            {
                case TripStopStatus.Delivered:
                    return Snapshot(
                        CustomerDeliveryStatusCodes.Delivered,
                        isTerminal: true,
                        readyAtUtc: input.ReadyAtUtc,
                        pickedUpAtUtc: input.PickedUpAtUtc,
                        arrivedAtUtc: attempt.ArrivedAtUtc,
                        deliveredAtUtc: attempt.DeliveredAtUtc,
                        inconsistency: InconsistencyOf(input, expectDayDelivered: true, expectOrderDelivered: true));

                case TripStopStatus.Failed:
                    return Snapshot(
                        CustomerDeliveryStatusCodes.Failed,
                        isTerminal: true,
                        readyAtUtc: input.ReadyAtUtc,
                        pickedUpAtUtc: input.PickedUpAtUtc,
                        arrivedAtUtc: attempt.ArrivedAtUtc,
                        deliveredAtUtc: null,
                        inconsistency: DeliveredEvidencePresent(input)
                            ? "delivered_evidence_with_failed_attempt"
                            : null);

                case TripStopStatus.ReturnedToKitchen:
                    return Snapshot(
                        CustomerDeliveryStatusCodes.ReturnedToKitchen,
                        isTerminal: true,
                        readyAtUtc: input.ReadyAtUtc,
                        pickedUpAtUtc: input.PickedUpAtUtc,
                        arrivedAtUtc: attempt.ArrivedAtUtc,
                        deliveredAtUtc: null,
                        inconsistency: DeliveredEvidencePresent(input)
                            ? "delivered_evidence_with_returned_attempt"
                            : null);

                case TripStopStatus.PickedUp:
                    // Physical pickup confirmed (handover time from the current non-revoked
                    // handover; null when the legacy rows never recorded one — never the trip start).
                    return Snapshot(
                        CustomerDeliveryStatusCodes.OutForDelivery,
                        isTerminal: false,
                        isTrackable: true,
                        readyAtUtc: input.ReadyAtUtc,
                        pickedUpAtUtc: input.PickedUpAtUtc,
                        arrivedAtUtc: attempt.ArrivedAtUtc,
                        deliveredAtUtc: null,
                        inconsistency: input.DayStatus is SubscriptionDayStatus.Delivered
                            ? "day_delivered_with_active_attempt"
                            : DeliveredEvidencePresent(input)
                                ? "delivered_evidence_with_active_attempt"
                                : null);

                case TripStopStatus.Pending:
                    // A stop exists (driver may already be assigned) but this box was not
                    // physically picked up yet: readiness evidence decides the stage.
                    break;
            }
        }

        // No picked-up attempt: readiness evidence decides between ready and preparing.
        if (input.OrderStatus == OrderStatus.ReadyForPickup || input.OrderStatus == OrderStatus.AssignedToDriver)
        {
            return Snapshot(
                CustomerDeliveryStatusCodes.ReadyForPickup,
                isTerminal: false,
                unavailableReason: CustomerDeliveryUnavailableReasons.DriverNotAssigned,
                readyAtUtc: input.ReadyAtUtc,
                inconsistency: InconsistencyOf(input, expectDayDelivered: false, expectOrderDelivered: false));
        }

        // Orders already in transit or delivered without a surviving stop row keep their
        // truthful outcome; only real timestamps are reported (legacy rows may have none).
        if (input.OrderStatus == OrderStatus.OnTheWay || input.OrderStatus == OrderStatus.PickedUp)
        {
            return Snapshot(
                CustomerDeliveryStatusCodes.OutForDelivery,
                isTerminal: false,
                readyAtUtc: input.ReadyAtUtc,
                pickedUpAtUtc: input.PickedUpAtUtc,
                deliveredAtUtc: null,
                inconsistency: "order_in_transit_without_trip_stop");
        }

        if (input.OrderStatus == OrderStatus.Delivered)
        {
            return Snapshot(
                CustomerDeliveryStatusCodes.Delivered,
                isTerminal: true,
                readyAtUtc: input.ReadyAtUtc,
                pickedUpAtUtc: input.PickedUpAtUtc,
                deliveredAtUtc: null,
                inconsistency: input.DayStatus != SubscriptionDayStatus.Delivered
                    ? "order_delivered_day_not_delivered"
                    : "delivery_timestamp_missing");
        }

        if (input.OrderStatus is OrderStatus.DeliveryFailed or OrderStatus.SecondAttemptScheduled)
        {
            return Snapshot(
                CustomerDeliveryStatusCodes.Failed,
                isTerminal: true,
                readyAtUtc: input.ReadyAtUtc,
                pickedUpAtUtc: input.PickedUpAtUtc);
        }

        if (input.OrderStatus == OrderStatus.ReturnedToKitchen)
        {
            return Snapshot(
                CustomerDeliveryStatusCodes.ReturnedToKitchen,
                isTerminal: true,
                readyAtUtc: input.ReadyAtUtc,
                pickedUpAtUtc: input.PickedUpAtUtc);
        }

        // Kitchen / pre-dispatch stages and any valid day without operational preparation.
        return Snapshot(
            input.OrderStatus == OrderStatus.Preparing
                ? CustomerDeliveryStatusCodes.Preparing
                : CustomerDeliveryStatusCodes.NotStarted,
            isTerminal: false,
            readyAtUtc: null);
    }

    private static bool DeliveredEvidencePresent(CustomerDeliveryStatusInput input)
        => input.OrderStatus == OrderStatus.Delivered
           || input.DayStatus == SubscriptionDayStatus.Delivered;

    private static string? InconsistencyOf(
        CustomerDeliveryStatusInput input, bool expectDayDelivered, bool expectOrderDelivered)
    {
        string? inconsistency = null;
        if (expectDayDelivered && input.DayStatus != SubscriptionDayStatus.Delivered)
            inconsistency = "stop_delivered_day_not_delivered";
        if (expectOrderDelivered && input.OrderStatus != OrderStatus.Delivered)
            inconsistency = inconsistency is null
                ? "stop_delivered_order_not_delivered"
                : "stop_delivered_order_and_day_not_delivered";
        if (!expectDayDelivered && input.DayStatus == SubscriptionDayStatus.Delivered)
            inconsistency = "day_delivered_before_pickup";
        if (expectOrderDelivered
            && input.OrderStatus == OrderStatus.Delivered
            && input.CurrentAttempt?.DeliveredAtUtc is null)
        {
            inconsistency = inconsistency is null
                ? "delivery_timestamp_missing"
                : inconsistency + "+delivery_timestamp_missing";
        }

        return inconsistency;
    }

    private static CustomerDeliveryStatusSnapshot Snapshot(
        string status,
        bool isTerminal,
        bool isTrackable = false,
        string? unavailableReason = null,
        DateTime? readyAtUtc = null,
        DateTime? pickedUpAtUtc = null,
        DateTime? arrivedAtUtc = null,
        DateTime? deliveredAtUtc = null,
        string? inconsistency = null)
    {
        var text = CustomerDeliveryStatusTexts.For(status);
        return new(
            status,
            text.Ar,
            text.En,
            isTerminal,
            isTrackable,
            unavailableReason,
            inconsistency,
            readyAtUtc,
            pickedUpAtUtc,
            arrivedAtUtc,
            deliveredAtUtc);
    }
}
