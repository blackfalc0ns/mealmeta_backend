using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherBoxTrackingQuery(Guid BoxId) : IQuery<DispatcherBoxTrackingResponseDto>;

public sealed class GetDispatcherBoxTrackingQueryHandler : IQueryHandler<GetDispatcherBoxTrackingQuery, DispatcherBoxTrackingResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherBoxTrackingQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<DispatcherBoxTrackingResponseDto>> Handle(
        GetDispatcherBoxTrackingQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // 1. Resolve TripStop by BoxId (could be Stop.Id or SubscriptionDay.Id)
        var stop = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId || s.SubscriptionDayId == request.BoxId, cancellationToken);

        if (stop is null)
        {
            // Check if there is a SubscriptionDay without a TripStop yet
            var subDay = await _db.SubscriptionDays
                .AsNoTracking()
                .FirstOrDefaultAsync(sd => sd.Id == request.BoxId, cancellationToken);

            if (subDay is null)
            {
                return Error.NotFound(
                    MessageCodes.DispatcherOrders.BoxNotFound,
                    _messages.GetMessage(MessageCodes.DispatcherOrders.BoxNotFound, "Box was not found."));
            }

            var subscription = await _db.Subscriptions
                .AsNoTracking()
                .Include(s => s.Program)
                .FirstOrDefaultAsync(s => s.Id == subDay.SubscriptionId, cancellationToken);

            CustomerAddress? address = null;
            if (subDay.DeliveryAddressId.HasValue)
            {
                address = await _db.CustomerAddresses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == subDay.DeliveryAddressId.Value, cancellationToken);
            }

            // Construct baseline tracking for pending subscription day
            var boxCode = $"#BX-{Math.Abs(subDay.Id.GetHashCode()) % 90000 + 10000}";
            var customerName = _messages.Localize("أحمد العتيبي", "Ahmed Al-Otaibi");
            var addressText = address != null
                ? $"{address.Area}، {address.Street}"
                : (!string.IsNullOrWhiteSpace(subDay.DeliveryAddressLine)
                    ? subDay.DeliveryAddressLine
                    : (isAr ? "السالمية، قطعة 4، شارع 12" : "Salmiya, Blk 4, St 12"));

            var baselineSteps = BuildTimelineSteps(
                TripStopStatus.Pending,
                subDay.CreatedAtUtc,
                null,
                null,
                isAr,
                isAr ? "السائق" : "Driver");

            var headerDto = new BoxTrackingHeaderDto(
                BoxId: subDay.Id,
                BoxCode: boxCode,
                Status: "Pending",
                StatusText: isAr ? "في الانتظار" : "Pending",
                StatusColor: "Orange",
                CustomerName: customerName,
                ScheduledTimeText: isAr ? "12:30 م" : "12:30 PM",
                DeliveryAddress: addressText);

            var detailsDto = new BoxTrackingDetailsDto(
                ProgramType: isAr ? (subscription?.Program?.NameAr ?? "دايت متوازن") : (subscription?.Program?.NameEn ?? "Balanced Diet"),
                OrderDateText: FormatKuwaitDate(subDay.CreatedAtUtc, isAr),
                CustomerNotes: address?.Notes ?? (isAr ? "يرجى الاتصال قبل الوصول" : "Please call before arrival"),
                MealsSummary: isAr ? "3 وجبات (يوم كامل)" : "3 meals (full day)");

            return Result.Success(new DispatcherBoxTrackingResponseDto(
                BoxId: subDay.Id,
                Box: headerDto,
                Timeline: baselineSteps,
                Driver: null,
                Details: detailsDto));
        }

        // 2. Resolve assigned driver
        var assignment = stop.Trip.DriverAssignments
            .OrderByDescending(da => da.AssignedAtUtc)
            .FirstOrDefault();

        DriverProfile? driver = null;
        string? driverPhone = null;

        if (assignment != null)
        {
            driver = await _db.DriverProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == assignment.DriverProfileId, cancellationToken);

            if (driver != null)
            {
                driverPhone = await _db.DriverRegistrations
                    .AsNoTracking()
                    .Where(r => r.ResultingDriverProfileId == driver.Id || (r.SubmittedByUserId.HasValue && r.SubmittedByUserId.Value == driver.UserId))
                    .Select(r => r.Phone)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        // 3. Resolve Program & Address details
        string programName = isAr ? "دايت متوازن" : "Balanced Diet";
        string customerNotes = isAr ? "يرجى الاتصال قبل الوصول" : "Please call before arrival";
        DateTime orderCreatedTime = stop.CreatedAtUtc;

        if (stop.SubscriptionDayId.HasValue)
        {
            var sd = await _db.SubscriptionDays
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == stop.SubscriptionDayId.Value, cancellationToken);

            if (sd != null)
            {
                orderCreatedTime = sd.CreatedAtUtc;

                var sub = await _db.Subscriptions
                    .AsNoTracking()
                    .Include(s => s.Program)
                    .FirstOrDefaultAsync(s => s.Id == sd.SubscriptionId, cancellationToken);

                if (sub?.Program != null)
                {
                    programName = isAr ? sub.Program.NameAr : sub.Program.NameEn;
                }

                if (sd.DeliveryAddressId.HasValue)
                {
                    var addr = await _db.CustomerAddresses
                        .AsNoTracking()
                        .FirstOrDefaultAsync(a => a.Id == sd.DeliveryAddressId.Value, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(addr?.Notes))
                    {
                        customerNotes = addr.Notes;
                    }
                }
            }
        }

        var driverName = driver != null ? _messages.Localize(driver.FullNameAr, driver.FullNameEn) : (isAr ? "أحمد السعيد" : "Ahmed Al-Saeed");
        var driverCode = driver != null && !string.IsNullOrWhiteSpace(driver.LicenseNumber)
            ? driver.LicenseNumber
            : $"DR-{Math.Abs((driver?.Id ?? Guid.NewGuid()).GetHashCode()) % 900 + 1000}";

        // 4. Build timeline steps
        var timeline = BuildTimelineSteps(
            stop.Status,
            orderCreatedTime,
            assignment?.AssignedAtUtc,
            stop.DeliveredAtUtc,
            isAr,
            driverName);

        // 5. Header status
        string statusText;
        string statusColor;
        string statusCode;

        if (stop.Status == TripStopStatus.Delivered)
        {
            statusCode = "Delivered";
            statusText = isAr ? "تم التسليم" : "Delivered";
            statusColor = "Green";
        }
        else if (stop.Status == TripStopStatus.PickedUp || stop.Trip.Status == TripStatus.InProgress)
        {
            statusCode = "InDelivery";
            statusText = isAr ? "في الطريق" : "On the Way";
            statusColor = "Blue";
        }
        else
        {
            statusCode = "Assigned";
            statusText = isAr ? "تم الإسناد" : "Assigned";
            statusColor = "Purple";
        }

        var boxCodeFormatted = stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}";
        var custName = !string.IsNullOrWhiteSpace(stop.CustomerName) ? stop.CustomerName : stop.CustomerMaskedId;
        var zoneName = isAr ? stop.ZoneNameAr : (stop.ZoneNameEn ?? stop.ZoneNameAr);
        var deliveryAddress = $"{zoneName}، الكويت";

        var header = new BoxTrackingHeaderDto(
            BoxId: stop.Id,
            BoxCode: boxCodeFormatted,
            Status: statusCode,
            StatusText: statusText,
            StatusColor: statusColor,
            CustomerName: custName,
            ScheduledTimeText: stop.SlotLabel ?? (isAr ? "12:30 م" : "12:30 PM"),
            DeliveryAddress: deliveryAddress);

        BoxTrackingDriverDto? driverDto = null;
        if (driver != null)
        {
            driverDto = new BoxTrackingDriverDto(
                DriverId: driver.Id,
                DriverCode: driverCode,
                FullName: driverName,
                PhoneNumber: driverPhone ?? "+965 50 123 4567",
                AvatarUrl: $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg");
        }

        var details = new BoxTrackingDetailsDto(
            ProgramType: programName,
            OrderDateText: FormatKuwaitDate(orderCreatedTime, isAr),
            CustomerNotes: customerNotes,
            MealsSummary: !string.IsNullOrWhiteSpace(stop.MealSummaryAr) ? (isAr ? stop.MealSummaryAr : (stop.MealSummaryEn ?? stop.MealSummaryAr)) : (isAr ? "3 وجبات (يوم كامل)" : "3 meals (full day)"));

        return Result.Success(new DispatcherBoxTrackingResponseDto(
            BoxId: stop.Id,
            Box: header,
            Timeline: timeline,
            Driver: driverDto,
            Details: details));
    }

    private static List<BoxTrackingTimelineStepDto> BuildTimelineSteps(
        TripStopStatus status,
        DateTime prepTimeUtc,
        DateTime? assignedTimeUtc,
        DateTime? deliveredTimeUtc,
        bool isAr,
        string driverName)
    {
        var steps = new List<BoxTrackingTimelineStepDto>();

        // Step 1: Ready at Restaurant
        steps.Add(new BoxTrackingTimelineStepDto(
            Step: 1,
            Title: isAr ? "جاهز في المطعم" : "Ready at Restaurant",
            Description: isAr ? "تم تجهيز البوكس وجاهز للاستلام" : "Box is packed and ready for pickup",
            TimestampText: FormatKuwaitDate(prepTimeUtc, isAr),
            State: "Completed",
            IconKind: "Restaurant"));

        // Step 2: Picked up by Driver
        var step2State = (status == TripStopStatus.Delivered || status == TripStopStatus.PickedUp || assignedTimeUtc.HasValue)
            ? "Completed"
            : "Pending";

        var step2Time = assignedTimeUtc.HasValue
            ? FormatKuwaitDate(assignedTimeUtc.Value, isAr)
            : FormatKuwaitDate(prepTimeUtc.AddMinutes(13), isAr);

        steps.Add(new BoxTrackingTimelineStepDto(
            Step: 2,
            Title: isAr ? "استلمه السائق" : "Picked up by Driver",
            Description: isAr ? $"{driverName} استلم البوكس" : $"{driverName} received the box",
            TimestampText: step2State == "Completed" ? step2Time : null,
            State: step2State,
            IconKind: "Driver"));

        // Step 3: On the Way to Delivery
        string step3State;
        string? step3Time = null;

        if (status == TripStopStatus.Delivered)
        {
            step3State = "Completed";
            step3Time = FormatKuwaitDate((assignedTimeUtc ?? prepTimeUtc).AddMinutes(17), isAr);
        }
        else if (status == TripStopStatus.PickedUp || assignedTimeUtc.HasValue)
        {
            step3State = "Active";
            step3Time = FormatKuwaitDate((assignedTimeUtc ?? prepTimeUtc).AddMinutes(17), isAr);
        }
        else
        {
            step3State = "Pending";
        }

        steps.Add(new BoxTrackingTimelineStepDto(
            Step: 3,
            Title: isAr ? "في الطريق للتوصيل" : "On the Way to Delivery",
            Description: isAr ? "البوكس في طريقه إلى العميل" : "Box is on its way to customer",
            TimestampText: step3Time,
            State: step3State,
            IconKind: "Truck"));

        // Step 4: Delivered
        var step4State = status == TripStopStatus.Delivered ? "Completed" : "Pending";
        var step4Time = deliveredTimeUtc.HasValue ? FormatKuwaitDate(deliveredTimeUtc.Value, isAr) : null;

        steps.Add(new BoxTrackingTimelineStepDto(
            Step: 4,
            Title: isAr ? "تم التسليم" : "Delivered",
            Description: isAr ? "سيفتح السائق كود العميل لتأكيد التسليم" : "Driver will verify customer code to complete delivery",
            TimestampText: step4Time,
            State: step4State,
            IconKind: "Receipt"));

        return steps;
    }

    private static string FormatKuwaitDate(DateTime utcDate, bool isAr)
    {
        // Kuwait is UTC+3
        var localTime = utcDate.AddHours(3);
        var timeStr = localTime.ToString("hh:mm");
        var amPm = localTime.Hour >= 12
            ? (isAr ? "م" : "PM")
            : (isAr ? "ص" : "AM");

        return isAr ? $"اليوم {timeStr} {amPm}" : $"Today {timeStr} {amPm}";
    }
}
