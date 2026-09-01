using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerPush;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class CustomerPushTestService : ICustomerPushTestService
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CustomerPushTestService> _logger;

    public CustomerPushTestService(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IDateTimeProvider clock,
        ILogger<CustomerPushTestService> logger)
    {
        _db = db;
        _fcm = fcm;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CustomerPushTestResultDto> SendTestAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured)
        {
            return new CustomerPushTestResultDto(
                false,
                0,
                0,
                0,
                ["Firebase credentials are missing. Set Firebase:CredentialPath or GOOGLE_APPLICATION_CREDENTIALS."]);
        }

        var tokens = await _db.CustomerDeviceTokens
            .Where(x => x.UserId == customerUserId && x.IsActive)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            return new CustomerPushTestResultDto(
                true,
                0,
                0,
                0,
                ["No active device tokens. Register one via PUT /customers/me/device-tokens first."]);
        }

        var preferredLanguage = await _db.CustomerProfiles.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .Select(x => x.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);
        var isRtl = !string.Equals(preferredLanguage, "en", StringComparison.OrdinalIgnoreCase);

        var title = isRtl ? "اختبار MealMate" : "MealMate test";
        var body = isRtl
            ? "الإشعار التجريبي وصل بنجاح."
            : "Your test notification was delivered successfully.";

        var sent = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var device in tokens)
        {
            var result = await _fcm.SendAsync(
                device.Token,
                title,
                body,
                new Dictionary<string, string>
                {
                    ["eventKey"] = "push.test",
                    ["subjectId"] = Guid.Empty.ToString(),
                    ["deepLink"] = "mealmate://home",
                },
                cancellationToken);

            if (result.IsSuccess)
            {
                sent++;
                device.LastSeenAtUtc = _clock.UtcNow;
                continue;
            }

            failed++;
            errors.Add(result.ErrorMessage ?? "Unknown FCM error");

            if (result.IsInvalidToken)
            {
                device.IsActive = false;
                device.ModifiedAtUtc = _clock.UtcNow;
                _logger.LogInformation("Deactivated invalid FCM token during test push for user {UserId}.", customerUserId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CustomerPushTestResultDto(
            true,
            tokens.Count,
            sent,
            failed,
            errors);
    }
}
