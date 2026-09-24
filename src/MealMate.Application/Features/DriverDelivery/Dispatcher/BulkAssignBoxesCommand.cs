using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record BulkAssignBoxesCommand(
    IReadOnlyList<Guid> BoxIds,
    Guid DriverId,
    string? Notes = null) : ICommand<BulkAssignBoxesResponseDto>;

public sealed record BulkAssignBoxesResponseDto(
    Guid TripId,
    string TripCode,
    int AssignedCount,
    Guid DriverId,
    string DriverName,
    IReadOnlyList<Guid> AssignedBoxIds,
    DateTime AssignedAtUtc,
    string Message);

public sealed class BulkAssignBoxesCommandValidator : AbstractValidator<BulkAssignBoxesCommand>
{
    public BulkAssignBoxesCommandValidator()
    {
        RuleFor(x => x.BoxIds)
            .NotEmpty().WithMessage("يجب تحديد صندوق واحد على الأقل للإسناد")
            .Must(x => x.Count <= 25).WithMessage("لا يمكن إسناد أكثر من 25 صندوق في دفعة واحدة");

        RuleFor(x => x.DriverId)
            .NotEmpty().WithMessage("يجب تحديد سائق صالح للإسناد");
    }
}

public sealed class BulkAssignBoxesCommandHandler : ICommandHandler<BulkAssignBoxesCommand, BulkAssignBoxesResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDriverPushNotifier? _driverPushNotifier;

    public BulkAssignBoxesCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IDriverPushNotifier? driverPushNotifier = null)
    {
        _db = db;
        _messages = messages;
        _driverPushNotifier = driverPushNotifier;
    }

    public async Task<Result<BulkAssignBoxesResponseDto>> Handle(
        BulkAssignBoxesCommand request,
        CancellationToken cancellationToken)
    {
        var driver = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == request.DriverId, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(
                MessageCodes.Dispatcher.DriverNotFound,
                _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver was not found."));
        }

        var driverName = driver.FullNameAr ?? driver.FullNameEn ?? "السائق المعين";
        var now = DateTime.UtcNow;

        var distinctBoxIds = request.BoxIds.Distinct().ToList();
        if (distinctBoxIds.Count == 0)
        {
            return Error.Validation(MessageCodes.Validation.Required, "No boxes specified for assignment.");
        }

        // 1. Check for existing TripStops matching requested BoxIds
        var existingStops = await _db.TripStops
            .Include(s => s.Trip)
            .Where(s => distinctBoxIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        var foundStopIds = existingStops.Select(s => s.Id).ToHashSet();
        var remainingBoxIds = distinctBoxIds.Where(id => !foundStopIds.Contains(id)).ToList();

        // 2. Check remaining IDs in SubscriptionDays
        var days = remainingBoxIds.Count > 0
            ? await _db.SubscriptionDays
                .Include(d => d.MealSelections)
                .Where(d => remainingBoxIds.Contains(d.Id))
                .ToListAsync(cancellationToken)
            : new List<SubscriptionDay>();

        // 3. Determine restaurant and planned service date
        var restaurantId = existingStops.FirstOrDefault()?.Trip.RestaurantId
            ?? days.FirstOrDefault()?.RestaurantId
            ?? driver.RestaurantId
            ?? Guid.Empty;

        var plannedDate = days.FirstOrDefault()?.ServiceDate
            ?? existingStops.FirstOrDefault()?.Trip.PlannedDate
            ?? DateOnly.FromDateTime(now);

        // 4. Find or create unified Trip for this driver on the target date
        var trip = await _db.Trips
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .FirstOrDefaultAsync(t => t.RestaurantId == restaurantId
                && !t.IsDeleted
                && t.Status != TripStatus.Completed
                && t.Status != TripStatus.Cancelled
                && t.PlannedDate == plannedDate
                && t.DriverAssignments.Any(da => da.DriverProfileId == request.DriverId && da.Status == "Active"), cancellationToken);

        if (trip == null)
        {
            trip = new Trip
            {
                RestaurantId = restaurantId,
                Code = $"TRP-{now:yyyyMMdd}-{Math.Abs(Guid.NewGuid().GetHashCode()) % 9000 + 1000}",
                Status = TripStatus.Assigned,
                PlannedDate = plannedDate,
                CreatedAtUtc = now
            };
            _db.Trips.Add(trip);

            _db.DriverAssignments.Add(new DriverAssignment
            {
                TripId = trip.Id,
                DriverProfileId = request.DriverId,
                AssignedAtUtc = now,
                Status = "Active"
            });
        }
        else
        {
            trip.Status = TripStatus.Assigned;
            var activeAssignment = trip.DriverAssignments.FirstOrDefault(da => da.DriverProfileId == request.DriverId && da.Status == "Active");
            if (activeAssignment == null)
            {
                _db.DriverAssignments.Add(new DriverAssignment
                {
                    TripId = trip.Id,
                    DriverProfileId = request.DriverId,
                    AssignedAtUtc = now,
                    Status = "Active"
                });
            }
        }

        // 5. Look up linked RestaurantDailyOrders
        var dayIdsToQuery = days.Select(d => d.Id)
            .Concat(existingStops.Where(s => s.SubscriptionDayId.HasValue).Select(s => s.SubscriptionDayId!.Value))
            .Distinct()
            .ToList();

        var dailyOrders = dayIdsToQuery.Count > 0
            ? await _db.RestaurantDailyOrders
                .Where(r => dayIdsToQuery.Contains(r.SubscriptionDayId))
                .ToDictionaryAsync(r => r.SubscriptionDayId, cancellationToken)
            : new Dictionary<Guid, RestaurantDailyOrder>();

        var assignedBoxIds = new List<Guid>();
        var currentStopSeq = (trip.Stops?.Count ?? 0) + 1;
        var targetDriverUserId = driver.UserId != Guid.Empty ? driver.UserId : driver.Id;

        // 6. Process existing TripStops
        foreach (var stop in existingStops)
        {
            stop.TripId = trip.Id;
            stop.SequenceNumber = currentStopSeq++;
            stop.Status = TripStopStatus.Pending;

            if (stop.SubscriptionDayId.HasValue)
            {
                var linkedDay = await _db.SubscriptionDays
                    .FirstOrDefaultAsync(d => d.Id == stop.SubscriptionDayId.Value, cancellationToken);
                if (linkedDay != null && (linkedDay.Status == SubscriptionDayStatus.ConfirmedByRestaurant || linkedDay.Status == SubscriptionDayStatus.Preparing))
                {
                    linkedDay.Status = SubscriptionDayStatus.OutForDelivery;
                }

                if (dailyOrders.TryGetValue(stop.SubscriptionDayId.Value, out var order))
                {
                    order.Status = OrderStatus.AssignedToDriver;
                    order.ModifiedAtUtc = now;
                }
            }

            var stopCode = stop.OrderCode ?? $"#BX-{Math.Abs(stop.Id.GetHashCode()) % 9000 + 1000}";
            _db.DriverNotifications.Add(new DriverNotification
            {
                UserId = targetDriverUserId,
                DriverProfileId = request.DriverId,
                Type = "box-assigned",
                TitleAr = "تم إسناد طلب جديد",
                TitleEn = "New Order Assigned",
                BodyAr = $"تم إسناد الطلب {stopCode} إليك للتوصيل",
                BodyEn = $"Order {stopCode} has been assigned to you for delivery",
                Section = "orders",
                SubjectId = stop.Id,
                DeepLink = $"/driver/orders/{stopCode}",
                IsRead = false,
                SentAtUtc = now
            });

            assignedBoxIds.Add(stop.Id);
        }

        // 7. Process SubscriptionDays as new TripStops
        foreach (var day in days)
        {
            var boxCode = $"#BX-{Math.Abs(day.Id.GetHashCode()) % 9000 + 1000}";
            var zoneName = !string.IsNullOrWhiteSpace(day.DeliveryAddressLine)
                ? (day.DeliveryAddressLine.StartsWith("منطقة") ? day.DeliveryAddressLine : $"منطقة {day.DeliveryAddressLine}")
                : "منطقة العاصمة";

            dailyOrders.TryGetValue(day.Id, out var dailyOrder);
            if (dailyOrder != null)
            {
                dailyOrder.Status = OrderStatus.AssignedToDriver;
                dailyOrder.ModifiedAtUtc = now;
            }

            var newStop = new TripStop
            {
                TripId = trip.Id,
                SubscriptionDayId = day.Id,
                DailyOrderId = dailyOrder?.Id,
                OrderCode = boxCode,
                CustomerMaskedId = $"CUST-{Math.Abs(day.SubscriptionId.GetHashCode()) % 9000 + 1000}",
                ZoneNameAr = zoneName,
                ZoneNameEn = zoneName,
                MealSummaryAr = $"{Math.Max(1, day.MealSelections?.Count ?? 1)} وجبات",
                MealSummaryEn = $"{Math.Max(1, day.MealSelections?.Count ?? 1)} meals",
                BoxCount = Math.Max(1, (day.MealSelections?.Count ?? 1) / 2),
                SlotLabel = "09:30-10:30 ص",
                SequenceNumber = currentStopSeq++,
                Status = TripStopStatus.Pending,
                CreatedAtUtc = now
            };
            _db.TripStops.Add(newStop);

            day.Status = SubscriptionDayStatus.OutForDelivery;

            _db.DriverNotifications.Add(new DriverNotification
            {
                UserId = targetDriverUserId,
                DriverProfileId = request.DriverId,
                Type = "box-assigned",
                TitleAr = "تم إسناد طلب جديد",
                TitleEn = "New Order Assigned",
                BodyAr = $"تم إسناد الطلب {boxCode} إليك للتوصيل",
                BodyEn = $"Order {boxCode} has been assigned to you for delivery",
                Section = "orders",
                SubjectId = day.Id,
                DeepLink = $"/driver/orders/{boxCode}",
                IsRead = false,
                SentAtUtc = now
            });

            assignedBoxIds.Add(day.Id);
        }

        // 8. Single batch push notification to assigned driver
        if (_driverPushNotifier != null && assignedBoxIds.Count > 0)
        {
            await _driverPushNotifier.NotifyTripAssignedAsync(
                targetDriverUserId,
                trip.Code,
                assignedBoxIds.Count,
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var message = _messages.GetMessage(
            MessageCodes.DispatcherOrders.BoxAssignedSuccess,
            $"تم إسناد {assignedBoxIds.Count} صناديق إلى السائق بنجاح");

        return Result.Success(new BulkAssignBoxesResponseDto(
            TripId: trip.Id,
            TripCode: trip.Code,
            AssignedCount: assignedBoxIds.Count,
            DriverId: request.DriverId,
            DriverName: driverName,
            AssignedBoxIds: assignedBoxIds,
            AssignedAtUtc: now,
            Message: message));
    }
}
