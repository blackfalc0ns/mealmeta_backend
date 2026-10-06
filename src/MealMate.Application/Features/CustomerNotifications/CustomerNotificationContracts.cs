using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.CustomerNotifications;

public sealed record CustomerNotificationItemDto(
    Guid Id,
    string EventKey,
    Guid SubjectId,
    Guid? SubscriptionId,
    string TitleAr,
    string TitleEn,
    string BodyAr,
    string BodyEn,
    string? DeepLink,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

public sealed record CustomerUnreadNotificationsDto(bool HasUnreadNotifications, int UnreadNotificationsCount);

public sealed record GetCustomerNotificationsQuery(int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<CustomerNotificationItemDto>>;

public sealed record GetCustomerUnreadNotificationsQuery : IQuery<CustomerUnreadNotificationsDto>;

public sealed record MarkCustomerNotificationReadCommand(Guid NotificationId) : ICommand;

public sealed record MarkAllCustomerNotificationsReadCommand : ICommand;
