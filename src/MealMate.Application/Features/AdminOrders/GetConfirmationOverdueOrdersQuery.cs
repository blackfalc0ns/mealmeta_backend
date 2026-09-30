using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminOrders;

public sealed record OverdueOrderDto(
    Guid OrderId,
    Guid RestaurantId,
    string RestaurantNameAr,
    string RestaurantNameEn,
    Guid SubscriptionDayId,
    DateOnly ServiceDate,
    int BoxCount,
    DateTime DispatchedAtUtc,
    int OverdueMinutes,
    Guid? CustomerId = null,
    string? CustomerDisplayName = null,
    string? RestaurantName = null,
    string? DeliveryDate = null,
    int HoursUntilDelivery = 0,
    string Phase = "confirmation_overdue",
    string ConfirmationStatus = "overdue",
    DateTime? ConfirmationDeadlineAt = null,
    string ReplacementWindowStatus = "not_opened",
    bool IsCustomerEditLocked = true,
    string CountryCode = "KWT");

public sealed record GetConfirmationOverdueOrdersQuery(
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PagedResult<OverdueOrderDto>>;

public sealed class GetConfirmationOverdueOrdersQueryHandler : IQueryHandler<GetConfirmationOverdueOrdersQuery, PagedResult<OverdueOrderDto>>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public GetConfirmationOverdueOrdersQueryHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<PagedResult<OverdueOrderDto>>> Handle(
        GetConfirmationOverdueOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;
        var confirmationMinutes = (await MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.ReadAsync(_db, cancellationToken)).Cutoff.EffectiveConfirmationMinutes;
        var overdueCutoff = nowUtc.AddMinutes(-confirmationMinutes);

        var page = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = request.PageSize switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => request.PageSize
        };

        var query = from order in _db.RestaurantDailyOrders.AsNoTracking()
                    where order.Status == OrderStatus.AwaitingRestaurantConfirmation
                          && order.CreatedAtUtc <= overdueCutoff
                    join day in _db.SubscriptionDays.AsNoTracking()
                        on order.SubscriptionDayId equals day.Id into dayGroup
                    from d in dayGroup.DefaultIfEmpty()
                    join sub in _db.Subscriptions.AsNoTracking()
                        on d.SubscriptionId equals sub.Id into subGroup
                    from s in subGroup.DefaultIfEmpty()
                    join restaurant in _db.RestaurantProfiles.AsNoTracking()
                        on order.RestaurantId equals restaurant.Id into restGroup
                    from r in restGroup.DefaultIfEmpty()
                    orderby order.CreatedAtUtc ascending
                    select new
                    {
                        Order = order,
                        RestaurantNameAr = r != null ? r.TradeNameAr : string.Empty,
                        RestaurantNameEn = r != null ? r.TradeNameEn : string.Empty,
                        CustomerId = s != null ? (Guid?)s.CustomerUserId : null
                    };

        var totalCount = await query.CountAsync(cancellationToken);

        var rawItems = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rawItems.Select(x => new OverdueOrderDto(
            OrderId: x.Order.Id,
            RestaurantId: x.Order.RestaurantId,
            RestaurantNameAr: x.RestaurantNameAr,
            RestaurantNameEn: x.RestaurantNameEn,
            SubscriptionDayId: x.Order.SubscriptionDayId,
            ServiceDate: x.Order.ServiceDate,
            BoxCount: x.Order.BoxCount,
            DispatchedAtUtc: x.Order.CreatedAtUtc,
            OverdueMinutes: (int)Math.Max(0, (nowUtc - x.Order.CreatedAtUtc).TotalMinutes),
            CustomerId: x.CustomerId,
            CustomerDisplayName: x.CustomerId.HasValue ? x.CustomerId.Value.ToString()[..8] : string.Empty,
            RestaurantName: string.IsNullOrWhiteSpace(x.RestaurantNameEn) ? x.RestaurantNameAr : x.RestaurantNameEn,
            DeliveryDate: x.Order.ServiceDate.ToString("yyyy-MM-dd"),
            HoursUntilDelivery: (int)Math.Max(0, (x.Order.ServiceDate.ToDateTime(TimeOnly.MinValue) - nowUtc).TotalHours),
            Phase: "confirmation_overdue",
            ConfirmationStatus: "overdue",
            ConfirmationDeadlineAt: x.Order.CreatedAtUtc.AddMinutes(confirmationMinutes),
            ReplacementWindowStatus: "not_opened",
            IsCustomerEditLocked: true,
            CountryCode: "KWT"
        )).ToList();

        return Result.Success(new PagedResult<OverdueOrderDto>(items, totalCount, page, pageSize));
    }
}

