using System.Text.Json;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MealMate.Persistence.Features.DriverDelivery;

/// <summary>
/// Captures tracked changes that alter the customer delivery projection. The rows are
/// staged before the state save, so they share its transaction and rollback behavior.
/// </summary>
internal static class CustomerDeliveryChangeOutboxCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<OutboxMessage> Stage(AppDbContext db, DateTime occurredAtUtc)
    {
        db.ChangeTracker.DetectChanges();

        var dayIds = new HashSet<Guid>();
        var tripIds = new HashSet<Guid>();
        var dailyOrderIds = new HashSet<Guid>();
        var trackedStops = db.ChangeTracker.Entries<TripStop>().ToList();
        var trackedOrders = db.ChangeTracker.Entries<RestaurantDailyOrder>().ToList();

        foreach (var entry in db.ChangeTracker.Entries<SubscriptionDay>())
        {
            if (IsAdded(entry) && IsCustomerDeliveryDayStatus(entry.Entity.Status))
            {
                dayIds.Add(entry.Entity.Id);
            }
            else if (IsModified(entry, nameof(SubscriptionDay.Status)))
            {
                dayIds.Add(entry.Entity.Id);
            }
        }

        foreach (var entry in trackedOrders)
        {
            if (IsAdded(entry) && (entry.Entity.Status != OrderStatus.Created || entry.Entity.ReadyForPickupAtUtc.HasValue)
                || IsDeleted(entry)
                || IsModified(entry, nameof(RestaurantDailyOrder.Status), nameof(RestaurantDailyOrder.ReadyForPickupAtUtc)))
            {
                dayIds.Add(entry.Entity.SubscriptionDayId);
            }
        }

        foreach (var entry in trackedStops)
        {
            if (IsAdded(entry) || IsDeleted(entry)
                || IsModified(entry,
                    nameof(TripStop.TripId), nameof(TripStop.SubscriptionDayId), nameof(TripStop.DailyOrderId),
                    nameof(TripStop.Status), nameof(TripStop.StartedDeliveryAtUtc), nameof(TripStop.ArrivedAtUtc),
                    nameof(TripStop.DeliveredAtUtc), nameof(TripStop.SequenceNumber), nameof(TripStop.OrderCode),
                    nameof(TripStop.FullAddress), nameof(TripStop.Latitude), nameof(TripStop.Longitude)))
            {
                AddNullableGuidCurrentAndOriginal(entry, nameof(TripStop.SubscriptionDayId), dayIds);
                AddGuidCurrentAndOriginal(entry, nameof(TripStop.TripId), tripIds);
                AddNullableGuidCurrentAndOriginal(entry, nameof(TripStop.DailyOrderId), dailyOrderIds);
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<DriverAssignment>())
        {
            if (IsAdded(entry) || IsDeleted(entry)
                || IsModified(entry, nameof(DriverAssignment.TripId), nameof(DriverAssignment.Status), nameof(DriverAssignment.DriverProfileId)))
            {
                AddGuidCurrentAndOriginal(entry, nameof(DriverAssignment.TripId), tripIds);
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<OrderHandover>())
        {
            if (IsAdded(entry) || IsDeleted(entry)
                || IsModified(entry,
                    nameof(OrderHandover.DailyOrderId), nameof(OrderHandover.TripId), nameof(OrderHandover.TripStopId),
                    nameof(OrderHandover.DriverProfileId), nameof(OrderHandover.Status), nameof(OrderHandover.HandedOverAtUtc),
                    nameof(OrderHandover.RevokedAtUtc)))
            {
                AddGuidCurrentAndOriginal(entry, nameof(OrderHandover.DailyOrderId), dailyOrderIds);
                AddNullableGuidCurrentAndOriginal(entry, nameof(OrderHandover.TripId), tripIds);
            }
        }

        AddDirectDayChanges<PickupScan>(db, dayIds,
            nameof(PickupScan.SubscriptionDayId), nameof(PickupScan.TripId), nameof(PickupScan.ScannedAtUtc));
        AddDirectDayChanges<SecondDeliveryAttempt>(db, dayIds,
            nameof(SecondDeliveryAttempt.SubscriptionDayId), nameof(SecondDeliveryAttempt.AttemptNumber),
            nameof(SecondDeliveryAttempt.ScheduledAtUtc), nameof(SecondDeliveryAttempt.CompletedAtUtc), nameof(SecondDeliveryAttempt.Outcome));
        AddDirectDayChanges<DeliveryConfirmationScan>(db, dayIds,
            nameof(DeliveryConfirmationScan.SubscriptionDayId), nameof(DeliveryConfirmationScan.ScannedAtUtc),
            nameof(DeliveryConfirmationScan.PhotoPath));
        AddDirectDayChanges<DeliveryHold>(db, dayIds,
            nameof(DeliveryHold.SubscriptionDayId), nameof(DeliveryHold.Status), nameof(DeliveryHold.ReleasedAtUtc),
            nameof(DeliveryHold.ResumedAtUtc), nameof(DeliveryHold.ResolvedAtUtc));

        if (dailyOrderIds.Count > 0)
        {
            foreach (var row in db.RestaurantDailyOrders.AsNoTracking()
                         .Where(order => dailyOrderIds.Contains(order.Id))
                         .Select(order => new { order.Id, order.SubscriptionDayId }))
            {
                dayIds.Add(row.SubscriptionDayId);
            }

            foreach (var entry in trackedOrders.Where(entry => entry.State != EntityState.Detached))
            {
                if (dailyOrderIds.Contains(entry.Entity.Id)) dayIds.Add(entry.Entity.SubscriptionDayId);
            }
        }

        if (tripIds.Count > 0)
        {
            foreach (var row in db.TripStops.AsNoTracking()
                         .Where(stop => tripIds.Contains(stop.TripId) && stop.SubscriptionDayId != null)
                         .Select(stop => new { stop.SubscriptionDayId }))
            {
                if (row.SubscriptionDayId.HasValue) dayIds.Add(row.SubscriptionDayId.Value);
            }

            foreach (var entry in trackedStops.Where(entry => entry.State != EntityState.Detached))
            {
                if (entry.Entity.SubscriptionDayId.HasValue
                    && (tripIds.Contains(entry.Entity.TripId)
                        || OriginalValue(entry, nameof(TripStop.TripId)) is { } originalTripId && tripIds.Contains(originalTripId)))
                {
                    dayIds.Add(entry.Entity.SubscriptionDayId.Value);
                }
            }
        }

        if (dayIds.Count == 0) return Array.Empty<OutboxMessage>();

        var messages = dayIds.Select(dayId => CreateMessage(dayId, occurredAtUtc)).ToList();
        db.OutboxMessages.AddRange(messages);
        return messages;
    }

    private static void AddDirectDayChanges<TEntity>(AppDbContext db, ISet<Guid> dayIds, params string[] meaningfulProperties)
        where TEntity : class
    {
        foreach (var entry in db.ChangeTracker.Entries<TEntity>())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Deleted
                && !IsModified(entry, meaningfulProperties))
            {
                continue;
            }

            AddNullableGuidCurrentAndOriginal(entry, "SubscriptionDayId", dayIds);
        }
    }

    private static bool IsCustomerDeliveryDayStatus(SubscriptionDayStatus status)
        => status is SubscriptionDayStatus.Preparing
            or SubscriptionDayStatus.OutForDelivery
            or SubscriptionDayStatus.Delivered
            or SubscriptionDayStatus.Failed
            or SubscriptionDayStatus.Frozen
            or SubscriptionDayStatus.Cancelled;

    private static bool IsAdded(EntityEntry entry) => entry.State == EntityState.Added;
    private static bool IsDeleted(EntityEntry entry) => entry.State == EntityState.Deleted;

    private static bool IsModified(EntityEntry entry, params string[] propertyNames)
        => entry.State == EntityState.Modified && propertyNames.Any(propertyName =>
        {
            var property = entry.Property(propertyName);
            return property.IsModified && !Equals(property.OriginalValue, property.CurrentValue);
        });

    private static void AddNullableGuidCurrentAndOriginal<TEntity>(EntityEntry<TEntity> entry, string propertyName, ISet<Guid> values)
        where TEntity : class
    {
        var current = entry.Property<Guid?>(propertyName).CurrentValue;
        var original = entry.State == EntityState.Added ? null : entry.Property<Guid?>(propertyName).OriginalValue;
        if (current.HasValue && current.Value != Guid.Empty) values.Add(current.Value);
        if (original.HasValue && original.Value != Guid.Empty) values.Add(original.Value);
    }

    private static void AddGuidCurrentAndOriginal<TEntity>(EntityEntry<TEntity> entry, string propertyName, ISet<Guid> values)
        where TEntity : class
    {
        var current = entry.Property<Guid>(propertyName).CurrentValue;
        var original = entry.State == EntityState.Added ? default : entry.Property<Guid>(propertyName).OriginalValue;
        if (current != Guid.Empty) values.Add(current);
        if (original != Guid.Empty) values.Add(original);
    }

    private static Guid? OriginalValue<TEntity>(EntityEntry<TEntity> entry, string propertyName) where TEntity : class
        => entry.State == EntityState.Added ? null : entry.Property<Guid>(propertyName).OriginalValue;

    private static OutboxMessage CreateMessage(Guid dayId, DateTime occurredAtUtc)
    {
        var message = new OutboxMessage();
        var payload = new CustomerDeliveryChangedV1(message.Id, dayId, occurredAtUtc);
        message.EventType = CustomerDeliveryChangedV1.OutboxEventType;
        message.AggregateType = nameof(SubscriptionDay);
        message.AggregateId = dayId.ToString("D");
        message.DeduplicationKey = $"customer-delivery-changed:{dayId:D}:{message.Id:D}";
        message.EventVersion = payload.SchemaVersion;
        message.PayloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        message.CreatedAtUtc = occurredAtUtc;
        return message;
    }
}
