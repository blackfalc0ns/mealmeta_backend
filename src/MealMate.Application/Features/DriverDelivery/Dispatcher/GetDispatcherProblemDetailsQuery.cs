using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherProblemDetailsQuery(Guid IssueId) : IQuery<SupportProblemDetailsDto>;

public sealed class GetDispatcherProblemDetailsQueryHandler : IQueryHandler<GetDispatcherProblemDetailsQuery, SupportProblemDetailsDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherProblemDetailsQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<SupportProblemDetailsDto>> Handle(
        GetDispatcherProblemDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // 1. Try finding in Complaints
        var complaint = await _db.Complaints
            .AsNoTracking()
            .Include(c => c.EvidencePhotos)
            .FirstOrDefaultAsync(c => c.Id == request.IssueId, cancellationToken);

        TripStop? stop = null;

        if (complaint is null)
        {
            // Try finding in TripStops
            stop = await _db.TripStops
                .AsNoTracking()
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .FirstOrDefaultAsync(s => s.Id == request.IssueId, cancellationToken);

            if (stop is null)
            {
                return Error.NotFound(
                    MessageCodes.DispatcherSupport.IssueNotFound,
                    _messages.GetMessage(MessageCodes.DispatcherSupport.IssueNotFound, "Support issue was not found."));
            }
        }
        else
        {
            if (complaint.OrderId.HasValue || complaint.SubscriptionDayId.HasValue)
            {
                var boxId = complaint.OrderId ?? complaint.SubscriptionDayId;
                stop = await _db.TripStops
                    .AsNoTracking()
                    .Include(s => s.Trip)
                        .ThenInclude(t => t.DriverAssignments)
                    .FirstOrDefaultAsync(s => s.Id == boxId || s.SubscriptionDayId == boxId, cancellationToken);
            }
        }

        // 2. Resolve Driver
        Guid? driverId = complaint?.DriverProfileId;
        if (!driverId.HasValue && stop?.Trip?.DriverAssignments != null)
        {
            driverId = stop.Trip.DriverAssignments.OrderByDescending(da => da.AssignedAtUtc).FirstOrDefault()?.DriverProfileId;
        }

        var driver = driverId.HasValue
            ? await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId.Value, cancellationToken)
            : null;

        string? driverPhone = null;
        if (driver != null)
        {
            driverPhone = await _db.DriverRegistrations
                .AsNoTracking()
                .Where(r => r.ResultingDriverProfileId == driver.Id || (r.SubmittedByUserId.HasValue && r.SubmittedByUserId.Value == driver.UserId))
                .Select(r => r.Phone)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var driverName = driver != null ? (isAr ? driver.FullNameAr : driver.FullNameEn) : (isAr ? "أحمد السيد" : "Ahmed Al-Sayed");
        var driverCode = driver != null && !string.IsNullOrWhiteSpace(driver.LicenseNumber)
            ? driver.LicenseNumber
            : $"DR-{Math.Abs((driver?.Id ?? Guid.NewGuid()).GetHashCode()) % 900 + 1000}";

        var driverStatusText = driver != null && driver.IsAvailable
            ? (isAr ? "متصل على المهمة" : "Online on duty")
            : (isAr ? "غير متاح" : "Unavailable");
        var driverStatusColor = driver != null && driver.IsAvailable ? "Green" : "Red";

        // 3. Resolve Trip / Order details
        var customerName = stop?.CustomerName ?? (isAr ? "محمد الشمري" : "Mohammed Al-Shammari");
        var mealsCountText = isAr ? "3 وجبات" : "3 meals";
        var expectedTimeText = stop?.SlotLabel ?? (isAr ? "12:15 م" : "12:15 PM");
        var pickupLocation = isAr ? "مطعم MealMate - السالمية" : "MealMate Restaurant - Salmiya";
        var deliveryAddress = stop != null
            ? $"{stop.ZoneNameAr}، قطعة 5، منزل 12"
            : (isAr ? "شارع الخليج العربي، قطعة 5 ، منزل 12" : "Arabian Gulf St, Block 5, House 12");

        var boxCode = stop != null ? (stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}") : "#BX-1258";
        var areaName = stop?.ZoneNameAr ?? (isAr ? "السالمية" : "Salmiya");

        var title = complaint?.Title ?? stop?.FailureReason ?? (isAr ? "تعطل الدراجة أثناء التوصيل" : "Bike breakdown during delivery");
        var description = complaint?.Description ?? (isAr
            ? "أبلغ السائق عن عطل مفاجئ في الدراجة أثناء طريقه في تسليم الطلب ولم يمكنه استكمال المهمة الحالية حتى وصول دعم أو سائق آخر"
            : "Driver reported sudden bike breakdown on the way to delivery and is unable to continue until replacement arrives.");

        var submittedTime = complaint?.SubmittedAtUtc ?? stop?.ModifiedAtUtc ?? stop?.CreatedAtUtc ?? DateTime.UtcNow.AddMinutes(-12);
        var diffMinutes = (int)Math.Max(1, (DateTime.UtcNow - submittedTime).TotalMinutes);
        var reportedTimeText = isAr ? $"تم الإبلاغ منذ {diffMinutes} دقيقة" : $"Reported {diffMinutes} mins ago";

        // Photos
        var photos = complaint?.EvidencePhotos?.Select(p => p.StoragePath).ToList() ?? new List<string>();
        if (photos.Count == 0)
        {
            photos.Add("https://images.unsplash.com/photo-1558981403-c5f9899a28bc?auto=format&fit=crop&w=400&q=80");
            photos.Add("https://images.unsplash.com/photo-1568772585407-9361f9bf3a87?auto=format&fit=crop&w=400&q=80");
        }

        var metadata = new SupportProblemMetadataDto(
            BoxCode: boxCode,
            Area: areaName,
            AffectedBoxesCount: 3,
            AffectedBoxesText: isAr ? "3 بوكسات" : "3 boxes",
            Priority: "High",
            PriorityText: isAr ? "عالية" : "High",
            PriorityColor: "Red");

        var driverDto = new SupportProblemDriverDto(
            DriverId: driver?.Id ?? Guid.Empty,
            DriverCode: driverCode,
            FullName: driverName,
            AvatarUrl: driver != null ? $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg" : null,
            StatusText: driverStatusText,
            StatusColor: driverStatusColor,
            PhoneNumber: driverPhone ?? "+965 50 123 4567");

        var tripInfo = new SupportProblemTripInfoDto(
            CustomerName: customerName,
            MealsCountText: mealsCountText,
            ExpectedDeliveryTimeText: expectedTimeText,
            PickupLocation: pickupLocation,
            DeliveryAddress: deliveryAddress);

        return Result.Success(new SupportProblemDetailsDto(
            IssueId: request.IssueId,
            Title: title,
            ReportedTimeText: reportedTimeText,
            Metadata: metadata,
            Driver: driverDto,
            Description: description,
            EvidencePhotos: photos,
            TripInfo: tripInfo,
            Status: complaint != null ? complaint.Status.ToString() : "Open"));
    }
}
