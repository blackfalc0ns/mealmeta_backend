using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class RestaurantPushTestService : IRestaurantPushTestService
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RestaurantPushTestService> _logger;

    public RestaurantPushTestService(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IDateTimeProvider clock,
        ILogger<RestaurantPushTestService> logger)
    {
        _db = db;
        _fcm = fcm;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RestaurantPushTestResultDto> SendTestAsync(
        Guid? restaurantUserId,
        string? directToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured)
        {
            return new RestaurantPushTestResultDto(
                false,
                0,
                0,
                0,
                ["Firebase credentials are not configured on server."]);
        }

        var tokensToSend = new List<string>();

        if (!string.IsNullOrWhiteSpace(directToken))
        {
            tokensToSend.Add(directToken.Trim());
        }
        else if (restaurantUserId.HasValue)
        {
            tokensToSend = await _db.RestaurantDeviceTokens
                .Where(x => x.UserId == restaurantUserId.Value && x.IsActive)
                .Select(x => x.Token)
                .ToListAsync(cancellationToken);
        }

        if (tokensToSend.Count == 0)
        {
            return new RestaurantPushTestResultDto(
                true,
                0,
                0,
                0,
                ["No active browser tokens found. Please grant notification permission in Restaurant Dashboard first."]);
        }

        var sent = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var token in tokensToSend)
        {
            var result = await _fcm.SendAsync(
                token,
                "إشعار تجريبي للوحة المطعم 🍽️",
                "تهانينا! تم تفعيل إشعارات الويب للوحة المطعم بنجاح.",
                new Dictionary<string, string>
                {
                    ["eventKey"] = "restaurant.test_push",
                    ["deepLink"] = "/restaurant",
                },
                cancellationToken);

            if (result.IsSuccess)
            {
                sent++;
            }
            else
            {
                failed++;
                if (result.ErrorMessage is not null)
                    errors.Add(result.ErrorMessage);
            }
        }

        _logger.LogInformation(
            "Restaurant test push completed for {UserId}: {Sent} sent, {Failed} failed out of {Total} tokens.",
            restaurantUserId, sent, failed, tokensToSend.Count);

        return new RestaurantPushTestResultDto(
            true,
            tokensToSend.Count,
            sent,
            failed,
            errors);
    }
}
