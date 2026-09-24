using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.KitchenCoordination;

public sealed record MarkTripReadyForPickupCommand(
    Guid TripId,
    string? Notes = null) : ICommand<TripReadyForPickupResponseDto>;

public sealed record TripReadyForPickupResponseDto(
    Guid TripId,
    string TripCode,
    int BoxesCount,
    Guid? DriverProfileId,
    string? DriverName,
    string Message,
    DateTime ReadyAtUtc);

public sealed record AcknowledgeReturnedBoxCommand(
    Guid BoxId,
    string Disposition,
    string? KitchenSupervisorNotes = null) : ICommand<AcknowledgeReturnedBoxResponseDto>;

public sealed record AcknowledgeReturnedBoxResponseDto(
    Guid BoxId,
    string BoxCode,
    string Disposition,
    string Message,
    DateTime AcknowledgedAtUtc);

public sealed class KitchenPickupCoordinationCommandHandlers
    : ICommandHandler<MarkTripReadyForPickupCommand, TripReadyForPickupResponseDto>,
      ICommandHandler<AcknowledgeReturnedBoxCommand, AcknowledgeReturnedBoxResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDriverPushNotifier? _driverPushNotifier;

    public KitchenPickupCoordinationCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IDriverPushNotifier? driverPushNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _driverPushNotifier = driverPushNotifier;
    }

    public async Task<Result<TripReadyForPickupResponseDto>> Handle(
        MarkTripReadyForPickupCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await _db.Trips
            .Include(t => t.Stops)
            .FirstOrDefaultAsync(t => t.Id == request.TripId, cancellationToken);

        if (trip is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الرحلة المحددة بالمطبخ.");
        }

        var now = DateTime.UtcNow;
        trip.Notes = string.IsNullOrWhiteSpace(trip.Notes)
            ? $"[KitchenReadyForPickup] at {now:yyyy-MM-dd HH:mm:ss} UTC: {request.Notes}"
            : $"{trip.Notes} | [KitchenReadyForPickup] at {now:yyyy-MM-dd HH:mm:ss} UTC: {request.Notes}";

        // Find assigned driver
        var assignment = await _db.DriverAssignments
            .Where(da => da.TripId == trip.Id && da.Status == "Active")
            .FirstOrDefaultAsync(cancellationToken);

        MealMate.Domain.IdentityAccess.DriverProfile? driverProfile = null;
        if (assignment is not null)
        {
            driverProfile = await _db.DriverProfiles
                .FirstOrDefaultAsync(dp => dp.Id == assignment.DriverProfileId, cancellationToken);
        }

        var driverName = driverProfile?.FullNameAr ?? driverProfile?.FullNameEn;

        if (assignment is not null && driverProfile is not null)
        {
            _db.DriverNotifications.Add(new MealMate.Domain.IdentityAccess.DriverNotification
            {
                UserId = driverProfile.UserId,
                DriverProfileId = assignment.DriverProfileId,
                Type = "trip-ready-for-pickup",
                TitleAr = "الشحنة جاهزة للاستلام بالمطبخ 🍳📦",
                TitleEn = "Shipment Ready for Pickup at Kitchen",
                BodyAr = $"أنهى المطبخ تعبئة وجبات الرحلة {trip.Code}. يمكنك التوجه لاستلامها ومسح الباركود الآن.",
                BodyEn = $"Kitchen finished packing meals for trip {trip.Code}. You can now proceed to pickup and scan.",
                Section = "pickup",
                SubjectId = trip.Id,
                DeepLink = $"/driver/pickup/manifest",
                IsRead = false,
                SentAtUtc = now
            });

            if (_driverPushNotifier != null)
            {
                await _driverPushNotifier.NotifyKitchenReadyForPickupAsync(
                    driverProfile.UserId,
                    trip.Id,
                    trip.Code,
                    cancellationToken);
            }
        }

        // Add audit log
        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "KitchenMarkedReadyForPickup",
            EntityType = "Trip",
            EntityId = trip.Id,
            Reason = request.Notes,
            ActorUserId = _currentUser.UserId
        });

        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
        var msg = isAr
            ? $"تم تأكيد جاهزية الشحنة {trip.Code} في المطبخ وإشعار السائق بنجاح."
            : $"Trip {trip.Code} marked ready for pickup and driver notified successfully.";

        return Result.Success(new TripReadyForPickupResponseDto(
            TripId: trip.Id,
            TripCode: trip.Code,
            BoxesCount: trip.Stops.Count,
            DriverProfileId: assignment?.DriverProfileId,
            DriverName: driverName,
            Message: msg,
            ReadyAtUtc: now
        ));
    }

    public async Task<Result<AcknowledgeReturnedBoxResponseDto>> Handle(
        AcknowledgeReturnedBoxCommand request,
        CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على البوكس المحدد.");
        }

        var now = DateTime.UtcNow;
        stop.FailureReason = string.IsNullOrWhiteSpace(stop.FailureReason)
            ? $"KitchenAcknowledged: [{request.Disposition}] {request.KitchenSupervisorNotes}"
            : $"{stop.FailureReason} | KitchenAcknowledged: [{request.Disposition}] {request.KitchenSupervisorNotes}";

        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "KitchenAcknowledgedReturn",
            EntityType = "TripStop",
            EntityId = request.BoxId,
            Reason = $"Disposition: {request.Disposition} - {request.KitchenSupervisorNotes}",
            ActorUserId = _currentUser.UserId
        });

        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
        var msg = isAr
            ? $"تم اعتماد استلام البوكس المرتجع {stop.OrderCode} بالمطبخ وتسجيل الإجراء بنجاح."
            : $"Returned box {stop.OrderCode} acknowledged at kitchen successfully.";

        return Result.Success(new AcknowledgeReturnedBoxResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            Disposition: request.Disposition,
            Message: msg,
            AcknowledgedAtUtc: now
        ));
    }
}
