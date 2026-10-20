using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record DriverReassignmentRequestDto(Guid RequestId, Guid? BoxId, string? BoxCode,
    string Status, string? Reason, string? Notes, DateTime RequestedAtUtc,
    string? Decision, string? DecisionNotes, Guid? DecidedByUserId, DateTime? DecidedAtUtc,
    Guid? HandoverId, string? HandoverStatus, Guid? ReplacementDriverId, Guid? ReplacementTripId,
    decimal? Latitude, decimal? Longitude);
public sealed record GetDriverReassignmentRequestsQuery(Guid? BoxId = null, int Page = 1, int PageSize = 20)
    : IQuery<IReadOnlyList<DriverReassignmentRequestDto>>;
public sealed record GetDriverReassignmentRequestQuery(Guid RequestId) : IQuery<DriverReassignmentRequestDto>;
public sealed record DecideDriverReassignmentCommand(Guid RequestId, string Decision, string? Notes = null)
    : ICommand<DriverReassignmentRequestDto>;

public sealed class DriverReassignmentHandlers(IAppDbContext db, ICurrentUser user, ILocalizedMessageProvider messages,
    IDateTimeProvider clock, IApplicationTransactionExecutor transactions, IApplicationLockManager locks) :
    IQueryHandler<GetDriverReassignmentRequestsQuery, IReadOnlyList<DriverReassignmentRequestDto>>,
    IQueryHandler<GetDriverReassignmentRequestQuery, DriverReassignmentRequestDto>,
    ICommandHandler<DecideDriverReassignmentCommand, DriverReassignmentRequestDto>
{
    private const string Category = "DriverReassignmentRequested";
    private string Text(string code) => messages.GetMessage("driver_reassignment." + code);
    private Error Forbidden() => Error.Forbidden("REASSIGNMENT_FORBIDDEN", Text("forbidden"));
    private Error Missing() => Error.NotFound("REASSIGNMENT_NOT_FOUND", Text("not_found"));
    private bool IsDispatcher => user.IsAuthenticated && user.UserId.HasValue
        && user.UserType is UserType.DeliveryManager or UserType.Admin;
    private bool Scope(Complaint c) => IsDispatcher && (user.UserType == UserType.Admin || c.RestaurantId.HasValue && c.RestaurantId == user.RestaurantId);
    private async Task<Guid?> DriverAsync(CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserType != UserType.Driver || !user.UserId.HasValue) return null;
        if (!(await db.GetActiveAccountUserIdsAsync(new[] { user.UserId.Value }, ct)).Contains(user.UserId.Value)) return null;
        return await db.DriverProfiles.AsNoTracking().Where(x => x.UserId == user.UserId && !x.IsDeleted)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<Result<IReadOnlyList<DriverReassignmentRequestDto>>> Handle(GetDriverReassignmentRequestsQuery request, CancellationToken ct)
    {
        var driver = await DriverAsync(ct);
        if (!driver.HasValue) return Forbidden();
        var query = db.Complaints.AsNoTracking().Where(x => !x.IsDeleted && x.Category == Category && x.DriverProfileId == driver);
        if (request.BoxId.HasValue)
        {
            var stop = await db.TripStops.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.BoxId.Value, ct);
            if (stop is null) return Missing();
            var identity = stop.DailyOrderId ?? stop.Id;
            query = query.Where(x => x.ReassignmentTripStopId == request.BoxId
                || x.ReassignmentTripStopId == null && x.OrderId == identity);
        }
        var items = await query.OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Id)
            .Skip((Math.Clamp(request.Page, 1, 100000) - 1) * Math.Clamp(request.PageSize, 1, 100))
            .Take(Math.Clamp(request.PageSize, 1, 100)).ToListAsync(ct);
        var result = new List<DriverReassignmentRequestDto>();
        foreach (var item in items) result.Add(await MapAsync(item, ct));
        return Result.Success<IReadOnlyList<DriverReassignmentRequestDto>>(result);
    }

    public async Task<Result<DriverReassignmentRequestDto>> Handle(GetDriverReassignmentRequestQuery request, CancellationToken ct)
    {
        var item = await db.Complaints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.RequestId && !x.IsDeleted && x.Category == Category, ct);
        if (item is null) return Missing();
        if (!Scope(item) && await DriverAsync(ct) != item.DriverProfileId) return Forbidden();
        // A null profile must never authorize a caller with no driver identity.
        if (!Scope(item) && !item.DriverProfileId.HasValue) return Forbidden();
        return Result.Success(await MapAsync(item, ct));
    }

    public Task<Result<DriverReassignmentRequestDto>> Handle(DecideDriverReassignmentCommand request, CancellationToken ct)
        => transactions.ExecuteResultAsync(async token =>
        {
            if (!IsDispatcher) return Result.Failure<DriverReassignmentRequestDto>(Forbidden());
            if (request.Decision is not ("Accepted" or "Rejected") || request.Notes?.Trim().Length > 500
                || request.Decision == "Rejected" && (request.Notes?.Trim().Length ?? 0) < 3)
                return Error.Validation("REASSIGNMENT_DECISION_INVALID", Text("decision_invalid"));
            await locks.LockSupportComplaintAsync(request.RequestId, token);
            var item = await db.Complaints.FirstOrDefaultAsync(x => x.Id == request.RequestId && !x.IsDeleted && x.Category == Category, token);
            if (item is null) return Missing();
            await db.ReloadEntityAsync(item, token);
            if (!Scope(item)) return Forbidden();
            var note = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            if (item.ReassignmentDecision is not null)
                return item.ReassignmentDecision == request.Decision && item.ReassignmentDecisionNotes == note
                    ? Result.Success(await MapAsync(item, token))
                    : Error.Conflict("REASSIGNMENT_ALREADY_DECIDED", Text("already_decided"));
            if (item.Status is ComplaintStatus.Resolved or ComplaintStatus.Rejected or ComplaintStatus.Closed)
                return Error.Conflict("REASSIGNMENT_ALREADY_CLOSED", Text("already_closed"));
            if (await db.DriverSupportHandovers.AnyAsync(x => x.ComplaintId == item.Id, token))
                return Error.Conflict("REASSIGNMENT_HANDOVER_EXISTS", Text("handover_exists"));
            if (request.Decision == "Accepted")
            {
                var stopId = await StopIdAsync(item, token);
                if (!stopId.HasValue) return Error.Conflict("REASSIGNMENT_STOP_UNAVAILABLE", Text("stop_unavailable"));
                var tripId = await db.TripStops.AsNoTracking().Where(x => x.Id == stopId).Select(x => (Guid?)x.TripId).FirstOrDefaultAsync(token);
                if (!tripId.HasValue) return Error.Conflict("REASSIGNMENT_STOP_UNAVAILABLE", Text("stop_unavailable"));
                await locks.LockTripAsync(tripId.Value, token);
                if (!await db.TripStops.AsNoTracking().AnyAsync(x => x.Id == stopId && x.TripId == tripId
                    && x.Trip.RestaurantId == item.RestaurantId && x.Status == TripStopStatus.PickedUp
                    && x.Trip.Status != TripStatus.Completed && x.Trip.Status != TripStatus.Cancelled, token)
                || !await db.DriverAssignments.AnyAsync(x => x.TripId == tripId && x.DriverProfileId == item.DriverProfileId && x.Status == "Active", token)
                || await DriverSupportHandoverMutationGuard.HasActiveHandoverForTripAsync(db, tripId.Value, token)
                || await DriverSupportHandoverMutationGuard.HasActiveHoldForStopAsync(db, stopId.Value, token))
                    return Error.Conflict("REASSIGNMENT_CUSTODY_CHANGED", Text("custody_changed"));
            }
            var now = clock.UtcNow;
            item.ReassignmentDecision = request.Decision;
            item.ReassignmentDecisionNotes = note;
            item.ReassignmentDecidedAtUtc = now;
            item.ReassignmentDecidedByUserId = user.UserId;
            item.Status = request.Decision == "Rejected" ? ComplaintStatus.Resolved : ComplaintStatus.UnderReview;
            RecordDecision(db, item, user.UserId!.Value, now, messages);
            await db.SaveChangesAsync(token);
            return Result.Success(await MapAsync(item, token));
        }, ct);

    internal static void RecordDecision(IAppDbContext db, Complaint item, Guid actor, DateTime now, ILocalizedMessageProvider messages)
    {
        db.AuditLogs.Add(new AuditLog { ActorUserId = actor, Action = "DriverReassignment" + item.ReassignmentDecision,
            EntityType = nameof(Complaint), EntityId = item.Id, Reason = item.ReassignmentDecisionNotes,
            BeforeJson = JsonSerializer.Serialize(new { Decision = (string?)null }),
            AfterJson = JsonSerializer.Serialize(new { item.ReassignmentDecision, item.ReassignmentDecidedAtUtc }),
            SourceChannel = "DispatcherReassignment", OccurredAtUtc = now });
        if (item.RestaurantId is Guid restaurantId)
            db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage { EventType = "driver-support-issue-changed.v1",
                DeduplicationKey = $"driver-reassignment:{item.Id:N}:decision",
                PayloadJson = JsonSerializer.Serialize(new { IssueId = item.Id, RestaurantId = restaurantId,
                    DriverProfileId = item.DriverProfileId, Category = item.Category, Event = "reassignment-" + item.ReassignmentDecision!.ToLowerInvariant() }), CreatedAtUtc = now });
        if (item.DriverProfileId is Guid driverId)
        {
            var profile = db.DriverProfiles.FirstOrDefault(x => x.Id == driverId);
            if (profile is not null) db.DriverNotifications.Add(new DriverNotification { UserId = profile.UserId, DriverProfileId = driverId,
                Type = "driver-reassignment", Section = "support", SubjectId = item.Id,
                DeepLink = $"/driver/reassignment-requests/{item.Id:D}", SentAtUtc = now,
                TitleAr = messages.GetArabicMessage("driver_reassignment.notification_title"), TitleEn = messages.GetEnglishMessage("driver_reassignment.notification_title"),
                BodyAr = messages.GetArabicMessage("driver_reassignment.notification_" + item.ReassignmentDecision!.ToLowerInvariant()),
                BodyEn = messages.GetEnglishMessage("driver_reassignment.notification_" + item.ReassignmentDecision!.ToLowerInvariant()) });
        }
    }

    private async Task<Guid?> StopIdAsync(Complaint c, CancellationToken ct)
    {
        if (c.ReassignmentTripStopId.HasValue) return c.ReassignmentTripStopId;
        // Compatibility: do not invent a stop when legacy order keys are ambiguous.
        var matches = await db.TripStops.AsNoTracking().Where(x => c.OrderId.HasValue && (x.Id == c.OrderId || x.DailyOrderId == c.OrderId)
            && x.Trip.RestaurantId == c.RestaurantId).Select(x => x.Id).Take(2).ToListAsync(ct);
        return matches.Count == 1 ? matches[0] : null;
    }
    private async Task<DriverReassignmentRequestDto> MapAsync(Complaint c, CancellationToken ct)
    {
        var stopId = await StopIdAsync(c, ct);
        var code = stopId.HasValue ? await db.TripStops.AsNoTracking().Where(x => x.Id == stopId).Select(x => x.OrderCode).FirstOrDefaultAsync(ct) : null;
        var h = await db.DriverSupportHandovers.AsNoTracking().Where(x => x.ComplaintId == c.Id).OrderByDescending(x => x.AssignedAtUtc).FirstOrDefaultAsync(ct);
        var decision = c.ReassignmentDecision ?? (h is not null ? "Accepted" : c.Status == ComplaintStatus.Rejected ? "Rejected" : null);
        var status = decision == "Rejected" ? "Rejected"
            : h is null && c.Status is (ComplaintStatus.Resolved or ComplaintStatus.Closed) ? "Closed"
            : decision ?? "ReassignmentRequested";
        return new(c.Id, stopId, code, status, c.ReassignmentReason, c.ReassignmentNotes, c.SubmittedAtUtc,
            decision, c.ReassignmentDecisionNotes, c.ReassignmentDecidedByUserId, c.ReassignmentDecidedAtUtc,
            h?.Id, h?.Status, h?.ReplacementDriverProfileId, h?.ReplacementTripId, c.Latitude, c.Longitude);
    }
}
