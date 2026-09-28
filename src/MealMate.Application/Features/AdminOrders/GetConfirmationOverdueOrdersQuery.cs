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
    int OverdueMinutes);

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
        var overdueCutoff = nowUtc.AddMinutes(-30);

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
                    join restaurant in _db.RestaurantProfiles.AsNoTracking()
                        on order.RestaurantId equals restaurant.Id into restGroup
                    from r in restGroup.DefaultIfEmpty()
                    orderby order.CreatedAtUtc ascending
                    select new
                    {
                        Order = order,
                        RestaurantNameAr = r != null ? r.TradeNameAr : string.Empty,
                        RestaurantNameEn = r != null ? r.TradeNameEn : string.Empty
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
            OverdueMinutes: (int)Math.Max(0, (nowUtc - x.Order.CreatedAtUtc).TotalMinutes)
        )).ToList();

        return Result.Success(new PagedResult<OverdueOrderDto>(items, totalCount, page, pageSize));
    }
}
