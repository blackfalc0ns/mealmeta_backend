using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class AdminPushTestService : IAdminPushTestService
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AdminPushTestService> _logger;

    public AdminPushTestService(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IDateTimeProvider clock,
        ILogger<AdminPushTestService> logger)
    {
        _db = db;
        _fcm = fcm;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AdminPushTestResultDto> SendTestAsync(
        Guid? adminUserId,
        string? directToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured)
        {
            return new AdminPushTestResultDto(
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
        else if (adminUserId.HasValue)
        {
            tokensToSend = await _db.AdminDeviceTokens
                .Where(x => x.UserId == adminUserId.Value && x.IsActive)
                .Select(x => x.Token)
                .ToListAsync(cancellationToken);
        }

        if (tokensToSend.Count == 0)
        {
            return new AdminPushTestResultDto(
                true,
                0,
                0,
                0,
                ["No active browser tokens found. Please grant notification permission in Admin Dashboard first."]);
        }

        var sent = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var token in tokensToSend)
        {
            var result = await _fcm.SendAsync(
                token,
                "إشعار تجريبي للوحة الإدارة 🚀",
                "تهانينا! تم تفعيل إشعارات الويب للوحة الإدارة بنجاح.",
                new Dictionary<string, string>
                {
                    ["eventKey"] = "admin.test_push",
                    ["deepLink"] = "/admin",
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
            "Admin test push completed for {UserId}: {Sent} sent, {Failed} failed out of {Total} tokens.",
            adminUserId, sent, failed, tokensToSend.Count);

        return new AdminPushTestResultDto(
            true,
            tokensToSend.Count,
            sent,
            failed,
            errors);
    }
}
