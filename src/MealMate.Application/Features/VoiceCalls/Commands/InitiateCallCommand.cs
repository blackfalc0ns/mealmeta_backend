using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record InitiateCallCommand(
    Guid TripStopId,
    Guid ClientRequestId) : ICommand<InitiateCallResponseDto>;

public sealed class InitiateCallCommandHandler : ICommandHandler<InitiateCallCommand, InitiateCallResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICallSignalingNotifier _notifier;
    private readonly IVoiceCallPushNotifier _pushNotifier;
    private readonly IVoiceCallParticipantLockService _lockService;

    public InitiateCallCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ICallSignalingNotifier? notifier = null,
        IVoiceCallPushNotifier? pushNotifier = null,
        IVoiceCallParticipantLockService? lockService = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? NullCallSignalingNotifier.Instance;
        _pushNotifier = pushNotifier ?? NullVoiceCallPushNotifier.Instance;
        _lockService = lockService ?? NullVoiceCallParticipantLockService.Instance;
    }

    public async Task<Result<InitiateCallResponseDto>> Handle(InitiateCallCommand request, CancellationToken cancellationToken)
    {
        // 1. Authenticate caller identity from server context
        if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
        {
            return VoiceCallErrors.Unauthorized();
        }

        var callerUserId = _currentUser.UserId.Value;

        // 2. Pre-insert fast Idempotency check: Return existing call if client retried with same request ID
        var existingCall = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CallerUserId == callerUserId && c.ClientRequestId == request.ClientRequestId, cancellationToken);

        if (existingCall is not null)
        {
            return InitiateCallResponseDto.FromEntity(existingCall);
        }

        // 3. Load TripStop with assigned delivery trip
        var stop = await _db.TripStops
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .FirstOrDefaultAsync(s => s.Id == request.TripStopId, cancellationToken);

        if (stop is null)
        {
            return VoiceCallErrors.TripStopNotFound();
        }

        // 4. Validate Delivery Contact Window
        if (stop.Status != TripStopStatus.Pending && stop.Status != TripStopStatus.PickedUp)
        {
            return VoiceCallErrors.DeliveryNotActive(stop.Status);
        }

        if (stop.Trip is not null && stop.Trip.Status == TripStatus.Cancelled)
        {
            return VoiceCallErrors.TripCancelled();
        }

        // 5. Resolve Customer and Driver identities
        Guid? customerUserId = null;
        if (stop.SubscriptionDayId.HasValue)
        {
            customerUserId = await _db.SubscriptionDays
                .Where(sd => sd.Id == stop.SubscriptionDayId.Value)
                .Join(_db.Subscriptions, sd => sd.SubscriptionId, s => s.Id, (sd, s) => (Guid?)s.CustomerUserId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        Guid? driverUserId = null;
        var activeAssignment = stop.Trip?.DriverAssignments
            .OrderByDescending(a => a.AssignedAtUtc)
            .FirstOrDefault();

        if (activeAssignment is not null)
        {
            driverUserId = await _db.DriverProfiles
                .Where(dp => dp.Id == activeAssignment.DriverProfileId)
                .Select(dp => (Guid?)dp.UserId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // 6. Determine participant roles and authorization
        CallParticipantType callerType;
        CallParticipantType receiverType;
        Guid receiverUserId;

        if (driverUserId.HasValue && driverUserId.Value == callerUserId)
        {
            callerType = CallParticipantType.Driver;
            receiverType = CallParticipantType.Customer;

            if (!customerUserId.HasValue || customerUserId.Value == Guid.Empty)
            {
                return VoiceCallErrors.UnresolvedCustomer();
            }

            receiverUserId = customerUserId.Value;
        }
        else if (customerUserId.HasValue && customerUserId.Value == callerUserId)
        {
            callerType = CallParticipantType.Customer;
            receiverType = CallParticipantType.Driver;

            if (!driverUserId.HasValue || driverUserId.Value == Guid.Empty)
            {
                return VoiceCallErrors.UnassignedDelivery();
            }

            receiverUserId = driverUserId.Value;
        }
        else
        {
            // Authenticated user is neither the customer nor the assigned driver
            return VoiceCallErrors.Forbidden();
        }

        if (callerUserId == receiverUserId)
        {
            return VoiceCallErrors.SelfCallNotAllowed();
        }

        // 7. Fast pre-lock busy detection check
        var fastCallerBusy = await _db.CallSessions
            .AsNoTracking()
            .AnyAsync(
                c => (c.CallerUserId == callerUserId || c.ReceiverUserId == callerUserId)
                     && (c.Status == CallStatus.Created
                         || c.Status == CallStatus.Ringing
                         || c.Status == CallStatus.Accepted
                         || c.Status == CallStatus.Connecting
                         || c.Status == CallStatus.Active),
                cancellationToken);

        if (fastCallerBusy)
        {
            return VoiceCallErrors.CallerBusy();
        }

        var fastReceiverBusy = await _db.CallSessions
            .AsNoTracking()
            .AnyAsync(
                c => (c.CallerUserId == receiverUserId || c.ReceiverUserId == receiverUserId)
                     && (c.Status == CallStatus.Created
                         || c.Status == CallStatus.Ringing
                         || c.Status == CallStatus.Accepted
                         || c.Status == CallStatus.Connecting
                         || c.Status == CallStatus.Active),
                cancellationToken);

        if (fastReceiverBusy)
        {
            return VoiceCallErrors.RecipientBusy();
        }

        // 8. Acquire cross-request participant locks and execute critical creation section within database transaction
        var lockResult = await _lockService.ExecuteInParticipantLockAsync(
            callerUserId,
            receiverUserId,
            async ct =>
            {
                // Re-check idempotency under lock to handle racing identical requests
                var existingCallUnderLock = await _db.CallSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CallerUserId == callerUserId && c.ClientRequestId == request.ClientRequestId, ct);

                if (existingCallUnderLock is not null)
                {
                    return Result.Success(existingCallUnderLock);
                }

                // Re-check busy detection under lock to guarantee no simultaneous overlapping calls
                var callerIsBusy = await _db.CallSessions
                    .AsNoTracking()
                    .AnyAsync(
                        c => (c.CallerUserId == callerUserId || c.ReceiverUserId == callerUserId)
                             && (c.Status == CallStatus.Created
                                 || c.Status == CallStatus.Ringing
                                 || c.Status == CallStatus.Accepted
                                 || c.Status == CallStatus.Connecting
                                 || c.Status == CallStatus.Active),
                        ct);

                if (callerIsBusy)
                {
                    return Result.Failure<CallSession>(VoiceCallErrors.CallerBusy());
                }

                var receiverIsBusy = await _db.CallSessions
                    .AsNoTracking()
                    .AnyAsync(
                        c => (c.CallerUserId == receiverUserId || c.ReceiverUserId == receiverUserId)
                             && (c.Status == CallStatus.Created
                                 || c.Status == CallStatus.Ringing
                                 || c.Status == CallStatus.Accepted
                                 || c.Status == CallStatus.Connecting
                                 || c.Status == CallStatus.Active),
                        ct);

                if (receiverIsBusy)
                {
                    return Result.Failure<CallSession>(VoiceCallErrors.RecipientBusy());
                }

                var now = DateTime.UtcNow;
                var session = new CallSession(
                    tripStopId: request.TripStopId,
                    callerUserId: callerUserId,
                    callerType: callerType,
                    receiverUserId: receiverUserId,
                    receiverType: receiverType,
                    clientRequestId: request.ClientRequestId,
                    startedAtUtc: now);

                session.MarkRinging(now);
                _db.CallSessions.Add(session);

                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "VoiceCallInitiated",
                    EntityType = "CallSession",
                    EntityId = session.Id,
                    Reason = $"1-to-1 in-app VoIP call initiated from {callerType} to {receiverType} on TripStop {request.TripStopId}",
                    ActorUserId = callerUserId,
                    OccurredAtUtc = now
                });

                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    var racedCall = await _db.CallSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.CallerUserId == callerUserId && c.ClientRequestId == request.ClientRequestId, ct);

                    if (racedCall is not null)
                    {
                        return Result.Success(racedCall);
                    }

                    throw;
                }

                return Result.Success(session);
            },
            cancellationToken);

        if (lockResult.IsFailure)
        {
            return Result.Failure<InitiateCallResponseDto>(lockResult.Error);
        }

        var callSession = lockResult.Value;

        // 9. Post-Commit Realtime and Push Notifications (Strictly executed AFTER database transaction commits)
        if (callSession.Status == CallStatus.Ringing)
        {
            try
            {
                await _notifier.NotifyIncomingCallAsync(
                    callSession.Id,
                    callSession.ReceiverUserId,
                    callSession.CallerUserId,
                    callSession.CallerType.ToString(),
                    callSession.TripStopId,
                    callSession.StartedAtUtc,
                    callSession.RingingAtUtc,
                    cancellationToken);

                if (callSession.RingingAtUtc.HasValue)
                {
                    await _notifier.NotifyCallRingingAsync(
                        callSession.Id,
                        callSession.CallerUserId,
                        callSession.RingingAtUtc.Value,
                        cancellationToken);
                }
            }
            catch
            {
                // Transient realtime notification errors must not rollback or fail persistent call creation
            }

            try
            {
                await _pushNotifier.SendIncomingCallPushAsync(
                    callSession.Id,
                    callSession.ReceiverUserId,
                    callSession.CallerUserId,
                    callSession.CallerType.ToString(),
                    callSession.TripStopId,
                    cancellationToken);
            }
            catch
            {
                // Transient push notification errors must not rollback or fail persistent call creation
            }
        }

        return InitiateCallResponseDto.FromEntity(callSession);
    }
}
