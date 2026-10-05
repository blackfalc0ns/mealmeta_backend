using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerPush;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CustomerNotifications;

/// <summary>
/// Writes customer in-app inbox entries for real business events. Content and
/// subscription scope are derived from CustomerPushTemplates and the actual
/// entities behind each event key — never fabricated.
/// </summary>
public interface ICustomerNotificationWriter
{
    Task EnsureCreatedAsync(Guid userId, string eventKey, Guid subjectId, CancellationToken cancellationToken = default);
}

public sealed class CustomerNotificationWriter : ICustomerNotificationWriter
{
    private static readonly HashSet<string> SupportedEventKeys = new()
    {
        CustomerPushEventKeys.SubscriptionPaymentConfirmed,
        CustomerPushEventKeys.OrderLocked72h,
        CustomerPushEventKeys.OrderConfirmedByRestaurant,
        CustomerPushEventKeys.DeliveryPrep24h,
        CustomerPushEventKeys.OrderOutForDelivery,
        CustomerPushEventKeys.OrderDelivered,
        CustomerPushEventKeys.InfluencerPromoted,
        CustomerPushEventKeys.OrderReplaced,
    };

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CustomerNotificationWriter> _logger;

    public CustomerNotificationWriter(IAppDbContext db, IDateTimeProvider clock, ILogger<CustomerNotificationWriter> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task EnsureCreatedAsync(Guid userId, string eventKey, Guid subjectId, CancellationToken cancellationToken = default)
    {
        if (!SupportedEventKeys.Contains(eventKey))
        {
            throw new NotSupportedException(
                $"Customer notification event key '{eventKey}' is not supported; refusing to store a fabricated inbox entry.");
        }

        var copy = await ResolveEventCopyAsync(userId, eventKey, subjectId, cancellationToken);
        if (copy is null)
        {
            _logger.LogWarning(
                "Skipping customer inbox entry for user {UserId}, event {EventKey}, subject {SubjectId}: the business event entity was not found.",
                userId, eventKey, subjectId);
            return;
        }

        var exists = await _db.CustomerNotifications.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.EventKey == eventKey && x.SubjectId == subjectId, cancellationToken);
        if (exists) return;

        var notification = new CustomerNotification
        {
            UserId = userId,
            EventKey = eventKey,
            SubjectId = subjectId,
            SubscriptionId = copy.SubscriptionId,
            TitleAr = copy.TitleAr,
            TitleEn = copy.TitleEn,
            BodyAr = copy.BodyAr,
            BodyEn = copy.BodyEn,
            DeepLink = copy.DeepLink,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.CustomerNotifications.Add(notification);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!await _db.CustomerNotifications.AsNoTracking()
                    .AnyAsync(x => x.UserId == userId && x.EventKey == eventKey && x.SubjectId == subjectId, cancellationToken))
            {
                throw;
            }

            // Lost a concurrent-create race: the unique key held. Detach the failed
            // insert so the caller's next SaveChanges does not retry and fail again.
            if (_db is DbContext ef)
            {
                ef.Entry(notification).State = EntityState.Detached;
            }

            _logger.LogInformation(
                "Customer inbox entry for user {UserId}, event {EventKey}, subject {SubjectId} already existed (concurrent write).",
                userId, eventKey, subjectId);
        }
    }

    private async Task<NotificationCopy?> ResolveEventCopyAsync(Guid userId, string eventKey, Guid subjectId, CancellationToken ct)
    {
        if (eventKey == CustomerPushEventKeys.InfluencerPromoted)
        {
            var influencer = await _db.InfluencerProfiles.AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => new { x.InfluencerCode, x.DefaultCommissionRate })
                .FirstOrDefaultAsync(ct);
            if (influencer?.InfluencerCode is null) return null;
            var rate = influencer.DefaultCommissionRate?.AsPercent ?? 0m;
            return FromCopy(
                CustomerPushTemplates.InfluencerPromoted(influencer.InfluencerCode, rate, isRtl: true),
                CustomerPushTemplates.InfluencerPromoted(influencer.InfluencerCode, rate, isRtl: false),
                SubscriptionId: null);
        }

        if (eventKey == CustomerPushEventKeys.SubscriptionPaymentConfirmed)
        {
            var subscriptionExists = await _db.Subscriptions.AsNoTracking().AnyAsync(x => x.Id == subjectId, ct);
            if (!subscriptionExists) return null;
            return FromCopy(
                CustomerPushTemplates.SubscriptionPaymentConfirmed(isRtl: true),
                CustomerPushTemplates.SubscriptionPaymentConfirmed(isRtl: false),
                subjectId);
        }

        // Remaining events are day-scoped: subjectId is the SubscriptionDay id.
        var day = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.Id == subjectId)
            .Select(x => new { x.SubscriptionId, x.ServiceDate, x.RestaurantId })
            .FirstOrDefaultAsync(ct);
        if (day is null) return null;

        if (eventKey == CustomerPushEventKeys.OrderReplaced)
        {
            var restaurant = day.RestaurantId is { } restaurantId
                ? await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
                    .Where(x => x.SubscriptionId == day.SubscriptionId && x.RestaurantId == restaurantId)
                    .Select(x => new { x.NameAr, x.NameEn })
                    .FirstOrDefaultAsync(ct)
                : null;
            return FromCopy(
                CustomerPushTemplates.OrderReplaced(day.ServiceDate, subjectId, restaurant?.NameAr ?? string.Empty, isRtl: true),
                CustomerPushTemplates.OrderReplaced(day.ServiceDate, subjectId, restaurant?.NameEn ?? string.Empty, isRtl: false),
                day.SubscriptionId);
        }

        return eventKey switch
        {
            CustomerPushEventKeys.OrderLocked72h => FromCopy(
                CustomerPushTemplates.OrderLocked72h(day.ServiceDate, true),
                CustomerPushTemplates.OrderLocked72h(day.ServiceDate, false),
                day.SubscriptionId),
            CustomerPushEventKeys.OrderConfirmedByRestaurant => FromCopy(
                CustomerPushTemplates.OrderConfirmedByRestaurant(day.ServiceDate, true),
                CustomerPushTemplates.OrderConfirmedByRestaurant(day.ServiceDate, false),
                day.SubscriptionId),
            CustomerPushEventKeys.DeliveryPrep24h => FromCopy(
                CustomerPushTemplates.DeliveryPrep24h(day.ServiceDate, true),
                CustomerPushTemplates.DeliveryPrep24h(day.ServiceDate, false),
                day.SubscriptionId),
            CustomerPushEventKeys.OrderOutForDelivery => FromCopy(
                CustomerPushTemplates.OrderOutForDelivery(day.ServiceDate, true),
                CustomerPushTemplates.OrderOutForDelivery(day.ServiceDate, false),
                day.SubscriptionId),
            CustomerPushEventKeys.OrderDelivered => FromCopy(
                CustomerPushTemplates.OrderDelivered(day.ServiceDate, true),
                CustomerPushTemplates.OrderDelivered(day.ServiceDate, false),
                day.SubscriptionId),
            _ => null,
        };
    }

    private static NotificationCopy FromCopy(CustomerPushTemplates.PushCopy ar, CustomerPushTemplates.PushCopy en, Guid? SubscriptionId)
        => new(ar.Title, en.Title, ar.Body, en.Body, ar.DeepLink, SubscriptionId);

    private sealed record NotificationCopy(
        string TitleAr,
        string TitleEn,
        string BodyAr,
        string BodyEn,
        string? DeepLink,
        Guid? SubscriptionId);
}
