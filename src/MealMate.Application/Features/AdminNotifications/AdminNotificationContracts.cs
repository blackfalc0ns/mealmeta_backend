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
    int UnreadCount,
    int TotalCount = 0,
    int Page = 1,
    int PageSize = 30);

public sealed record GetAdminNotificationsQuery(
    int Limit = 30,
    int Page = 1,
    int PageSize = 30,
    bool? UnreadOnly = null) : IQuery<AdminNotificationsListResponse>;

public sealed record MarkAdminNotificationReadCommand(Guid Id) : ICommand;

public sealed record MarkAllAdminNotificationsReadCommand : ICommand;
