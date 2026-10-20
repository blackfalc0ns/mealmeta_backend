using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record StartTripCommand(
    Guid TripId,
    decimal Latitude,
    decimal Longitude,
    string IdempotencyKey) : ICommand<StartTripResponseDto>;

public sealed class StartTripCommandHandler
    : ICommandHandler<StartTripCommand, StartTripResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IApplicationLockManager? _locks;
    private readonly IApplicationTransactionExecutor? _transactions;

    public StartTripCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IApplicationLockManager? locks = null,
        IApplicationTransactionExecutor? transactions = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _locks = locks;
        _transactions = transactions;
    }

    public async Task<Result<StartTripResponseDto>> Handle(
        StartTripCommand request,
        CancellationToken cancellationToken)
    {
        if (_transactions is null || _locks is null)
            return Error.Conflict("TRIP_TRANSACTION_UNAVAILABLE", _messages.GetMessage("driver_support_handover.transaction_unavailable", "Trip start requires transactional locks."));
        return await _transactions.ExecuteResultAsync(ct => HandleCoreAsync(request, ct), cancellationToken);
    }

    private async Task<Result<StartTripResponseDto>> HandleCoreAsync(
        StartTripCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value && !d.IsDeleted, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Error.Validation("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.");
        }

        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.TripId}:{request.Latitude}:{request.Longitude}")));
        var idempotencyScope = $"TripStart:{driver.Id}";

        // Idempotency check
        var existingRecord = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Scope == idempotencyScope && r.Key == request.IdempotencyKey, cancellationToken);

        if (existingRecord != null)
        {
            if (existingRecord.RequestHash != requestHash)
            {
                return Error.Conflict("IDEMPOTENCY_KEY_REUSED", "An operation with this idempotency key already exists with different request parameters.");
            }

            if (!string.IsNullOrWhiteSpace(existingRecord.ResponseBody))
            {
                var cached = JsonSerializer.Deserialize<StartTripResponseDto>(existingRecord.ResponseBody);
                if (cached != null)
                {
                    return Result.Success(cached with
                    {
                        Message = _messages.GetMessage(MessageCodes.DriverTrip.Started,
                            arguments: new object[] { cached.DispatchedBoxesCount })
                    });
                }
            }
        }

        await _locks.LockTripAsync(request.TripId, cancellationToken);
        await _locks.LockDriverAsync(driver.Id, cancellationToken);
        await _db.ReloadEntityAsync(driver, cancellationToken);
        if (await DriverSupportHandoverMutationGuard.HasActiveHandoverForTripAsync(_db, request.TripId, cancellationToken))
            return Error.Conflict("HANDOVER_PENDING", _messages.GetMessage("driver_support_handover.pending", "The active support handover must be completed or cancelled first."));
        if (!(await _db.GetActiveAccountUserIdsAsync(new[] { driver.UserId }, cancellationToken)).Contains(driver.UserId))
            return Error.Forbidden("DRIVER_ACCOUNT_INACTIVE", _messages.GetMessage("driver.account_inactive", "The driver account is not active."));

        if (request.Latitude == 0 && request.Longitude == 0)
        {
            return Error.Validation("LOCATION_REQUIRED", "Driver GPS coordinates are required to start trip.");
        }

        var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == request.TripId, cancellationToken);

        if (trip is null)
        {
            return Error.NotFound("TRIP_NOT_FOUND", "Trip was not found.");
        }

        await _db.ReloadEntityAsync(trip, cancellationToken);
        var stops = await _db.TripStops.Where(s => s.TripId == request.TripId).ToListAsync(cancellationToken);
        foreach (var stop in stops)
            if (await DriverSupportHandoverMutationGuard.HasActiveHoldForStopAsync(_db, stop.Id, cancellationToken))
                return Error.Conflict("DELIVERY_HOLD_ACTIVE", _messages.GetMessage("driver_support_handover.hold_active", "A delivery hold blocks trip start."));
        var assignments = await _db.DriverAssignments.Where(a => a.TripId == request.TripId).ToListAsync(cancellationToken);

        var isAssignedToDriver = assignments.Any(da => da.DriverProfileId == driver.Id && (da.Status == "Active" || da.Status == "Assigned"));
        if (!isAssignedToDriver)
        {
            return Error.Forbidden("DRIVER_NOT_AUTHORIZED", "This trip is not assigned to you.");
        }

        var alreadyStarted = trip.Status == TripStatus.InProgress;
        if (alreadyStarted && !trip.StartedAtUtc.HasValue)
        {
            return Error.Conflict("TRIP_ALREADY_STARTED", "This trip is already in progress.");
        }

        if (!alreadyStarted && trip.Status != TripStatus.Assigned)
        {
            return Error.Validation("TRIP_NOT_READY", "Trip cannot be started from its current status.");
        }

        var pendingStops = stops.Where(s => s.Status != TripStopStatus.PickedUp && s.Status != TripStopStatus.Delivered).ToList();
        if (!alreadyStarted && pendingStops.Count > 0)
        {
            return Error.Validation("UNSCANNED_BOXES_EXIST", $"{pendingStops.Count} boxes have not been scanned and verified yet.");
        }

        var nowUtc = DateTime.UtcNow;
        if (!alreadyStarted)
        {
            trip.Status = TripStatus.InProgress;
            trip.StartedAtUtc = nowUtc;

            // Reopening an already started trip must not reset work-session counts.
            var activeSession = await _db.DriverWorkSessions
                .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);
            if (activeSession != null)
            {
                activeSession.InDeliveryOrdersCount = stops.Count;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        var msg = _messages.GetMessage(MessageCodes.DriverTrip.Started, arguments: new object[] { stops.Count });

        var response = new StartTripResponseDto(
            TripId: trip.Id,
            TripCode: trip.Code,
            Status: "InProgress",
            DispatchedBoxesCount: stops.Count,
            Message: msg,
            StartedAtUtc: trip.StartedAtUtc!.Value);

        // Record Idempotency outcome
        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Scope = idempotencyScope,
            Key = request.IdempotencyKey,
            RequestHash = requestHash,
            ResponseBody = JsonSerializer.Serialize(response),
            HttpStatusCode = 200,
            UserId = driver.UserId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddHours(24)
        });
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(response);
    }
}
