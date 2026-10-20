using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

/// <summary>
/// The selected current delivery attempt for one authorized subscription day, plus the
/// resolved customer delivery status snapshot and the evidence the widget needs.
/// Attempts and handovers are selected deterministically; ambiguous current attempts are
/// flagged instead of picking an arbitrary row.
/// </summary>
public sealed record CustomerDeliveryDayProjection
{
    public required Guid SubscriptionDayId { get; init; }
    public required Guid SubscriptionId { get; init; }
    public required DateOnly ServiceDate { get; init; }
    public required SubscriptionDayStatus DayStatus { get; init; }

    public Guid? DailyOrderId { get; init; }
    public OrderStatus? OrderStatus { get; init; }
    public DateTime? ReadyAtUtc { get; init; }
    public string? OrderCode { get; init; }

    public CustomerDeliveryAttemptInput? Attempt { get; init; }
    public Guid? DriverProfileId { get; init; }

    /// <summary>Trip stops of the same trip with a smaller sequence that are still
    /// undelivered — a direct route to this customer is then not a delivery ETA.</summary>
    public bool HasEarlierUndeliveredStops { get; init; }

    /// <summary>Storage key of the delivery-proof scan matching this day's guarded
    /// identifiers and current attempt; null when nothing trustworthy exists.</summary>
    public string? ProofPhotoStorageKey { get; init; }

    public required CustomerDeliveryStatusSnapshot Snapshot { get; init; }
}

/// <summary>
/// Read-only batched projection of delivery state for a set of ALREADY-AUTHORIZED day
/// IDs. It never authorizes by itself and never writes; callers (widget, calendar,
/// legacy tracker) establish access first. Every query is AsNoTracking and bounded by
/// the given day IDs — no per-card or unbounded history reads.
/// </summary>
public interface ICustomerDeliveryReadService
{
    Task<IReadOnlyDictionary<Guid, CustomerDeliveryDayProjection>> GetForDaysAsync(
        IReadOnlyCollection<Guid> authorizedDayIds,
        CancellationToken cancellationToken);
}

public sealed class CustomerDeliveryReadService : ICustomerDeliveryReadService
{
    private readonly IAppDbContext _db;

    public CustomerDeliveryReadService(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<Guid, CustomerDeliveryDayProjection>> GetForDaysAsync(
        IReadOnlyCollection<Guid> authorizedDayIds,
        CancellationToken cancellationToken)
    {
        if (authorizedDayIds.Count == 0)
        {
            return new Dictionary<Guid, CustomerDeliveryDayProjection>();
        }

        var dayIds = authorizedDayIds.ToList();

        var days = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => dayIds.Contains(d.Id))
            .Select(d => new { d.Id, d.SubscriptionId, d.ServiceDate, d.Status })
            .ToListAsync(cancellationToken);

        var orders = await _db.RestaurantDailyOrders.AsNoTracking()
            .Where(o => dayIds.Contains(o.SubscriptionDayId))
            .Select(o => new
            {
                o.Id,
                o.SubscriptionDayId,
                o.Status,
                o.ReadyForPickupAtUtc
            })
            .ToListAsync(cancellationToken);

        var stops = (await _db.TripStops.AsNoTracking()
            .Where(s => s.SubscriptionDayId != null && dayIds.Contains(s.SubscriptionDayId.Value))
            .Select(s => new
            {
                s.Id,
                s.TripId,
                s.SubscriptionDayId,
                s.DailyOrderId,
                s.SequenceNumber,
                s.Status,
                s.ArrivedAtUtc,
                s.DeliveredAtUtc,
                s.OrderCode,
                s.CreatedAtUtc
            })
            .ToListAsync(cancellationToken))
            .Select(s => new AttemptStopRow(
                s.Id, s.TripId, s.SubscriptionDayId, s.DailyOrderId, s.SequenceNumber, s.Status,
                s.ArrivedAtUtc, s.DeliveredAtUtc, s.OrderCode, s.CreatedAtUtc))
            .ToList();

        var tripIds = stops.Select(s => s.TripId).Distinct().ToList();

        var activeAssignments = await _db.DriverAssignments.AsNoTracking()
            .Where(a => tripIds.Contains(a.TripId) && a.Status == "Active")
            .Select(a => new { a.TripId, a.DriverProfileId })
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();

        // Current non-revoked handovers only; revoked custody is never evidence.
        var handovers = (await _db.OrderHandovers.AsNoTracking()
            .Where(h => orderIds.Contains(h.DailyOrderId) && h.RevokedAtUtc == null)
            .Select(h => new
            {
                h.DailyOrderId,
                h.TripId,
                h.TripStopId,
                h.DriverProfileId,
                h.HandedOverAtUtc
            })
            .ToListAsync(cancellationToken))
            .Select(h => new
            {
                h.DailyOrderId,
                Row = new AttemptHandoverRow(h.TripStopId, h.TripId, h.DriverProfileId, h.HandedOverAtUtc)
            })
            .ToList();

        var orderByDay = orders
            .GroupBy(o => o.SubscriptionDayId)
            .ToDictionary(g => g.Key, g => g.First());
        var stopsByDay = stops
            .GroupBy(s => s.SubscriptionDayId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var assignmentByTrip = activeAssignments
            .GroupBy(a => a.TripId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var handoversByOrder = handovers
            .GroupBy(h => h.DailyOrderId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Row).ToList());

        // A support transfer creates a new delivery attempt while preserving the
        // original kitchen handover as audit evidence. Use only persisted receipt
        // evidence from a completed transfer for the replacement driver's pickup.
        var stopIds = stops.Select(s => s.Id).Distinct().ToList();
        var supportReceipts = stopIds.Count == 0
            ? new List<SupportReceiptRow>()
            : await (from box in _db.DriverSupportHandoverBoxes.AsNoTracking()
                     join handover in _db.DriverSupportHandovers.AsNoTracking()
                         on box.HandoverId equals handover.Id
                     where stopIds.Contains(box.TripStopId)
                           && box.Status == "Received"
                           && handover.Status == "Completed"
                           && handover.CompletedAtUtc.HasValue
                     select new SupportReceiptRow(
                         box.TripStopId,
                         handover.ReplacementDriverProfileId,
                         handover.CompletedAtUtc!.Value))
                .ToListAsync(cancellationToken);
        var supportReceiptByStop = supportReceipts
            .GroupBy(x => x.TripStopId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ReceivedAtUtc).First());

        var result = new Dictionary<Guid, CustomerDeliveryDayProjection>(days.Count);

        foreach (var day in days)
        {
            orderByDay.TryGetValue(day.Id, out var order);
            stopsByDay.TryGetValue(day.Id, out var dayStops);

            var selection = SelectAttempt(dayStops, day.Id);
            Guid? driverProfileId = null;
            string? ambiguity = selection.AmbiguityReason;
            DateTime? pickedUpAtUtc = null;

            if (selection.Attempt is not null)
            {
                // One active assignment per trip is the contract; several mean custody
                // of this box cannot be attributed, so no driver data is exposed.
                if (assignmentByTrip.TryGetValue(selection.Attempt.TripId, out var assignments))
                {
                    if (assignments.Count == 1)
                    {
                        driverProfileId = assignments[0].DriverProfileId;
                    }
                    else if (assignments.Count > 1 && ambiguity is null)
                    {
                        ambiguity = $"multiple_active_assignments:{assignments.Count}";
                    }
                }

                if (driverProfileId is not null)
                {
                    var orderId = selection.AttemptIdentifier ?? order?.Id;
                    pickedUpAtUtc = ResolvePickupTime(orderId, selection.Attempt, handoversByOrder, driverProfileId.Value, ref ambiguity);
                    if (supportReceiptByStop.TryGetValue(selection.Attempt.TripStopId, out var receipt)
                        && receipt.ReplacementDriverProfileId == driverProfileId.Value)
                    {
                        pickedUpAtUtc = receipt.ReceivedAtUtc;
                        ambiguity = null;
                    }
                }
            }

            var snapshotInput = new CustomerDeliveryStatusInput(
                DayStatus: day.Status,
                OrderStatus: order?.Status,
                ReadyAtUtc: order?.ReadyForPickupAtUtc,
                PickedUpAtUtc: pickedUpAtUtc,
                CurrentAttempt: selection.Attempt,
                HasAmbiguousAttempts: ambiguity is not null,
                AmbiguityReason: ambiguity);

            result[day.Id] = new CustomerDeliveryDayProjection
            {
                SubscriptionDayId = day.Id,
                SubscriptionId = day.SubscriptionId,
                ServiceDate = day.ServiceDate,
                DayStatus = day.Status,
                DailyOrderId = order?.Id ?? selection.AttemptIdentifier,
                OrderStatus = order?.Status,
                ReadyAtUtc = order?.ReadyForPickupAtUtc,
                OrderCode = selection.OrderCode,
                Attempt = selection.Attempt,
                DriverProfileId = driverProfileId,
                HasEarlierUndeliveredStops = selection.HasEarlierUndeliveredStops,
                Snapshot = CustomerDeliveryStatusResolver.Resolve(snapshotInput)
            };
        }

        return result;
    }

    private sealed record AttemptSelection(
        CustomerDeliveryAttemptInput? Attempt,
        Guid? AttemptIdentifier,
        string? OrderCode,
        bool HasEarlierUndeliveredStops,
        string? AmbiguityReason);

    /// <summary>
    /// Deterministic current-attempt selection: exactly one active (Pending/PickedUp) stop
    /// is the current attempt; several active stops are ambiguous; with no active stop the
    /// terminal outcome is the latest delivered stop, or the latest failed/returned one.
    /// </summary>
    private static AttemptSelection SelectAttempt(List<AttemptStopRow>? stops, Guid dayId)
    {
        if (stops is null || stops.Count == 0)
        {
            return new AttemptSelection(null, null, null, false, null);
        }

        var active = stops.Where(s => s.Status is TripStopStatus.Pending or TripStopStatus.PickedUp).ToList();
        if (active.Count > 1)
        {
            return new AttemptSelection(null, null, null, false, $"multiple_active_stops:{active.Count}");
        }

        if (active.Count == 1)
        {
            var stop = active[0];
            return new AttemptSelection(
                ToAttempt(stop),
                stop.DailyOrderId,
                stop.OrderCode,
                HasEarlierUndeliveredStops(stops, stop),
                null);
        }

        var delivered = stops.Where(s => s.Status == TripStopStatus.Delivered).ToList();
        if (delivered.Count > 1)
        {
            return new AttemptSelection(null, null, null, false, $"multiple_delivered_stops:{delivered.Count}");
        }

        if (delivered.Count == 1)
        {
            var stop = delivered[0];
            return new AttemptSelection(ToAttempt(stop), stop.DailyOrderId, stop.OrderCode, false, null);
        }

        var terminal = stops
            .Where(s => s.Status is TripStopStatus.Failed or TripStopStatus.ReturnedToKitchen)
            .OrderByDescending(s => s.DeliveredAtUtc ?? s.CreatedAtUtc)
            .FirstOrDefault();
        if (terminal is not null)
        {
            return new AttemptSelection(ToAttempt(terminal), terminal.DailyOrderId, terminal.OrderCode, false, null);
        }

        return new AttemptSelection(null, null, null, false, null);
    }

    private static bool HasEarlierUndeliveredStops(List<AttemptStopRow> stops, AttemptStopRow current)
        => stops.Any(s => s.Id != current.Id
            && s.TripId == current.TripId
            && s.SequenceNumber < current.SequenceNumber
            && s.Status is not (TripStopStatus.Delivered or TripStopStatus.Failed or TripStopStatus.ReturnedToKitchen));

    private static CustomerDeliveryAttemptInput ToAttempt(AttemptStopRow stop)
        => new(
            stop.Id,
            stop.TripId,
            stop.SequenceNumber,
            stop.Status,
            stop.ArrivedAtUtc,
            stop.DeliveredAtUtc);

    /// <summary>
    /// Pickup time = the current non-revoked handover of THIS stop's delivery attempt,
    /// attributed to the currently assigned driver. Legacy rows without a stop link match
    /// by order + trip; ambiguous multi-driver handovers set the ambiguity diagnostic and
    /// the caller drops driver/proof data instead of guessing.
    /// </summary>
    private static DateTime? ResolvePickupTime(
        Guid? orderId,
        CustomerDeliveryAttemptInput attempt,
        Dictionary<Guid, List<AttemptHandoverRow>> handoversByOrder,
        Guid driverProfileId,
        ref string? ambiguity)
    {
        if (orderId is null || !handoversByOrder.TryGetValue(orderId.Value, out var candidates))
        {
            return null;
        }

        var forAttempt = candidates
            .Where(h => (h.TripStopId.HasValue && h.TripStopId.Value == attempt.TripStopId)
                        || (!h.TripStopId.HasValue && h.TripId == attempt.TripId))
            .Where(h => h.HandedOverAtUtc.HasValue)
            .ToList();

        if (forAttempt.Count == 0)
        {
            return null;
        }

        if (forAttempt.Select(h => h.DriverProfileId).Distinct().Count() > 1 && ambiguity is null)
        {
            ambiguity = $"multiple_active_handovers:{forAttempt.Count}";
            return null;
        }

        var currentDriverHandovers = forAttempt
            .Where(h => h.DriverProfileId == driverProfileId)
            .ToList();

        return currentDriverHandovers.Count == 0
            ? null
            : currentDriverHandovers.Max(h => h.HandedOverAtUtc!.Value);
    }

    private sealed record AttemptStopRow(
        Guid Id,
        Guid TripId,
        Guid? SubscriptionDayId,
        Guid? DailyOrderId,
        int SequenceNumber,
        TripStopStatus Status,
        DateTime? ArrivedAtUtc,
        DateTime? DeliveredAtUtc,
        string? OrderCode,
        DateTime CreatedAtUtc);

    private sealed record AttemptHandoverRow(
        Guid? TripStopId,
        Guid? TripId,
        Guid? DriverProfileId,
        DateTime? HandedOverAtUtc);

    private sealed record SupportReceiptRow(Guid TripStopId, Guid ReplacementDriverProfileId, DateTime ReceivedAtUtc);
}
