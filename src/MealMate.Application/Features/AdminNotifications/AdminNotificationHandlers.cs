using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminNotifications;

public sealed class GetAdminNotificationsQueryHandler
    : IQueryHandler<GetAdminNotificationsQuery, AdminNotificationsListResponse>
{
    private readonly IAppDbContext _db;

    public GetAdminNotificationsQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<AdminNotificationsListResponse>> Handle(
        GetAdminNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 100);

        var notifications = await _db.AdminNotifications
            .AsNoTracking()
            .OrderByDescending(n => n.SentAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var unreadCount = await _db.AdminNotifications
            .AsNoTracking()
            .CountAsync(n => !n.IsRead, cancellationToken);

        var items = notifications.Select(n =>
        {
            string? deepLink = null;
            string? eventKey = null;

            if (!string.IsNullOrWhiteSpace(n.PayloadJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(n.PayloadJson);
                    if (doc.RootElement.TryGetProperty("deepLink", out var dlProp))
                    {
                        deepLink = dlProp.GetString();
                    }
                    if (doc.RootElement.TryGetProperty("eventKey", out var ekProp))
                    {
                        eventKey = ekProp.GetString();
                    }
                }
                catch
                {
                    // Ignore malformed json payload
                }
            }

            return new AdminNotificationDto(
                n.Id,
                n.TitleAr,
                n.TitleEn,
                n.BodyAr,
                n.BodyEn,
                n.IsRead,
                n.SentAtUtc,
                deepLink,
                eventKey);
        }).ToList();

        return new AdminNotificationsListResponse(items, unreadCount);
    }
}

public sealed class MarkAdminNotificationReadCommandHandler
    : ICommandHandler<MarkAdminNotificationReadCommand>
{
    private readonly IAppDbContext _db;

    public MarkAdminNotificationReadCommandHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result> Handle(
        MarkAdminNotificationReadCommand request,
        CancellationToken cancellationToken)
    {
        var notification = await _db.AdminNotifications
            .FirstOrDefaultAsync(n => n.Id == request.Id, cancellationToken);

        if (notification is null)
        {
            return Result.Success();
        }

        notification.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class MarkAllAdminNotificationsReadCommandHandler
    : ICommandHandler<MarkAllAdminNotificationsReadCommand>
{
    private readonly IAppDbContext _db;

    public MarkAllAdminNotificationsReadCommandHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result> Handle(
        MarkAllAdminNotificationsReadCommand request,
        CancellationToken cancellationToken)
    {
        var unread = await _db.AdminNotifications
            .Where(n => !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var item in unread)
        {
            item.IsRead = true;
        }

        if (unread.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
