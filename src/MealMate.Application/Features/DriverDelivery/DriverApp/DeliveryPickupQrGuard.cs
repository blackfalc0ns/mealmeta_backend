using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

/// <summary>Delivery must repeat the QR recorded for this driver's pickup of this order.</summary>
internal static class DeliveryPickupQrGuard
{
    internal static string Normalize(string value) => value.Trim().TrimStart('#');

    public static async Task<Error?> ValidateAsync(IAppDbContext db, ILocalizedMessageProvider messages,
        TripStop stop, Guid? driverId, string? barcode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(barcode) || string.IsNullOrWhiteSpace(Normalize(barcode)))
            return Failure("Orders.DeliveryBarcodeRequired");
        if (stop.Status != TripStopStatus.PickedUp)
            return Failure("Orders.DeliveryPickupScanRequired");

        // Follow only completed, receipted custody transfers for this exact stop.
        // Replacement drivers must still scan the original pickup QR.
        var pickupTripId = stop.TripId;
        var pickupDriverId = driverId;
        var visited = new HashSet<(Guid, Guid?)>();
        PickupScan? pickup = null;
        while (visited.Add((pickupTripId, pickupDriverId)))
        {
            // Never bind by display-code suffix or nullable ID equality.
            Expression<Func<PickupScan, bool>> belongsToOrder = p => p.TripId == pickupTripId && p.DriverProfileId == pickupDriverId
                && ((stop.SubscriptionDayId.HasValue && p.SubscriptionDayId == stop.SubscriptionDayId)
                    || (stop.DailyOrderId.HasValue && p.OrderId == stop.DailyOrderId))
                && (!stop.SubscriptionDayId.HasValue || !p.SubscriptionDayId.HasValue || p.SubscriptionDayId == stop.SubscriptionDayId)
                && (!stop.DailyOrderId.HasValue || !p.OrderId.HasValue || p.OrderId == stop.DailyOrderId);
            var persisted = await db.PickupScans.AsNoTracking().Where(belongsToOrder)
                .OrderByDescending(p => p.ScannedAtUtc).ThenByDescending(p => p.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
            // Offline pickup and delivery may be queued in the same transaction.
            // Include that validated pickup before SaveChanges commits the batch.
            pickup = db.PickupScans.Local.Where(belongsToOrder.Compile())
                .Concat(persisted is null ? Enumerable.Empty<PickupScan>() : new[] { persisted })
                .OrderByDescending(p => p.ScannedAtUtc).ThenByDescending(p => p.CreatedAtUtc).FirstOrDefault();
            if (pickup is not null) break;

            var custody = await db.DriverSupportHandovers.AsNoTracking()
                .Where(h => h.Status == "Completed" && h.CompletedAtUtc.HasValue
                    && h.ReplacementTripId == pickupTripId && h.ReplacementDriverProfileId == pickupDriverId
                    && h.Boxes.Any(b => b.TripStopId == stop.Id && b.Status == "Received" && b.ScannedAtUtc.HasValue))
                .OrderByDescending(h => h.CompletedAtUtc)
                .Select(h => new { h.TripId, h.OriginalDriverProfileId }).FirstOrDefaultAsync(ct);
            if (custody is null) break;
            pickupTripId = custody.TripId;
            pickupDriverId = custody.OriginalDriverProfileId;
        }

        if (pickup is null || string.IsNullOrWhiteSpace(pickup.BarcodeValue))
            return Failure("Orders.DeliveryPickupScanRequired");

        return string.Equals(Normalize(barcode), Normalize(pickup.BarcodeValue), StringComparison.Ordinal)
            ? null : Failure("Orders.BarcodeMismatch");

        Error Failure(string code) => Error.Validation(code, messages.GetMessage(code));
    }
}
