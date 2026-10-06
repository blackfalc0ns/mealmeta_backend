using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Services;

/// <summary>
/// Persists-relationship-based participant check. Authorization is derived from the
/// active driver assignment and the subscription-day ownership chain
/// (SubscriptionDay → Subscription → CustomerUserId) — never from ID secrecy or
/// client-supplied identifiers.
/// </summary>
public sealed class VoiceCallAuthorizationService : IVoiceCallAuthorizationService
{
    private readonly IAppDbContext _db;

    public VoiceCallAuthorizationService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result> AuthorizeTripStopParticipantAsync(Guid tripStopId, Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(VoiceCallErrors.TripStopNotFound());
        }

        var stop = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .FirstOrDefaultAsync(s => s.Id == tripStopId, cancellationToken);

        if (stop is null)
        {
            return Result.Failure(VoiceCallErrors.TripStopNotFound());
        }

        // Assigned driver: the stop's trip must carry an active assignment resolving
        // to this user's driver profile. Inactive/reassigned assignments do not grant access.
        if (stop.Trip is not null)
        {
            var activeAssignmentProfileIds = stop.Trip.DriverAssignments
                .Where(a => a.Status == "Active")
                .Select(a => a.DriverProfileId)
                .ToHashSet();

            if (activeAssignmentProfileIds.Count > 0)
            {
                var isAssignedDriver = await _db.DriverProfiles
                    .AsNoTracking()
                    .AnyAsync(dp => activeAssignmentProfileIds.Contains(dp.Id) && dp.UserId == userId, cancellationToken);

                if (isAssignedDriver)
                {
                    return Result.Success();
                }
            }
        }

        // Customer: real ownership chain only. If the stop has no subscription-day
        // link there is no resolvable customer identity — we never fabricate one.
        if (stop.SubscriptionDayId.HasValue)
        {
            var isCustomer = await _db.SubscriptionDays
                .AsNoTracking()
                .Where(sd => sd.Id == stop.SubscriptionDayId.Value)
                .Join(_db.Subscriptions,
                    sd => sd.SubscriptionId,
                    s => s.Id,
                    (sd, s) => s.CustomerUserId)
                .AnyAsync(customerUserId => customerUserId == userId, cancellationToken);

            if (isCustomer)
            {
                return Result.Success();
            }
        }

        return Result.Failure(VoiceCallErrors.TripStopNotFound());
    }
}
