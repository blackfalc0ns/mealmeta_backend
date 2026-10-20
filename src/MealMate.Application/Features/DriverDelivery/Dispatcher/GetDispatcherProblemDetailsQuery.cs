using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
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
            .Include(c => c.Classifications)
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
                    .FirstOrDefaultAsync(s => s.Id == boxId || s.SubscriptionDayId == boxId || s.DailyOrderId == boxId, cancellationToken);
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
        string? registrationVehicleType = null;
        string? registrationVehicleModel = null;
        string? registrationVehicleColor = null;
        string? registrationVehiclePlate = null;
        if (driver != null)
        {
            var registration = await _db.DriverRegistrations
                .AsNoTracking()
                .Where(r => r.ResultingDriverProfileId == driver.Id || (r.SubmittedByUserId.HasValue && r.SubmittedByUserId.Value == driver.UserId))
                .Select(r => new
                {
                    r.Phone,
                    r.VehicleType,
                    r.VehicleModel,
                    r.VehicleColor,
                    r.VehiclePlate
                })
                .FirstOrDefaultAsync(cancellationToken);
            driverPhone = NonBlank(registration?.Phone);
            registrationVehicleType = registration?.VehicleType;
            registrationVehicleModel = registration?.VehicleModel;
            registrationVehicleColor = registration?.VehicleColor;
            registrationVehiclePlate = registration?.VehiclePlate;
        }

        var driverName = driver == null ? null : NonBlank(isAr ? driver.FullNameAr : driver.FullNameEn);
        var driverCode = NonBlank(driver?.LicenseNumber);

        var driverStatus = driver != null
            ? (driver.IsAvailable ? "OnTask" : "Unavailable")
            : "Unavailable";
        var driverStatusText = driverStatus == "OnTask"
            ? (isAr ? "متصل على المهمة" : "Online on duty")
            : (isAr ? "غير متاح" : "Unavailable");
        var driverStatusColor = driverStatus == "OnTask" ? "#10B981" : "#EF4444";

        var vehicleType = NonBlank(driver?.VehicleType) ?? NonBlank(registrationVehicleType);
        var vehicleModel = NonBlank(driver?.VehicleModel) ?? NonBlank(registrationVehicleModel);
        var vehicleColor = NonBlank(driver?.VehicleColor) ?? NonBlank(registrationVehicleColor);
        var vehiclePlate = NonBlank(driver?.VehiclePlate) ?? NonBlank(registrationVehiclePlate);
        var vehicleIdentity = vehicleModel ?? vehicleType;
        var vehicleInfoText = BuildVehicleInfo(vehicleIdentity, vehicleColor);
        var vehicleDto = vehicleType is null && vehicleModel is null && vehicleColor is null && vehiclePlate is null
            ? null
            : new DriverVehicleDto(vehicleType, vehicleModel, vehicleColor, vehiclePlate);

        var driverDto = new SupportProblemDriverDto(
            DriverId: driver?.Id,
            DriverCode: driverCode,
            FullName: driverName,
            AvatarUrl: CatalogImageStorage.ToContentUrl(driver?.ProfileImageStorageKey),
            Status: driverStatus,
            StatusText: driverStatusText,
            StatusColor: driverStatusColor,
            PhoneNumber: driverPhone,
            VehicleInfo: vehicleInfoText,
            Vehicle: vehicleDto);

        // 3. Resolve Trip / Order details
        var now = DateTime.UtcNow;
        var submittedTime = complaint != null
            ? complaint.SubmittedAtUtc != default ? complaint.SubmittedAtUtc : complaint.CreatedAtUtc
            : stop?.CreatedAtUtc ?? default;
        var hasSubmittedTime = submittedTime != default;
        var diffMinutes = hasSubmittedTime ? (int)Math.Max(1, (now - submittedTime).TotalMinutes) : (int?)null;
        var reportedTimeText = diffMinutes.HasValue
            ? isAr ? $"منذ {diffMinutes.Value} دقيقة" : $"{diffMinutes.Value} mins ago"
            : _messages.GetMessage("dispatcher_support.data_unavailable", isAr ? "غير متوفر" : "Unavailable");

        var customerName = NonBlank(stop?.CustomerName);
        var mealsSummary = NonBlank(isAr ? stop?.MealSummaryAr : stop?.MealSummaryEn);
        var unavailableText = _messages.GetMessage("dispatcher_support.data_unavailable", isAr ? "غير متوفر" : "Unavailable");
        var expectedDeliveryTimeText = NonBlank(stop?.SlotLabel);
        var boxCode = FormatOrderCode(stop?.OrderCode);
        var areaName = NonBlank(isAr ? stop?.ZoneNameAr : stop?.ZoneNameEn)
            ?? NonBlank(isAr ? stop?.ZoneNameEn : stop?.ZoneNameAr);
        var deliveryAddress = NonBlank(stop?.FullAddress);

        var tripInfo = new SupportProblemTripInfoDto(
            CustomerName: customerName,
            MealsCount: null,
            MealsCountText: unavailableText,
            ExpectedDeliveryFromUtc: null,
            ExpectedDeliveryToUtc: null,
            ExpectedDeliveryTimeText: expectedDeliveryTimeText ?? unavailableText,
            PickupLocation: null,
            DeliveryAddress: deliveryAddress,
            MealSummary: mealsSummary);

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

        var title = NonBlank(complaint?.Title) ?? NonBlank(stop?.FailureReason)
            ?? (isAr ? "مشكلة دعم" : "Support issue");
        var description = NonBlank(complaint?.Description)
            ?? _messages.GetMessage("dispatcher_support.data_unavailable", isAr ? "غير متوفر" : "Unavailable");

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
            AffectedBoxesCount: stop?.BoxCount,
            AffectedBoxesText: stop != null
                ? isAr ? $"{stop.BoxCount} بوكس متأثر" : $"{stop.BoxCount} box affected"
                : unavailableText,
            Priority: GetComplaintSeverity(complaint),
            PriorityText: GetPriorityLabel(GetComplaintSeverity(complaint), unavailableText, isAr),
            PriorityColor: GetPriorityColor(GetComplaintSeverity(complaint)));

        // 5. Photos
        var photos = new List<SupportEvidencePhotoDto>();
        if (complaint?.EvidencePhotos != null && complaint.EvidencePhotos.Count > 0)
        {
            foreach (var p in complaint.EvidencePhotos)
            {
                var photoUrl = CatalogImageStorage.ToContentUrl(p.StoragePath);
                photos.Add(new SupportEvidencePhotoDto(
                    Id: p.Id,
                    Url: photoUrl ?? string.Empty,
                    ThumbnailUrl: null,
                    UploadedAtUtc: p.UploadedAtUtc == default ? submittedTime : p.UploadedAtUtc));
            }
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

            var resNotes = NonBlank(auditLog?.Reason) ?? unavailableText;
            var resolvedTime = auditLog?.OccurredAtUtc ?? complaint?.ModifiedAtUtc ?? stop?.ModifiedAtUtc ?? default;
            var resolvedById = auditLog?.ActorUserId;
            string? resolvedByName = null;

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

    private static string? NonBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? BuildVehicleInfo(string? identity, string? color)
    {
        var parts = new[] { NonBlank(identity), NonBlank(color) }
            .Where(part => part is not null);
        var value = string.Join(" • ", parts);
        return value.Length == 0 ? null : value;
    }

    private static string? FormatOrderCode(string? orderCode)
    {
        var value = NonBlank(orderCode);
        if (value is null || value.StartsWith("#", StringComparison.Ordinal))
        {
            return value;
        }

        return $"#{value}";
    }

    private static string? GetComplaintSeverity(Complaint? complaint)
        => complaint?.Classifications
            .OrderByDescending(classification => classification.ClassifiedAtUtc)
            .Select(classification => NonBlank(classification.Severity))
            .FirstOrDefault(severity => severity is not null);

    private static string GetPriorityLabel(string? severity, string unavailableText, bool isAr)
    {
        if (severity is null)
        {
            return unavailableText;
        }

        return severity.ToLowerInvariant() switch
        {
            "urgent" => isAr ? "عاجلة" : "Urgent",
            "high" => isAr ? "عالية" : "High",
            "medium" => isAr ? "متوسطة" : "Medium",
            "low" => isAr ? "منخفضة" : "Low",
            _ => severity
        };
    }

    private static string? GetPriorityColor(string? severity)
        => severity?.ToLowerInvariant() switch
        {
            "urgent" or "high" => "#EF4444",
            "medium" => "#F59E0B",
            "low" => "#6B7280",
            _ => null
        };
}
