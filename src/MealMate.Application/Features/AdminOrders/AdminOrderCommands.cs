using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminOrders;

// --- DTOs ---

public sealed record ManualReassignOrderRequest(
    Guid TargetRestaurantId,
    string? Reason = null);

public sealed record OpenReplacementWindowOrderRequest(
    string? Reason = null,
    bool NotifyCustomer = true);

public sealed record OpenReplacementWindowResponseDto(
    Guid OrderId,
    DateTime WindowOpensAt,
    DateTime WindowClosesAt);

public sealed record ApplyOrderExceptionRequest(
    string ExceptionType,
    string Reason,
    Guid? NewRestaurantId = null,
    Guid? NewMealId = null,
    string? ActorNote = null);

public sealed record OrderExceptionResponseDto(
    Guid OrderId,
    Guid AuditLogId,
    DateTime AppliedAt);

public sealed record ReplacementWindowRowDto(
    Guid OrderId,
    string CustomerDisplayName,
    string OriginalRestaurantName,
    DateTime WindowOpensAt,
    DateTime WindowClosesAt,
    string Status,
    int HoursRemaining);

public sealed record ReplacementWindowListResponseDto(
    IReadOnlyList<ReplacementWindowRowDto> Items,
    int TotalCount);

// --- Commands & Queries ---

public sealed record ManualReassignOrderCommand(
    Guid OrderId,
    Guid TargetRestaurantId,
    string? Reason = null) : ICommand<ReassignmentOutcome>;

public sealed class ManualReassignOrderCommandHandler : ICommandHandler<ManualReassignOrderCommand, ReassignmentOutcome>
{
    private readonly IAppDbContext _db;
    private readonly IAdminCheckoutOperationsService _checkoutOperations;
    private readonly ICurrentUser _currentUser;

    public ManualReassignOrderCommandHandler(
        IAppDbContext db,
        IAdminCheckoutOperationsService checkoutOperations,
        ICurrentUser currentUser)
    {
        _db = db;
        _checkoutOperations = checkoutOperations;
        _currentUser = currentUser;
    }

    public async Task<Result<ReassignmentOutcome>> Handle(ManualReassignOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.RestaurantDailyOrders.FirstOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<ReassignmentOutcome>(Error.NotFound("order.not_found", $"Order {request.OrderId} was not found."));
        }

        var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.Id == order.SubscriptionDayId, cancellationToken);
        if (day is null)
        {
            return Result.Failure<ReassignmentOutcome>(Error.NotFound("subscription_day.not_found", $"Subscription day {order.SubscriptionDayId} was not found."));
        }

        var adminId = _currentUser.UserId ?? Guid.Empty;
        var adminType = _currentUser.UserType ?? UserType.Admin;

        var outcomeResult = await _checkoutOperations.ReassignRestaurantAsync(
            day.SubscriptionId,
            day.ServiceDate,
            new ReassignRestaurantRequest(request.TargetRestaurantId),
            adminId,
            adminType,
            cancellationToken);

        if (outcomeResult.IsFailure)
        {
            return outcomeResult;
        }

        var outcome = outcomeResult.Value;
        if (outcome.ErrorCode is not null)
        {
            return Result.Failure<ReassignmentOutcome>(Error.Validation(outcome.ErrorCode, "Restaurant reassignment failed."));
        }

        order.RestaurantId = request.TargetRestaurantId;
        order.Status = OrderStatus.ReplacementPending;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(outcome);
    }
}

public sealed record OpenReplacementWindowCommand(
    Guid OrderId,
    string? Reason = null,
    bool NotifyCustomer = true) : ICommand<OpenReplacementWindowResponseDto>;

public sealed class OpenReplacementWindowCommandHandler : ICommandHandler<OpenReplacementWindowCommand, OpenReplacementWindowResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public OpenReplacementWindowCommandHandler(IAppDbContext db, IDateTimeProvider clock,
        IApplicationTransactionExecutor? transactions = null, IApplicationLockManager? locks = null)
    {
        _db = db;
        _clock = clock;
        _transactions = transactions;
        _locks = locks;
    }

    public async Task<Result<OpenReplacementWindowResponseDto>> Handle(OpenReplacementWindowCommand request, CancellationToken cancellationToken)
        => _transactions is null
            ? await OpenAsync(request, cancellationToken)
            : await _transactions.ExecuteResultAsync(token => OpenAsync(request, token), cancellationToken);

    private async Task<Result<OpenReplacementWindowResponseDto>> OpenAsync(OpenReplacementWindowCommand request, CancellationToken cancellationToken)
    {
        if (_locks is not null)
        {
            var subscriptionId = await (from o in _db.RestaurantDailyOrders.AsNoTracking()
                                        join d in _db.SubscriptionDays.AsNoTracking() on o.SubscriptionDayId equals d.Id
                                        where o.Id == request.OrderId
                                        select (Guid?)d.SubscriptionId).SingleOrDefaultAsync(cancellationToken);
            if (subscriptionId.HasValue) await _locks.LockSubscriptionAsync(subscriptionId.Value, cancellationToken);
        }
        var order = await _db.RestaurantDailyOrders.FirstOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<OpenReplacementWindowResponseDto>(Error.NotFound("order.not_found", $"Order {request.OrderId} was not found."));
        }

        var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.Id == order.SubscriptionDayId, cancellationToken);
        if (day is null)
        {
            return Result.Failure<OpenReplacementWindowResponseDto>(Error.NotFound("subscription_day.not_found", $"Subscription day {order.SubscriptionDayId} was not found."));
        }

        var now = _clock.UtcNow;
        if (day.Status is not (SubscriptionDayStatus.ReplacementRequired or SubscriptionDayStatus.ReplacementWindowOpen
            or SubscriptionDayStatus.Open or SubscriptionDayStatus.CustomerSelected or SubscriptionDayStatus.AutoSelected))
            return Result.Failure<OpenReplacementWindowResponseDto>(Error.Conflict(MessageCodes.Calendar.SelectionLocked));

        // Retrying an active window must not grant another 24 hours or reset the customer's draft.
        if (day.ReplacementWindowOpensAtUtc is { } opened && day.ReplacementWindowClosesAtUtc is { } closes && now < closes)
            return Result.Success(new OpenReplacementWindowResponseDto(order.Id, opened, closes));

        if (day.Status is not (SubscriptionDayStatus.ReplacementRequired or SubscriptionDayStatus.ReplacementWindowOpen))
            return Result.Failure<OpenReplacementWindowResponseDto>(Error.Conflict(MessageCodes.Calendar.SelectionLocked));

        day.OpenReplacementWindow(now);
        order.Status = OrderStatus.ReplacementPending;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new OpenReplacementWindowResponseDto(
            OrderId: order.Id,
            WindowOpensAt: day.ReplacementWindowOpensAtUtc!.Value,
            WindowClosesAt: day.ReplacementWindowClosesAtUtc!.Value));
    }
}

public sealed record ApplyOrderExceptionCommand(
    Guid OrderId,
    string ExceptionType,
    string Reason,
    Guid? NewRestaurantId = null,
    Guid? NewMealId = null,
    string? ActorNote = null) : ICommand<OrderExceptionResponseDto>;

public sealed class ApplyOrderExceptionCommandHandler : ICommandHandler<ApplyOrderExceptionCommand, OrderExceptionResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ApplyOrderExceptionCommandHandler(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<OrderExceptionResponseDto>> Handle(ApplyOrderExceptionCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.RestaurantDailyOrders.FirstOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<OrderExceptionResponseDto>(Error.NotFound("order.not_found", $"Order {request.OrderId} was not found."));
        }

        var now = _clock.UtcNow;
        var audit = new AuditLog
        {
            ActorUserId = _currentUser.UserId,
            Action = $"OrderException:{request.ExceptionType}",
            EntityType = "RestaurantDailyOrder",
            EntityId = order.Id,
            Reason = request.Reason,
            AfterJson = $"{{\"newRestaurantId\":\"{request.NewRestaurantId}\",\"newMealId\":\"{request.NewMealId}\",\"actorNote\":\"{request.ActorNote}\"}}",
            OccurredAtUtc = now
        };

        _db.AuditLogs.Add(audit);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new OrderExceptionResponseDto(order.Id, audit.Id, now));
    }
}

public sealed record GetReplacementWindowsQuery() : IQuery<ReplacementWindowListResponseDto>;

public sealed class GetReplacementWindowsQueryHandler : IQueryHandler<GetReplacementWindowsQuery, ReplacementWindowListResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public GetReplacementWindowsQueryHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<ReplacementWindowListResponseDto>> Handle(GetReplacementWindowsQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var query = from order in _db.RestaurantDailyOrders.AsNoTracking()
                    where order.Status == OrderStatus.ReplacementPending
                    join day in _db.SubscriptionDays.AsNoTracking()
                        on order.SubscriptionDayId equals day.Id
                    where day.ReplacementWindowOpensAtUtc != null && day.ReplacementWindowClosesAtUtc != null
                    join restaurant in _db.RestaurantProfiles.AsNoTracking()
                        on order.RestaurantId equals restaurant.Id into restGroup
                    from r in restGroup.DefaultIfEmpty()
                    orderby day.ReplacementWindowOpensAtUtc descending
                    select new
                    {
                        OrderId = order.Id,
                        RestaurantName = r != null ? (r.TradeNameEn ?? r.TradeNameAr) : string.Empty,
                        OpensAt = day.ReplacementWindowOpensAtUtc!.Value,
                        ClosesAt = day.ReplacementWindowClosesAtUtc!.Value
                    };

        var rawItems = await query.ToListAsync(cancellationToken);

        var items = rawItems.Select(x => new ReplacementWindowRowDto(
            OrderId: x.OrderId,
            CustomerDisplayName: string.Empty,
            OriginalRestaurantName: x.RestaurantName,
            WindowOpensAt: x.OpensAt,
            WindowClosesAt: x.ClosesAt,
            Status: x.ClosesAt <= now ? "expired" : "open",
            HoursRemaining: (int)Math.Max(0, Math.Ceiling((x.ClosesAt - now).TotalHours))
        )).ToList();

        return Result.Success(new ReplacementWindowListResponseDto(items, items.Count));
    }
}
