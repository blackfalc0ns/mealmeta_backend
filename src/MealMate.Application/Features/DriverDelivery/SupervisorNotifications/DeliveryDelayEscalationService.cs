using MealMate.Application.Common.Abstractions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.SupervisorNotifications;

/// <summary>
/// Scans today's in-progress trips for stops that passed their promised
/// delivery window (plus a grace period) and raises one urgent supervisor
/// alert per late stop. Re-runs are de-duplicated against alerts already
/// persisted for the same stop on the same day.
/// </summary>
public sealed class DeliveryDelayEscalationService(
    IAppDbContext db,
    IDateTimeProvider clock,
    IDeliverySupervisorNotifier notifier)
{
    public const string DelayEventKey = "delivery.stop.delayed";
    public const int GracePeriodMinutes = 15;

    /// <summary>Notification <see cref="Domain.IdentityAccess.DeliveryManagerNotification.Type"/> written for each raised delay alert.</summary>
    public const string DelayNotificationType = "delivery-stop-delayed";

    public async Task DetectAndNotifyAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.UtcNow;
        var kuwaitNow = nowUtc.AddHours(DeliveryWindowParser.KuwaitUtcOffsetHours);
        var todayKuwait = DateOnly.FromDateTime(kuwaitNow);
        var todayStartUtc = DateTime.SpecifyKind(kuwaitNow.Date.AddHours(-DeliveryWindowParser.KuwaitUtcOffsetHours), DateTimeKind.Utc);

        var trips = await db.Trips.AsNoTracking()
            .Include(t => t.DriverAssignments)
            .Where(t => t.Status == TripStatus.InProgress && t.PlannedDate == todayKuwait)
            .ToListAsync(cancellationToken);
        if (trips.Count == 0)
            return;

        var tripIds = trips.Select(t => t.Id).ToList();
        var stops = await db.TripStops.AsNoTracking()
            .Where(s => tripIds.Contains(s.TripId)
                        && (s.Status == TripStopStatus.Pending || s.Status == TripStopStatus.PickedUp))
            .ToListAsync(cancellationToken);
        if (stops.Count == 0)
            return;

        var alertedStopIds = await db.DeliveryManagerNotifications.AsNoTracking()
            .Where(n => n.Type == DelayNotificationType
                        && n.SubjectId != null
                        && n.SentAtUtc >= todayStartUtc)
            .Select(n => n.SubjectId!.Value)
            .ToListAsync(cancellationToken);
        var alertedStopIdSet = alertedStopIds.ToHashSet();

        foreach (var stop in stops)
        {
            var trip = trips.First(t => t.Id == stop.TripId);
            if (!DeliveryWindowParser.TryGetExpectedEndTimeUtc(stop.SlotLabel, trip.ShiftName, nowUtc, out var expectedEndUtc))
                continue;

            var lateMinutes = (int)Math.Floor((nowUtc - expectedEndUtc.AddMinutes(GracePeriodMinutes)).TotalMinutes);
            if (lateMinutes <= 0)
                continue;

            if (alertedStopIdSet.Contains(stop.Id))
                continue;

            var assignment = trip.DriverAssignments.FirstOrDefault(a => a.Status == "Active")
                             ?? trip.DriverAssignments.FirstOrDefault();
            string driverNameAr = string.Empty;
            string driverNameEn = string.Empty;
            if (assignment is not null)
            {
                var driver = await db.DriverProfiles.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == assignment.DriverProfileId, cancellationToken);
                driverNameAr = driver?.FullNameAr ?? string.Empty;
                driverNameEn = driver?.FullNameEn ?? string.Empty;
            }

            await notifier.NotifyStopDelayedAsync(
                trip.RestaurantId,
                stop.Id,
                trip.Id,
                trip.Code,
                driverNameAr,
                driverNameEn,
                stop.OrderCode ?? string.Empty,
                stop.OrderCode ?? string.Empty,
                stop.ZoneNameAr ?? string.Empty,
                stop.ZoneNameEn ?? string.Empty,
                lateMinutes,
                cancellationToken);
        }
    }
}
