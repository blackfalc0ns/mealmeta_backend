using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

/// <summary>
/// Persists delivery-supervisor notifications to the admin in-app bell and
/// fans them out over FCM to active admin devices. Every step is best-effort:
/// notification failures must never break the delivery workflow that raised them.
/// </summary>
public sealed class DeliverySupervisorNotifier : IDeliverySupervisorNotifier
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly ILogger<DeliverySupervisorNotifier> _logger;

    public DeliverySupervisorNotifier(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        ILogger<DeliverySupervisorNotifier> logger)
    {
        _db = db;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task NotifyPickupCompletedAsync(
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        int boxesCount,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "اكتمل مسح الصناديق ✅";
        var titleEn = "Pickup Completed";
        var bodyAr = string.IsNullOrWhiteSpace(driverNameAr)
            ? $"تم مسح وتصوير كل الصناديق ({boxesCount}) الخاصة بالرحلة {tripCode} وجاهزة للتوصيل."
            : $"أنهى السائق {driverNameAr} مسح وتصوير كل الصناديق ({boxesCount}) للرحلة {tripCode}.";
        var bodyEn = string.IsNullOrWhiteSpace(driverNameEn)
            ? $"All {boxesCount} boxes of trip {tripCode} were scanned and photographed — ready for delivery."
            : $"Driver {driverNameEn} finished scanning and photographing all {boxesCount} boxes of trip {tripCode}.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.pickup.completed",
            ["tripId"] = tripId.ToString(),
            ["tripCode"] = tripCode,
            ["boxesCount"] = boxesCount.ToString()
        };

        await NotifyAdminsAsync(titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    public async Task NotifyTripDeliveredAsync(
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        int boxesCount,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "تم تسليم كل الطلبات 🎉";
        var titleEn = "All Orders Delivered";
        var bodyAr = string.IsNullOrWhiteSpace(driverNameAr)
            ? $"أنهت الرحلة {tripCode} توصيل كل الطلبات ({boxesCount}) بنجاح."
            : $"أنهى السائق {driverNameAr} توصيل كل الطلبات ({boxesCount}) للرحلة {tripCode} بنجاح.";
        var bodyEn = string.IsNullOrWhiteSpace(driverNameEn)
            ? $"Trip {tripCode} delivered all {boxesCount} orders successfully."
            : $"Driver {driverNameEn} delivered all {boxesCount} orders of trip {tripCode}.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.trip.delivered",
            ["tripId"] = tripId.ToString(),
            ["tripCode"] = tripCode,
            ["boxesCount"] = boxesCount.ToString()
        };

        await NotifyAdminsAsync(titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    public async Task NotifyStopDelayedAsync(
        Guid stopId,
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        string orderCode,
        string boxCode,
        string zoneNameAr,
        string zoneNameEn,
        int minutesLate,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "تأخير عاجل في التسليم ⚠️";
        var titleEn = "Urgent Delivery Delay";
        var bodyAr = $"تأخير {minutesLate} دقيقة في تسليم الطلب {orderCode}"
                     + (string.IsNullOrWhiteSpace(zoneNameAr) ? "." : $" - منطقة {zoneNameAr}.")
                     + (string.IsNullOrWhiteSpace(driverNameAr) ? string.Empty : $" السائق: {driverNameAr}.");
        var bodyEn = $"Order {orderCode} is {minutesLate} minutes past its delivery window"
                     + (string.IsNullOrWhiteSpace(zoneNameEn) ? "." : $" ({zoneNameEn}).")
                     + (string.IsNullOrWhiteSpace(driverNameEn) ? string.Empty : $" Driver: {driverNameEn}.");

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "delivery.stop.delayed",
            ["priority"] = "urgent",
            ["stopId"] = stopId.ToString(),
            ["tripId"] = tripId.ToString(),
            ["tripCode"] = tripCode,
            ["orderCode"] = orderCode,
            ["boxCode"] = boxCode,
            ["minutesLate"] = minutesLate.ToString()
        };

        await NotifyAdminsAsync(titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    private async Task NotifyAdminsAsync(
        string titleAr,
        string titleEn,
        string bodyAr,
        string bodyEn,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        try
        {
            _db.AdminNotifications.Add(new AdminNotification
            {
                TitleAr = titleAr,
                TitleEn = titleEn,
                BodyAr = bodyAr,
                BodyEn = bodyEn,
                Channel = NotificationChannel.InApp,
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(data)
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist supervisor notification {EventKey}.", data.GetValueOrDefault("eventKey"));
            return;
        }

        if (!_fcm.IsConfigured)
            return;

        List<string> tokens;
        try
        {
            tokens = await _db.AdminDeviceTokens
                .Where(x => x.IsActive)
                .Select(x => x.Token)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load admin device tokens for {EventKey}.", data.GetValueOrDefault("eventKey"));
            return;
        }

        foreach (var token in tokens)
        {
            try
            {
                await _fcm.SendAsync(token, titleAr, bodyAr, data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send supervisor push for {EventKey}.", data.GetValueOrDefault("eventKey"));
            }
        }
    }
}
