using System;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantOperations;

public sealed record RestaurantEmergencyOverrideDto(
    Guid Id,
    Guid RestaurantId,
    DateOnly StartDate,
    DateOnly EndDate,
    int? ReducedCapacity,
    bool IsClosed,
    string Reason,
    Guid TriggeredByUserId,
    bool IsResolved,
    DateTime? ResolvedAtUtc,
    string? ResolutionReason,
    DateTime CreatedAtUtc);

public sealed record ActivateRestaurantEmergencyOverrideCommand(
    Guid RestaurantId,
    DateOnly From,
    DateOnly To,
    int? ReducedCapacity,
    bool Closed,
    string Reason) : ICommand<RestaurantEmergencyOverrideDto>;

public sealed class ActivateRestaurantEmergencyOverrideCommandHandler
    : ICommandHandler<ActivateRestaurantEmergencyOverrideCommand, RestaurantEmergencyOverrideDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public ActivateRestaurantEmergencyOverrideCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<RestaurantEmergencyOverrideDto>> Handle(
        ActivateRestaurantEmergencyOverrideCommand request,
        CancellationToken cancellationToken)
    {
        if (request.From > request.To)
        {
            return Error.Validation("Emergency.InvalidDateRange", "Start date must be on or before end date.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation("Emergency.ReasonRequired", "Reason is required for emergency override.");
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(_currentUser, request.RestaurantId);
        if (scopeResult.IsFailure)
        {
            return Result.Failure<RestaurantEmergencyOverrideDto>(scopeResult.Error);
        }

        var restaurantId = scopeResult.Value;

        var approvedBaseCapacity = await (from d in _db.DailyCapacities
                                          where d.RestaurantId == restaurantId && d.CapacityType == CapacityType.Base
                                          orderby d.ServiceDate descending
                                          select (int?)d.MaxBoxes).FirstOrDefaultAsync(cancellationToken) ?? 50;

        if (request.ReducedCapacity.HasValue && request.ReducedCapacity.Value > approvedBaseCapacity)
        {
            return Result.Failure<RestaurantEmergencyOverrideDto>(
                Error.Validation("Emergency.CannotIncreaseCapacity", "Emergency override cannot increase capacity above approved limit."));
        }

        var effectiveCapacity = request.ReducedCapacity.HasValue
            ? Math.Min(approvedBaseCapacity, request.ReducedCapacity.Value)
            : (int?)null;

        var entity = new RestaurantEmergencyOverride
        {
            RestaurantId = restaurantId,
            StartDate = request.From,
            EndDate = request.To,
            ReducedCapacity = effectiveCapacity,
            IsClosed = request.Closed,
            Reason = request.Reason.Trim(),
            TriggeredByUserId = _currentUser.UserId ?? Guid.Empty,
            IsResolved = false,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.RestaurantEmergencyOverrides.Add(entity);

        if (request.Closed)
        {
            var startUtc = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endUtc = request.To.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            _db.BusyStatuses.Add(new BusyStatus
            {
                RestaurantId = restaurantId,
                IsBusy = true,
                Reason = request.Reason.Trim(),
                StartsAtUtc = startUtc,
                EndsAtUtc = endUtc,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        if (effectiveCapacity.HasValue)
        {
            for (var cur = request.From; cur <= request.To; cur = cur.AddDays(1))
            {
                var cap = await _db.DailyCapacities.FirstOrDefaultAsync(c => c.RestaurantId == restaurantId && c.ServiceDate == cur, cancellationToken);
                if (cap != null)
                {
                    cap.MaxBoxes = Math.Min(cap.MaxBoxes, effectiveCapacity.Value);
                    cap.ModifiedAtUtc = _clock.UtcNow;
                }
                else
                {
                    _db.DailyCapacities.Add(new DailyCapacity
                    {
                        RestaurantId = restaurantId,
                        ServiceDate = cur,
                        CapacityType = CapacityType.DateOverride,
                        MaxBoxes = effectiveCapacity.Value,
                        ReservedBoxes = 0,
                        CreatedAtUtc = _clock.UtcNow,
                    });
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "RestaurantEmergencyOverrideActivated",
            nameof(RestaurantEmergencyOverride),
            entity.Id,
            Reason: request.Reason,
            SourceChannel: "restaurant-operations"), cancellationToken);

        var dto = new RestaurantEmergencyOverrideDto(
            entity.Id,
            entity.RestaurantId,
            entity.StartDate,
            entity.EndDate,
            entity.ReducedCapacity,
            entity.IsClosed,
            entity.Reason,
            entity.TriggeredByUserId,
            entity.IsResolved,
            entity.ResolvedAtUtc,
            entity.ResolutionReason,
            entity.CreatedAtUtc);

        return Result.Success(dto);
    }
}

public sealed record ResolveRestaurantEmergencyOverrideCommand(
    Guid OverrideId,
    string Reason) : ICommand<bool>;

public sealed class ResolveRestaurantEmergencyOverrideCommandHandler
    : ICommandHandler<ResolveRestaurantEmergencyOverrideCommand, bool>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public ResolveRestaurantEmergencyOverrideCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<bool>> Handle(
        ResolveRestaurantEmergencyOverrideCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation("Emergency.ReasonRequired", "Resolution reason is required.");
        }

        var overrideEntity = await _db.RestaurantEmergencyOverrides
            .FirstOrDefaultAsync(x => x.Id == request.OverrideId, cancellationToken);

        if (overrideEntity is null)
        {
            return Error.NotFound("Emergency.NotFound", "Emergency override was not found.");
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(_currentUser, overrideEntity.RestaurantId);
        if (scopeResult.IsFailure)
        {
            return Result.Failure<bool>(scopeResult.Error);
        }

        overrideEntity.IsResolved = true;
        overrideEntity.ResolvedAtUtc = _clock.UtcNow;
        overrideEntity.ResolvedByUserId = _currentUser.UserId;
        overrideEntity.ResolutionReason = request.Reason.Trim();
        overrideEntity.ModifiedAtUtc = _clock.UtcNow;

        if (overrideEntity.IsClosed)
        {
            var busyStatuses = await _db.BusyStatuses
                .Where(b => b.RestaurantId == overrideEntity.RestaurantId && b.IsBusy && b.Reason == overrideEntity.Reason)
                .ToListAsync(cancellationToken);

            foreach (var b in busyStatuses)
            {
                b.IsBusy = false;
                b.ModifiedAtUtc = _clock.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "RestaurantEmergencyOverrideResolved",
            nameof(RestaurantEmergencyOverride),
            overrideEntity.Id,
            Reason: request.Reason,
            SourceChannel: "restaurant-operations"), cancellationToken);

        return Result.Success(true);
    }
}
