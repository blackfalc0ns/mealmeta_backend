using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.AdminNotifications;

public sealed record AdminNotificationDto(
    Guid Id,
    string TitleAr,
    string TitleEn,
    string? BodyAr,
    string? BodyEn,
    bool IsRead,
    DateTime SentAtUtc,
    string? DeepLink,
    string? EventKey);

public sealed record AdminNotificationsListResponse(
    IReadOnlyList<AdminNotificationDto> Items,
    int UnreadCount);

public sealed record GetAdminNotificationsQuery(int Limit = 30) : IQuery<AdminNotificationsListResponse>;

public sealed record MarkAdminNotificationReadCommand(Guid Id) : ICommand;

public sealed record MarkAllAdminNotificationsReadCommand : ICommand;
