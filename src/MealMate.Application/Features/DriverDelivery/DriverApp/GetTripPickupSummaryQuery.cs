using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record GetTripPickupSummaryQuery(Guid TripId) : IQuery<TripPickupSummaryResponseDto>;

public sealed class GetTripPickupSummaryQueryHandler
    : IQueryHandler<GetTripPickupSummaryQuery, TripPickupSummaryResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetTripPickupSummaryQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<TripPickupSummaryResponseDto>> Handle(
        GetTripPickupSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

        var trip = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .FirstOrDefaultAsync(t => t.Id == request.TripId, cancellationToken);

        if (trip is null)
        {
            return Error.NotFound("TRIP_NOT_FOUND", "Trip was not found.");
        }

        // Ownership verification
        var isAssignedToDriver = trip.DriverAssignments.Any(da => da.DriverProfileId == driver.Id && da.Status == "Active");
        if (!isAssignedToDriver)
        {
            return Error.Forbidden("DRIVER_NOT_AUTHORIZED", "This trip is not assigned to you.");
        }

        var stops = trip.Stops.ToList();

        var pickupScans = await _db.PickupScans
            .AsNoTracking()
            .Where(s => s.TripId == trip.Id)
            .ToListAsync(cancellationToken);

        var scansByBarcode = pickupScans
            .Where(s => !string.IsNullOrEmpty(s.BarcodeValue))
            .GroupBy(s => s.BarcodeValue, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ScannedAtUtc).First(), StringComparer.OrdinalIgnoreCase);

        var confirmedPhotos = await _db.PickupConditionPhotos
            .AsNoTracking()
            .Where(p => p.TripId == trip.Id && p.ConfirmedAtUtc != null && p.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);

        var photosByBoxId = confirmedPhotos
            .GroupBy(p => p.BoxId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.ConfirmedAtUtc).First());

        var validations = await _db.PickupValidations
            .AsNoTracking()
            .Where(v => v.TripId == trip.Id && v.DriverProfileId == driver.Id)
            .ToListAsync(cancellationToken);

        var latestValidations = validations
            .GroupBy(v => v.BoxId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.ValidatedAtUtc).First());

        var assignedBoxesCount = stops.Count;
        var receivedBoxesCount = stops.Count(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Delivered);
        var remainingBoxesCount = Math.Max(0, assignedBoxesCount - receivedBoxesCount);
        var allBoxesReceived = remainingBoxesCount == 0 && assignedBoxesCount > 0;
        var validatedBoxesCount = latestValidations.Count;

        var canStartTrip = allBoxesReceived && (trip.Status == TripStatus.Assigned);

        string pickupStatus;
        if (canStartTrip)
        {
            pickupStatus = "ReadyToStart";
        }
        else if (receivedBoxesCount > 0)
        {
            pickupStatus = "InProgress";
        }
        else
        {
            pickupStatus = "Pending";
        }

        var isAr = _messages.IsArabic();
        var boxesDtoList = new List<TripPickupSummaryBoxDto>();

        foreach (var stop in stops)
        {
            scansByBarcode.TryGetValue(stop.OrderCode, out var scan);
            photosByBoxId.TryGetValue(stop.Id, out var photo);
            latestValidations.TryGetValue(stop.Id, out var validation);

            var photoStorageKey = photo?.StorageKey ?? scan?.PhotoPath;
            string? photoUrl = !string.IsNullOrWhiteSpace(photoStorageKey)
                ? $"/api/v1/uploads/content?key={Uri.EscapeDataString(photoStorageKey)}"
                : null;

            boxesDtoList.Add(new TripPickupSummaryBoxDto(
                BoxId: stop.Id,
                BoxCode: stop.OrderCode ?? string.Empty,
                CustomerName: stop.CustomerName ?? (isAr ? "العميل" : "Customer"),
                Status: stop.Status.ToString(),
                ValidatedAtUtc: validation?.ValidatedAtUtc,
                PickedUpAtUtc: scan?.ScannedAtUtc,
                ConditionPhotoUrl: photoUrl));
        }

        return Result.Success(new TripPickupSummaryResponseDto(
            TripId: trip.Id,
            TripCode: trip.Code,
            TripStatus: trip.Status.ToString(),
            PickupStatus: pickupStatus,
            AssignedBoxesCount: assignedBoxesCount,
            ValidatedBoxesCount: validatedBoxesCount,
            ReceivedBoxesCount: receivedBoxesCount,
            RemainingBoxesCount: remainingBoxesCount,
            AllBoxesReceived: allBoxesReceived,
            CanStartTrip: canStartTrip,
            Boxes: boxesDtoList));
    }
}
