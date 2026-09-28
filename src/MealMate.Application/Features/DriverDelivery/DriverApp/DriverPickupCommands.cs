using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record ArriveAtKitchenCommand(
    Guid? DriverId = null,
    double? Latitude = null,
    double? Longitude = null) : ICommand<ArriveAtKitchenResponse>;

public sealed record LegacyValidatePickupBarcodeCommand(
    string BarcodeValue,
    Guid? DriverId = null,
    bool IsManualEntry = false) : ICommand<ValidatePickupBarcodeResponse>;

public sealed record StartTripDeliveryCommand(
    Guid? DriverId = null,
    double? Latitude = null,
    double? Longitude = null,
    bool ForceStartWithUnscannedBoxes = false,
    string? UnscannedReason = null) : ICommand<StartTripDeliveryResponse>;

public sealed class DriverPickupCommandHandlers
    : ICommandHandler<ArriveAtKitchenCommand, ArriveAtKitchenResponse>,
      ICommandHandler<LegacyValidatePickupBarcodeCommand, ValidatePickupBarcodeResponse>,
      ICommandHandler<StartTripDeliveryCommand, StartTripDeliveryResponse>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public DriverPickupCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<ArriveAtKitchenResponse>> Handle(
        ArriveAtKitchenCommand request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var trip = await (
            from da in _db.DriverAssignments
            join t in _db.Trips on da.TripId equals t.Id
            where da.DriverProfileId == driver.Id && da.Status == "Active"
            orderby t.CreatedAtUtc descending
            select t
        ).FirstOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return Error.NotFound("Trip.NotFound", _messages.IsArabic()
                ? "لا توجد رحلة أو صناديق مسندة إليك حالياً من المطبخ."
                : "No trip or boxes are currently assigned to you from the kitchen.");
        }

        var now = DateTime.UtcNow;
        trip.Notes = string.IsNullOrWhiteSpace(trip.Notes)
            ? $"[DriverArrivedAtKitchen] at {now:yyyy-MM-dd HH:mm:ss} UTC"
            : $"{trip.Notes} | [DriverArrivedAtKitchen] at {now:yyyy-MM-dd HH:mm:ss} UTC";

        await _db.SaveChangesAsync(cancellationToken);

        var msg = _messages.IsArabic()
            ? "تم تأكيد وصولك إلى المطبخ بنجاح. يمكنك الآن بدء مسح الصناديق."
            : "Arrival at kitchen confirmed successfully. You can now proceed to scan boxes.";

        return Result.Success(new ArriveAtKitchenResponse(
            TripId: trip.Id,
            Message: msg,
            ArrivedAtUtc: now
        ));
    }

    public async Task<Result<ValidatePickupBarcodeResponse>> Handle(
        LegacyValidatePickupBarcodeCommand request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();

        if (string.IsNullOrWhiteSpace(request.BarcodeValue))
        {
            return Error.Validation("Orders.BarcodeRequired", isAr
                ? "رمز الباركود أو QR مطلوب."
                : "Barcode or QR code value is required.");
        }

        var cleanInput = request.BarcodeValue.Trim().TrimStart('#');

        // Find active trip assigned to this driver
        var myTrip = await (
            from da in _db.DriverAssignments
            join t in _db.Trips on da.TripId equals t.Id
            where da.DriverProfileId == driver.Id && da.Status == "Active"
            orderby t.CreatedAtUtc descending
            select t
        ).FirstOrDefaultAsync(cancellationToken);

        // Find stop across the system matching this barcode / order code
        var matchedStop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s =>
                s.OrderCode != null &&
                (s.OrderCode.Equals(cleanInput) ||
                 s.OrderCode.EndsWith(cleanInput) ||
                 cleanInput.EndsWith(s.OrderCode)),
                cancellationToken);

        // If not matched by order code directly, match via kitchen MealBarcodes or SubscriptionDayId
        if (matchedStop is null)
        {
            var matchedSubscriptionDayId = await _db.MealBarcodes
                .Where(mb => mb.BarcodeValue == cleanInput || cleanInput.EndsWith(mb.BarcodeValue) || mb.BarcodeValue.EndsWith(cleanInput))
                .Select(mb => (Guid?)mb.SubscriptionDayId)
                .FirstOrDefaultAsync(cancellationToken);

            if (matchedSubscriptionDayId.HasValue && matchedSubscriptionDayId.Value != Guid.Empty)
            {
                matchedStop = await _db.TripStops
                    .Include(s => s.Trip)
                    .FirstOrDefaultAsync(s => s.SubscriptionDayId == matchedSubscriptionDayId.Value, cancellationToken);
            }
            else if (cleanInput.StartsWith("MM-", StringComparison.OrdinalIgnoreCase) || cleanInput.StartsWith("INV-", StringComparison.OrdinalIgnoreCase))
            {
                var parts = cleanInput.Split('-');
                if (parts.Length >= 3 && Guid.TryParse(parts[2], out var parsedGuid))
                {
                    matchedStop = await _db.TripStops
                        .Include(s => s.Trip)
                        .FirstOrDefaultAsync(s => s.SubscriptionDayId == parsedGuid || s.DailyOrderId == parsedGuid, cancellationToken);
                }
            }
            else if (Guid.TryParse(cleanInput, out var rawGuid))
            {
                matchedStop = await _db.TripStops
                    .Include(s => s.Trip)
                    .FirstOrDefaultAsync(s => s.Id == rawGuid || s.SubscriptionDayId == rawGuid || s.DailyOrderId == rawGuid, cancellationToken);
            }
        }

        if (matchedStop is null)
        {
            // Screen 06.12: رمز QR غير صالح
            return Error.Validation("Orders.InvalidBarcode", isAr
                ? "رمز الباركود غير صالح أو غير مسجل في النظام. يرجى التأكد والمحاولة مرة أخرى."
                : "Invalid barcode or QR code. Please verify and try again.");
        }

        // Check if stop belongs to this driver's current trip
        var belongsToMe = myTrip is not null && matchedStop.TripId == myTrip.Id;

        if (!belongsToMe)
        {
            // Screen 06.13: هذا الرمز لا يخصك!
            var otherAssignment = await (
                from da in _db.DriverAssignments
                join dp in _db.DriverProfiles on da.DriverProfileId equals dp.Id
                where da.TripId == matchedStop.TripId && da.Status == "Active"
                select new { dp.Id, Name = isAr ? dp.FullNameAr : dp.FullNameEn }
            ).FirstOrDefaultAsync(cancellationToken);

            var assignedName = otherAssignment?.Name ?? (isAr ? "سائق آخر" : "Another Driver");

            return Error.Validation("Orders.BoxBelongsToAnotherDriver", isAr
                ? $"هذا الرمز لا يخصك! الصندوق {matchedStop.OrderCode} مسند إلى {assignedName}. يرجى مسح صندوق تابع لرحلتك."
                : $"This box does not belong to you! Box {matchedStop.OrderCode} is assigned to {assignedName}.");
        }

        // Check if already scanned
        var isAlreadyScanned = matchedStop.Status == TripStopStatus.PickedUp || matchedStop.Status == TripStopStatus.Delivered;
        var scanStatus = isAlreadyScanned ? "Scanned" : "PendingScan";

        var zoneName = isAr ? matchedStop.ZoneNameAr : matchedStop.ZoneNameEn;
        var mealSummary = isAr ? matchedStop.MealSummaryAr : matchedStop.MealSummaryEn;

        var message = isAlreadyScanned
            ? (isAr ? "تم مسح وتوثيق هذا البوكس مسبقاً." : "This box has already been scanned and verified.")
            : (request.IsManualEntry
                ? (isAr ? "تم التحقق من كود الصندوق يدوياً بنجاح. يلزم إلزامياً تصوير ملصق البوكس لإتمام الاستلام والمراجعة." : "Box code verified manually. A photo of the box label is strictly required to complete pickup for audit review.")
                : (isAr ? "تم التحقق من البوكس بنجاح. يرجى التقاط صورة لحالته لتوثيق الاستلام." : "Box verified. Please take a condition photo to complete pickup."));

        return Result.Success(new ValidatePickupBarcodeResponse(
            IsValid: true,
            BoxId: matchedStop.Id,
            BoxCode: matchedStop.OrderCode,
            CustomerName: matchedStop.CustomerName ?? string.Empty,
            DeliveryZone: zoneName ?? string.Empty,
            MealsCount: matchedStop.BoxCount > 0 ? matchedStop.BoxCount : 1,
            ScanStatus: scanStatus,
            Message: message,
            BelongsToThisDriver: true,
            AssignedDriverId: driver.Id,
            AssignedDriverName: isAr ? driver.FullNameAr : driver.FullNameEn,
            IsManualEntry: request.IsManualEntry,
            ConditionPhotoRequired: true
        ));
    }

    public async Task<Result<StartTripDeliveryResponse>> Handle(
        StartTripDeliveryCommand request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();

        var trip = await (
            from da in _db.DriverAssignments
            join t in _db.Trips on da.TripId equals t.Id
            where da.DriverProfileId == driver.Id && da.Status == "Active"
            orderby t.CreatedAtUtc descending
            select t
        ).FirstOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return Error.NotFound("Trip.NotFound", isAr
                ? "لا توجد رحلة مسندة إليك حالياً."
                : "No trip is currently assigned to you.");
        }

        var stops = await _db.TripStops
            .Where(s => s.TripId == trip.Id)
            .ToListAsync(cancellationToken);

        var pendingStops = stops.Where(s => s.Status == TripStopStatus.Pending).ToList();

        if (pendingStops.Count > 0 && !request.ForceStartWithUnscannedBoxes)
        {
            return Error.Validation("Orders.UnscannedBoxesRemaining", isAr
                ? $"تبقى {pendingStops.Count} صناديق لم يتم مسحها وتوثيق حالتها بعد. يجب إتمام مسح كافة الصناديق قبل بدء التوصيل."
                : $"{pendingStops.Count} boxes have not been scanned and verified yet. All boxes must be scanned before starting delivery.");
        }

        var now = DateTime.UtcNow;
        trip.Status = TripStatus.InProgress;
        trip.StartedAtUtc = now;

        if (!string.IsNullOrWhiteSpace(request.UnscannedReason))
        {
            trip.Notes = string.IsNullOrWhiteSpace(trip.Notes)
                ? $"[ForceStartedWithUnscanned] {request.UnscannedReason}"
                : $"{trip.Notes} | [ForceStartedWithUnscanned] {request.UnscannedReason}";
        }

        // Also ensure active DriverWorkSession is tracking in-delivery status
        var activeSession = await _db.DriverWorkSessions
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        if (activeSession is not null)
        {
            activeSession.InDeliveryOrdersCount = stops.Count(s => s.Status == TripStopStatus.PickedUp);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var pickedUpCount = stops.Count(s => s.Status == TripStopStatus.PickedUp);
        var msg = isAr
            ? $"تم بدء رحلة التوصيل بنجاح بحمولة {pickedUpCount} صندوق. رافقتك السلامة!"
            : $"Trip delivery started successfully with {pickedUpCount} boxes. Have a safe drive!";

        return Result.Success(new StartTripDeliveryResponse(
            TripId: trip.Id,
            TripCode: trip.Code,
            Status: "InProgress",
            DispatchedBoxesCount: pickedUpCount,
            Message: msg,
            StartedAtUtc: now
        ));
    }

    private Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
        => DriverResolutionHelper.ResolveDriverAsync(_db, _currentUser, driverId, cancellationToken);
}
