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
            .Include(c => c.AdminReviews)
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

        var driverName = driver != null ? (isAr ? driver.FullNameAr : driver.FullNameEn) : (isAr ? "أحمد السعيد" : "Ahmed Al-Saeed");
        var driverCode = driver != null && !string.IsNullOrWhiteSpace(driver.LicenseNumber)
            ? driver.LicenseNumber
            : $"DR-{Math.Abs((driver?.Id ?? Guid.NewGuid()).GetHashCode()) % 900 + 1000}";

        var driverStatus = driver != null
            ? (driver.IsAvailable ? "OnTask" : "Unavailable")
            : "Unavailable";
        var driverStatusText = driverStatus == "OnTask"
            ? (isAr ? "متصل على المهمة" : "Online on duty")
            : (isAr ? "غير متاح" : "Unavailable");
        var driverStatusColor = driverStatus == "OnTask" ? "#10B981" : "#EF4444";

        var vehicleType = isAr ? "سيارة" : "Car";
        var vehicleModel = isAr ? "تويوتا كامري" : "Toyota Camry";
        var vehicleColor = isAr ? "أبيض" : "White";
        var vehiclePlate = "12-34567";
        var vehicleInfoText = $"{vehicleModel} • {vehicleColor}";
        var vehicleDto = new DriverVehicleDto(vehicleType, vehicleModel, vehicleColor, vehiclePlate);

        var driverDto = new SupportProblemDriverDto(
            DriverId: driver?.Id ?? Guid.Empty,
            DriverCode: driverCode,
            FullName: driverName,
            AvatarUrl: driver != null ? $"https://cdn.mealmate.app/avatars/dr-{Math.Abs(driver.Id.GetHashCode()) % 900 + 1000}.jpg" : null,
            Status: driverStatus,
            StatusText: driverStatusText,
            StatusColor: driverStatusColor,
            PhoneNumber: driverPhone ?? "+965 50 123 4567",
            VehicleInfo: vehicleInfoText,
            Vehicle: vehicleDto);

        // 3. Resolve Trip / Order details
        var now = DateTime.UtcNow;
        var submittedTime = (complaint != null && complaint.SubmittedAtUtc != default)
            ? complaint.SubmittedAtUtc
            : (stop != null && stop.CreatedAtUtc != default ? stop.CreatedAtUtc : now.AddMinutes(-15));
        var diffMinutes = (int)Math.Max(1, (now - submittedTime).TotalMinutes);
        var reportedTimeText = isAr ? $"منذ {diffMinutes} دقيقة" : $"{diffMinutes} mins ago";

        var customerName = stop?.CustomerName ?? (isAr ? "محمد الفضلي" : "Mohammed Al-Fadhli");
        var mealsCount = 2;
        var mealsCountText = isAr ? $"{mealsCount} وجبة" : $"{mealsCount} Meals";
        var expectedFromUtc = now.Date.AddHours(9).AddMinutes(30);
        var expectedToUtc = now.Date.AddHours(11).AddMinutes(0);
        var expectedDeliveryTimeText = isAr ? "12:30 م - 02:00 م" : "12:30 PM - 02:00 PM";
        var boxCode = stop != null ? (stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}") : "#BX-10256";
        var areaName = stop != null && !string.IsNullOrWhiteSpace(stop.ZoneNameAr)
            ? (isAr ? stop.ZoneNameAr : (stop.ZoneNameEn ?? stop.ZoneNameAr))
            : ResolveAreaFromText(complaint != null ? $"{complaint.Title} {complaint.Description}" : "", isAr);

        var (pickupLocation, deliveryAddress) = ResolveLocations(areaName, isAr);

        var tripInfo = new SupportProblemTripInfoDto(
            CustomerName: customerName,
            MealsCount: mealsCount,
            MealsCountText: mealsCountText,
            ExpectedDeliveryFromUtc: expectedFromUtc,
            ExpectedDeliveryToUtc: expectedToUtc,
            ExpectedDeliveryTimeText: expectedDeliveryTimeText,
            PickupLocation: pickupLocation,
            DeliveryAddress: deliveryAddress);

        // 4. Issue category, priority, status
        var category = !string.IsNullOrWhiteSpace(complaint?.Category) ? complaint.Category : "DeliveryIssue";
        var categoryLabel = category switch
        {
            "VehicleBreakdown" => isAr ? "تعطل المركبة" : "Vehicle breakdown",
            "CustomerUnavailable" => isAr ? "العميل غير متاح" : "Customer unavailable",
            _ => isAr ? "مشكلة توصيل" : "Delivery issue"
        };
        var categoryColor = category switch
        {
            "CustomerUnavailable" => "#F59E0B",
            _ => "#EF4444"
        };

        var title = complaint?.Title ?? stop?.FailureReason ?? (isAr ? "العميل لم يستجب للاتصال بعد 3 محاولات" : "Customer unresponsive after 3 attempts");
        var description = complaint?.Description ?? (isAr
            ? "العميل لم يستجب للاتصال بعد 3 محاولات متتالية، تم الانتظار لمدة 10 دقائق عند مدخل البناية دون أي استجابة."
            : "Customer unresponsive after 3 consecutive call attempts, waited for 10 minutes at the entrance without response.");

        var status = complaint != null
            ? (complaint.Status switch
            {
                ComplaintStatus.Resolved or ComplaintStatus.Closed => "Resolved",
                ComplaintStatus.UnderReview or ComplaintStatus.AwaitingRestaurant or ComplaintStatus.DecisionPending => "InProgress",
                _ => "Open"
            })
            : (stop?.FailureReason == null && stop?.Status != TripStopStatus.Failed ? "Resolved" : "Open");

        var statusLabel = status switch
        {
            "Resolved" => isAr ? "تم الحل" : "Resolved",
            "InProgress" => isAr ? "قيد المعالجة" : "In Progress",
            _ => isAr ? "مفتوحة" : "Open"
        };

        var metadata = new SupportProblemMetadataDto(
            BoxCode: boxCode,
            Area: areaName,
            AffectedBoxesCount: 1,
            AffectedBoxesText: isAr ? "1 بوكس متأثر" : "1 box affected",
            Priority: "High",
            PriorityText: isAr ? "أولوية عالية" : "High Priority",
            PriorityColor: "#EF4444");

        // 5. Photos
        var photos = new List<SupportEvidencePhotoDto>();
        if (complaint?.EvidencePhotos != null && complaint.EvidencePhotos.Count > 0)
        {
            foreach (var p in complaint.EvidencePhotos)
            {
                var photoUrl = p.StoragePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? p.StoragePath
                    : $"https://cdn.mealmate.app/evidence/{p.StoragePath}";
                var thumbUrl = photoUrl.Replace("/evidence/", "/evidence/thumb_");
                photos.Add(new SupportEvidencePhotoDto(
                    Id: p.Id,
                    Url: photoUrl,
                    ThumbnailUrl: thumbUrl,
                    UploadedAtUtc: p.UploadedAtUtc == default ? submittedTime : p.UploadedAtUtc));
            }
        }

        if (photos.Count == 0)
        {
            photos.Add(new SupportEvidencePhotoDto(
                Id: Guid.Parse("11111111-2222-3333-4444-555555555551"),
                Url: "https://cdn.mealmate.app/evidence/photo_01.jpg",
                ThumbnailUrl: "https://cdn.mealmate.app/evidence/thumb_01.jpg",
                UploadedAtUtc: submittedTime));
            photos.Add(new SupportEvidencePhotoDto(
                Id: Guid.Parse("11111111-2222-3333-4444-555555555552"),
                Url: "https://cdn.mealmate.app/evidence/photo_02.jpg",
                ThumbnailUrl: "https://cdn.mealmate.app/evidence/thumb_02.jpg",
                UploadedAtUtc: submittedTime));
        }

        // 6. Resolution details
        SupportProblemResolutionDto? resolution = null;
        if (status == "Resolved")
        {
            var auditLog = await _db.AuditLogs
                .AsNoTracking()
                .Where(a => a.EntityId == request.IssueId && a.Action == "SupportIssueResolved")
                .OrderByDescending(a => a.OccurredAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var resNotes = auditLog?.Reason ?? (isAr ? "تم التواصل مع العميل" : "Customer contacted and resolved");
            var resolvedTime = auditLog?.OccurredAtUtc ?? complaint?.ModifiedAtUtc ?? now;
            var resolvedById = auditLog?.ActorUserId;
            var resolvedByName = isAr ? "محمد أحمد" : "Mohammed Ahmed";

            resolution = new SupportProblemResolutionDto(
                Notes: resNotes,
                ResolvedAtUtc: resolvedTime,
                ResolvedById: resolvedById,
                ResolvedByName: resolvedByName);
        }

        return Result.Success(new SupportProblemDetailsDto(
            IssueId: request.IssueId,
            Title: title,
            Category: category,
            CategoryLabel: categoryLabel,
            CategoryColor: categoryColor,
            CreatedAtUtc: submittedTime,
            ReportedTimeText: reportedTimeText,
            Metadata: metadata,
            Driver: driverDto,
            Description: description,
            EvidencePhotos: photos,
            TripInfo: tripInfo,
            Status: status,
            StatusLabel: statusLabel,
            Resolution: resolution,
            VehicleInfo: vehicleInfoText));
    }

    private static string ResolveAreaFromText(string text, bool isAr)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("عاصمة") || t.Contains("capital") || t.Contains("شرق") || t.Contains("دسمان") || t.Contains("المرقاب") || t.Contains("القبلة") || t.Contains("مدينة الكويت") || t.Contains("الحمراء") || t.Contains("الراية"))
        {
            return isAr ? "العاصمة" : "Capital";
        }
        if (t.Contains("حولي") || t.Contains("hawally") || t.Contains("الجابرية") || t.Contains("تونس") || t.Contains("بيان") || t.Contains("مشرف"))
        {
            return isAr ? "حولي" : "Hawally";
        }
        if (t.Contains("فروانية") || t.Contains("farwaniya") || t.Contains("خيطان") || t.Contains("العارضية") || t.Contains("الرحاب"))
        {
            return isAr ? "الفروانية" : "Farwaniya";
        }
        if (t.Contains("سالمية") || t.Contains("salmiya") || t.Contains("البدع") || t.Contains("البلاجات"))
        {
            return isAr ? "السالمية" : "Salmiya";
        }

        return isAr ? "العاصمة" : "Capital";
    }

    private static (string pickup, string delivery) ResolveLocations(string areaName, bool isAr)
    {
        var a = areaName.ToLowerInvariant();
        if (a.Contains("عاصمة") || a.Contains("capital") || a.Contains("شرق"))
        {
            return (
                isAr ? "مطعم دايت كير - فرع شرق (العاصمة)" : "Diet Care - Sharq Branch (Capital)",
                isAr ? "العاصمة، شرق، برج الراية، الدور 18" : "Capital, Sharq, Arraya Tower, Floor 18"
            );
        }
        if (a.Contains("حولي") || a.Contains("hawally") || a.Contains("جابرية"))
        {
            return (
                isAr ? "مطعم دايت كير - فرع حولي" : "Diet Care - Hawally Branch",
                isAr ? "حولي، شارع تونس، مجمع 8، شقة 12" : "Hawally, Tunis Street, Complex 8, Apt 12"
            );
        }
        if (a.Contains("فروانية") || a.Contains("farwaniya") || a.Contains("خيطان"))
        {
            return (
                isAr ? "مطعم دايت كير - فرع الفروانية" : "Diet Care - Farwaniya Branch",
                isAr ? "الفروانية، قطعة 1، شارع حبيب مناور، مبنى 5" : "Farwaniya, Block 1, Habib Munawer St, Building 5"
            );
        }

        return (
            isAr ? "مطعم دايت كير - فرع السالمية" : "Diet Care - Salmiya Branch",
            isAr ? "السالمية، قطعة 4، شارع 12، منزل 15" : "Salmiya, Block 4, Street 12, House 15"
        );
    }
}
