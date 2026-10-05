using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerNotifications;

public sealed class CustomerNotificationHandlers
    : IQueryHandler<GetCustomerNotificationsQuery, PagedResult<CustomerNotificationItemDto>>,
      IQueryHandler<GetCustomerUnreadNotificationsQuery, CustomerUnreadNotificationsDto>,
      ICommandHandler<MarkCustomerNotificationReadCommand>,
      ICommandHandler<MarkAllCustomerNotificationsReadCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public CustomerNotificationHandlers(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<PagedResult<CustomerNotificationItemDto>>> Handle(
        GetCustomerNotificationsQuery request, CancellationToken cancellationToken)
    {
        if (!TryGetCustomerId(out var error, out var userId)) return error!;
        var pageRequest = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var baseQuery = _db.CustomerNotifications.AsNoTracking().Where(x => x.UserId == userId);
        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var items = await baseQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.NormalizedPageSize)
            .Select(x => new CustomerNotificationItemDto(
                x.Id, x.EventKey, x.SubjectId, x.SubscriptionId,
                x.TitleAr, x.TitleEn, x.BodyAr, x.BodyEn, x.DeepLink,
                x.CreatedAtUtc, x.ReadAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<CustomerNotificationItemDto>(
            items, totalCount, pageRequest.NormalizedPage, pageRequest.NormalizedPageSize));
    }

    public async Task<Result<CustomerUnreadNotificationsDto>> Handle(
        GetCustomerUnreadNotificationsQuery request, CancellationToken cancellationToken)
    {
        if (!TryGetCustomerId(out var error, out var userId)) return error!;
        var unreadCount = await _db.CustomerNotifications.AsNoTracking()
            .CountAsync(x => x.UserId == userId && x.ReadAtUtc == null, cancellationToken);
        return Result.Success(new CustomerUnreadNotificationsDto(unreadCount > 0, unreadCount));
    }

    public async Task<Result> Handle(MarkCustomerNotificationReadCommand request, CancellationToken cancellationToken)
    {
        if (!TryGetCustomerId(out var error, out var userId)) return error!;
        var notification = await _db.CustomerNotifications
            .FirstOrDefaultAsync(x => x.Id == request.NotificationId && x.UserId == userId, cancellationToken);
        if (notification is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Notification not found.");

        notification.ReadAtUtc ??= _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(MarkAllCustomerNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        if (!TryGetCustomerId(out var error, out var userId)) return error!;
        var unread = await _db.CustomerNotifications
            .Where(x => x.UserId == userId && x.ReadAtUtc == null)
            .ToListAsync(cancellationToken);
        var now = _clock.UtcNow;
        foreach (var notification in unread)
        {
            notification.ReadAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private bool TryGetCustomerId(out Error? error, out Guid userId)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            error = Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required.");
            userId = default;
            return false;
        }

        if (_currentUser.UserType != UserType.Customer)
        {
            error = Error.Forbidden(MessageCodes.Auth.Forbidden, "Only customers can access their notification inbox.");
            userId = default;
            return false;
        }

        error = null;
        userId = _currentUser.UserId.Value;
        return true;
    }
}
