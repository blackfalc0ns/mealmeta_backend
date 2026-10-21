using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
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

/// <summary>Driver support handover mutations use the same transaction and trip row lock as assignment/pickup routes.</summary>
public sealed class DriverSupportHandoverHandlers :
    ICommandHandler<CreateDriverSupportHandoverCommand, DriverSupportHandoverDto>,
    ICommandHandler<ArriveDriverSupportHandoverCommand, DriverSupportHandoverDto>,
    ICommandHandler<ScanDriverSupportHandoverBoxCommand, DriverSupportHandoverDto>,
    ICommandHandler<CompleteDriverSupportHandoverCommand, DriverSupportHandoverDto>,
    ICommandHandler<CancelDriverSupportHandoverCommand, DriverSupportHandoverDto>,
    ICommandHandler<ResolveDriverSupportHandoverFollowUpCommand, DriverSupportHandoverDto>,
    IQueryHandler<GetDriverSupportHandoverQuery, DriverSupportHandoverDto>,
    IQueryHandler<GetMyDriverSupportHandoversQuery, IReadOnlyList<DriverSupportHandoverDto>>,
    IQueryHandler<GetDispatcherSupportHandoversQuery, DispatcherSupportHandoversDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDateTimeProvider _clock;
    private readonly IApplicationTransactionExecutor _transactions;
    private readonly IApplicationLockManager _locks;

    public DriverSupportHandoverHandlers(IAppDbContext db, ICurrentUser user, ILocalizedMessageProvider messages,
        IDateTimeProvider clock, IApplicationTransactionExecutor transactions, IApplicationLockManager locks)
        => (_db, _user, _messages, _clock, _transactions, _locks) = (db, user, messages, clock, transactions, locks);

    public Task<Result<DriverSupportHandoverDto>> Handle(CreateDriverSupportHandoverCommand request, CancellationToken ct)
        => _transactions.ExecuteResultAsync(token => CreateAsync(request, token), ct);

    private async Task<Result<DriverSupportHandoverDto>> CreateAsync(CreateDriverSupportHandoverCommand request, CancellationToken ct)
    {
        if (!IsDispatcher() || !_user.UserId.HasValue) return Forbidden();
        if (request.IssueId == Guid.Empty || request.ReplacementDriverId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 100)
            return Validation("INVALID_HANDOVER_REQUEST", "Invalid handover request.", "طلب نقل العهدة غير صالح.");
        var notes = request.Notes?.Trim();
        if (notes?.Length > 500) return Validation("NOTES_TOO_LONG", "Notes must not exceed 500 characters.", "يجب ألا تتجاوز الملاحظات 500 حرف.");

        await _locks.LockSupportHandoverKeyAsync(_user.UserId.Value, request.IdempotencyKey.Trim(), ct);
        var replay = await _db.DriverSupportHandovers.Include(x => x.Boxes).ThenInclude(x => x.FollowUp)
            .FirstOrDefaultAsync(x => x.RequestedByUserId == _user.UserId && x.IdempotencyKey == request.IdempotencyKey.Trim(), ct);
        if (replay is not null)
            return HasRestaurantScope(replay.RestaurantId) && replay.ReplacementDriverProfileId == request.ReplacementDriverId && replay.RequestedIssueId == request.IssueId && replay.Notes == notes
                ? Result.Success(await MapAsync(replay, ct)) : Conflict("IDEMPOTENCY_KEY_REUSED", "The idempotency key was used for another handover.", "مفتاح الطلب مستخدم لعملية أخرى.");

        var complaint = await _db.Complaints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.IssueId && !x.IsDeleted, ct);
        if (complaint is null)
        {
            // Older support cards use the failed TripStop id as their issue key.
            // Promote only a real failed stop with an active custodian and a real
            // customer relationship; never synthesize a customer or driver.
            var failedStop = await _db.TripStops.AsNoTracking().Include(x => x.Trip)
                .FirstOrDefaultAsync(x => x.Id == request.IssueId && x.Status == TripStopStatus.Failed && x.FailureReason != null, ct);
            if (failedStop?.Trip is null) return NotFound();
            if (!HasRestaurantScope(failedStop.Trip.RestaurantId)) return Forbidden();
            await _locks.LockTripAsync(failedStop.TripId, ct);
            failedStop = await _db.TripStops.AsNoTracking().Include(x => x.Trip)
                .FirstOrDefaultAsync(x => x.Id == request.IssueId && x.Status == TripStopStatus.Failed && x.FailureReason != null, ct);
            if (failedStop?.Trip is null) return NotFound();
            var promotedReplay = await _db.DriverSupportHandovers.Include(x => x.Boxes).ThenInclude(x => x.FollowUp)
                .FirstOrDefaultAsync(x => x.TripId == failedStop.TripId && x.RequestedIssueId == request.IssueId, ct);
            if (promotedReplay is not null)
                return HasRestaurantScope(promotedReplay.RestaurantId)
                    && promotedReplay.RequestedByUserId == _user.UserId
                    && promotedReplay.IdempotencyKey == request.IdempotencyKey.Trim()
                    && promotedReplay.ReplacementDriverProfileId == request.ReplacementDriverId
                    && promotedReplay.Notes == notes
                    ? Result.Success(await MapAsync(promotedReplay, ct))
                    : Conflict("HANDOVER_ALREADY_ACTIVE", "This legacy support issue already has a handover.", "يوجد نقل عهدة لهذا البلاغ مسبقاً.");
            var failedAssignments = await _db.DriverAssignments.AsNoTracking()
                .Where(x => x.TripId == failedStop.TripId && (x.Status == "Active" || x.Status == "Assigned")).ToListAsync(ct);
            if (failedAssignments.Count != 1) return Conflict("ORIGINAL_CUSTODY_AMBIGUOUS", "The failed stop has no unambiguous active custodian.", "لا يوجد سائق عهدة حالي واضح لهذا الطلب المتعذر.");
            if (!failedStop.SubscriptionDayId.HasValue) return Conflict("HANDOVER_CUSTOMER_UNAVAILABLE", "The support stop has no persisted customer relationship.", "لا توجد علاقة عميل محفوظة لهذا الطلب.");
            var customerUserId = await _db.SubscriptionDays.Where(x => x.Id == failedStop.SubscriptionDayId.Value)
                .Join(_db.Subscriptions, day => day.SubscriptionId, sub => sub.Id, (_, sub) => sub.CustomerUserId)
                .FirstOrDefaultAsync(ct);
            if (customerUserId == Guid.Empty) return Conflict("HANDOVER_CUSTOMER_UNAVAILABLE", "The support stop has no persisted customer account.", "لا يوجد حساب عميل محفوظ لهذا الطلب.");
            var category = ResolveLegacyCategory(failedStop.FailureReason);
            complaint = new Complaint
            {
                CustomerUserId = customerUserId, RestaurantId = failedStop.Trip.RestaurantId,
                OrderId = failedStop.DailyOrderId ?? failedStop.Id, SubscriptionDayId = failedStop.SubscriptionDayId,
                DriverProfileId = failedAssignments[0].DriverProfileId, Category = category,
                Title = $"{category}: {failedStop.OrderCode}", Description = failedStop.FailureReason,
                Status = ComplaintStatus.Submitted, SubmittedAtUtc = _clock.UtcNow
            };
            _db.Complaints.Add(complaint);
            await _db.SaveChangesAsync(ct);
        }
        var complaintId = complaint.Id;
        await _locks.LockSupportComplaintAsync(complaintId, ct);
        complaint = await _db.Complaints.FirstOrDefaultAsync(x => x.Id == complaintId && !x.IsDeleted, ct);
        if (complaint is null) return NotFound();
        if (complaint.ReassignmentDecision == "Rejected")
            return Conflict("REASSIGNMENT_REJECTED", "The reassignment request was rejected.", "تم رفض طلب إعادة الإسناد.");
        if (complaint.Status is ComplaintStatus.Resolved or ComplaintStatus.Rejected or ComplaintStatus.Closed)
            return Conflict("ISSUE_ALREADY_RESOLVED", "The support issue is already resolved.", "تم حل البلاغ مسبقاً.");
        var stopQuery = _db.TripStops.AsNoTracking().Include(x => x.Trip).ThenInclude(x => x.DriverAssignments)
            .Where(x => complaint.OrderId.HasValue && (x.Id == complaint.OrderId || x.DailyOrderId == complaint.OrderId)
                || complaint.SubscriptionDayId.HasValue && x.SubscriptionDayId == complaint.SubscriptionDayId);
        var stops = await stopQuery.ToListAsync(ct);
        TripStop? stop = null;
        if (complaint.OrderId.HasValue)
            stop = stops.FirstOrDefault(x => x.Id == complaint.OrderId.Value);
        if (stop is null)
        {
            var active = stops.Where(x => x.Status is not (TripStopStatus.Delivered or TripStopStatus.ReturnedToKitchen)
                && x.Trip.Status is not (TripStatus.Completed or TripStatus.Cancelled)).ToList();
            if (complaint.DriverProfileId.HasValue)
            {
                var reporterAssigned = active.Where(x => x.Trip.DriverAssignments.Any(a => a.Status == "Active" && a.DriverProfileId == complaint.DriverProfileId.Value)).ToList();
                if (reporterAssigned.Count == 1) active = reporterAssigned;
            }
            if (active.Count == 1) stop = active[0];
            else if (active.Count > 1) return Conflict("HANDOVER_TRIP_AMBIGUOUS", "The issue matches multiple active delivery trips.", "يرتبط البلاغ بأكثر من رحلة توصيل نشطة.");
        }
        if (stop is null || stop.Trip is null) return Conflict("HANDOVER_TRIP_NOT_FOUND", "The issue is not linked to a delivery trip.", "البلاغ غير مرتبط برحلة توصيل.");
        if (!HasRestaurantScope(stop.Trip.RestaurantId)) return Forbidden();
        await _locks.LockTripAsync(stop.TripId, ct);
        // Re-read after acquiring the lock so concurrent assignments are observable.
        stop = await _db.TripStops.AsNoTracking().Include(x => x.Trip).ThenInclude(x => x.DriverAssignments)
            .FirstOrDefaultAsync(x => x.Id == stop.Id, ct);
        if (stop?.Trip is null) return NotFound();
        if (stop.Trip.Status is TripStatus.Completed or TripStatus.Cancelled)
            return Conflict("TRIP_TERMINAL", "A completed or cancelled trip cannot be handed over.", "لا يمكن نقل عهدة رحلة مكتملة أو ملغاة.");
        var existing = await _db.DriverSupportHandovers.Include(x => x.Boxes).ThenInclude(x => x.FollowUp)
            .FirstOrDefaultAsync(x => x.TripId == stop.TripId && (x.Status == "Assigned" || x.Status == "Arrived" || x.Status == "Receiving"), ct);
        if (existing is not null) return Conflict("HANDOVER_ALREADY_ACTIVE", "This trip already has an active handover.", "يوجد نقل عهدة نشط لهذه الرحلة.");

        var assignments = await _db.DriverAssignments.AsNoTracking()
            .Where(x => x.TripId == stop.TripId && (x.Status == "Active" || x.Status == "Assigned"))
            .OrderByDescending(x => x.AssignedAtUtc).ToListAsync(ct);
        if (assignments.Count == 0) return Conflict("ORIGINAL_DRIVER_NOT_ASSIGNED", "The trip has no active assigned driver.", "لا يوجد سائق نشط معين للرحلة.");
        if (assignments.Select(x => x.DriverProfileId).Distinct().Count() != 1 || assignments.Count > 1)
            return Conflict("ORIGINAL_CUSTODY_AMBIGUOUS", "The trip has multiple current driver assignments; resolve custody before creating a handover.", "للرحلة أكثر من تعيين سائق حالي؛ يجب حسم العهدة قبل نقلها.");
        var assignment = assignments[0];
        var originalDriverId = assignment.DriverProfileId;
        var replacement = await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ReplacementDriverId && !x.IsDeleted, ct);
        if (replacement is null) return Error.NotFound("DRIVER_NOT_FOUND", Local("driver.not_found", "Replacement driver was not found.", "لم يتم العثور على السائق البديل."));
        if (replacement.Id == originalDriverId) return Validation("CANNOT_REASSIGN_TO_SAME_DRIVER", "Choose a different driver.", "يرجى اختيار سائق آخر.");
        await _locks.LockDriverAsync(replacement.Id, ct);
        replacement = await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ReplacementDriverId && !x.IsDeleted, ct);
        if (replacement is null) return NotFound();
        if (!IsEligibleReplacement(replacement))
            return Conflict("REPLACEMENT_DRIVER_UNAVAILABLE", "The replacement driver is not approved and available.", "السائق البديل غير معتمد أو غير متاح.");
        if (!await IsActiveAccountAsync(replacement.UserId, ct)) return Conflict("REPLACEMENT_ACCOUNT_INACTIVE", "The replacement driver's account is not active.", "حساب السائق البديل غير نشط.");
        if (replacement.RestaurantId != stop.Trip.RestaurantId && !await _db.RestaurantDriverLinks.AnyAsync(x => x.RestaurantId == stop.Trip.RestaurantId && x.DriverProfileId == replacement.Id && x.IsActive, ct))
            return Conflict("REPLACEMENT_RESTAURANT_MISMATCH", "The replacement driver is not linked to this restaurant.", "السائق البديل غير مرتبط بهذا المطعم.");
        if (await _db.DriverAssignments.AnyAsync(x => x.DriverProfileId == replacement.Id && (x.Status == "Active" || x.Status == "Assigned"), ct)
            || !await _db.DriverWorkSessions.AnyAsync(x => x.DriverProfileId == replacement.Id && x.Status == "Active" && x.EndedAtUtc == null, ct)
            || await _db.DriverSupportHandovers.AnyAsync(x => x.ReplacementDriverProfileId == replacement.Id && (x.Status == "Assigned" || x.Status == "Arrived" || x.Status == "Receiving"), ct))
            return Conflict("REPLACEMENT_DRIVER_BUSY", "The replacement driver already has active work.", "لدى السائق البديل مهمة نشطة حالياً.");

        decimal? latitude = null;
        decimal? longitude = null;
        var locationSource = "Complaint";
        DateTime? locationRecordedAt = complaint.SubmittedAtUtc;
        if (complaint.Latitude.HasValue && complaint.Longitude.HasValue
            && complaint.Latitude is >= -90 and <= 90 && complaint.Longitude is >= -180 and <= 180)
        {
            latitude = complaint.Latitude;
            longitude = complaint.Longitude;
        }
        else if (originalDriverId != Guid.Empty)
        {
            var point = await _db.DeliveryTrackingPoints.Where(x => x.TripId == stop.TripId && x.DriverProfileId == originalDriverId)
                .OrderByDescending(x => x.RecordedAtUtc).FirstOrDefaultAsync(ct);
            if (point is not null && point.Latitude is >= -90 and <= 90 && point.Longitude is >= -180 and <= 180)
            {
                latitude = point.Latitude; longitude = point.Longitude; locationSource = "DriverTracking";
                locationRecordedAt = point.RecordedAtUtc;
            }
        }
        if (!latitude.HasValue || !longitude.HasValue) return Conflict("PICKUP_LOCATION_UNAVAILABLE", "A verified handover location is unavailable.", "موقع نقل العهدة غير متوفر.");

        var now = _clock.UtcNow;
        var handover = new DriverSupportHandover
        {
            ComplaintId = complaint.Id, RequestedIssueId = request.IssueId, TripId = stop.TripId, OriginalDriverProfileId = originalDriverId,
            ReplacementDriverProfileId = replacement.Id, RestaurantId = stop.Trip.RestaurantId, RequestedByUserId = _user.UserId.Value,
            IdempotencyKey = request.IdempotencyKey.Trim(), Status = "Assigned", Notes = notes,
            PickupLatitude = latitude.Value, PickupLongitude = longitude.Value, PickupLocationSource = locationSource,
            PickupLocationRecordedAtUtc = locationRecordedAt, AssignedAtUtc = now
        };
        var manifest = await _db.TripStops.Where(x => x.TripId == stop.TripId
                && (x.Status == TripStopStatus.PickedUp
                    || (x.Status == TripStopStatus.Failed && x.FailureReason != null
                        && ((complaint.OrderId.HasValue && (x.Id == complaint.OrderId.Value || x.DailyOrderId == complaint.OrderId.Value))
                            || (complaint.SubscriptionDayId.HasValue && x.SubscriptionDayId == complaint.SubscriptionDayId.Value))
                        && _db.OrderHandovers.Any(handover => handover.TripStopId == x.Id
                            && handover.DriverProfileId == originalDriverId && handover.Status == "Completed" && handover.RevokedAtUtc == null))))
            .OrderBy(x => x.Id).ToListAsync(ct);
        if (manifest.Count == 0) return Conflict("HANDOVER_MANIFEST_EMPTY", "No boxes remain in custody for this trip.", "لا توجد صناديق متبقية في عهدة هذه الرحلة.");
        foreach (var item in manifest)
            handover.Boxes.Add(new DriverSupportHandoverBox { TripStopId = item.Id, ManifestCode = item.OrderCode, Status = "Pending" });
        _db.DriverSupportHandovers.Add(handover);
        if (complaint.Category == "DriverReassignmentRequested" && complaint.ReassignmentDecision is null)
        {
            // Existing assign APIs remain compatible: successful assignment atomically accepts the request.
            complaint.ReassignmentDecision = "Accepted";
            complaint.ReassignmentDecidedByUserId = _user.UserId;
            complaint.ReassignmentDecidedAtUtc = now;
            complaint.ReassignmentDecisionNotes = notes;
            DriverReassignmentHandlers.RecordDecision(_db, complaint, _user.UserId.Value, now, _messages);
        }
        complaint.Status = ComplaintStatus.UnderReview;
        await AddNotificationsAsync(handover, "assigned", ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(await MapAsync(handover, ct));
    }

    public Task<Result<DriverSupportHandoverDto>> Handle(ArriveDriverSupportHandoverCommand request, CancellationToken ct)
        => MutateAsync(request.HandoverId, async (h, token) =>
        {
            if (!await IsReplacementDriverAsync(h, token)) return Forbidden<DriverSupportHandoverDto>();
            if (h.Status is "Arrived" or "Receiving" or "Completed") return Result.Success(await MapAsync(h, token));
            if (h.Status != "Assigned") return Conflict("HANDOVER_NOT_ASSIGNABLE", "This handover cannot be arrived.", "لا يمكن تسجيل الوصول لهذا النقل.");
            if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180) return Validation("LOCATION_INVALID", "The location is invalid.", "الموقع غير صالح.");
            h.ArrivalLatitude = request.Latitude; h.ArrivalLongitude = request.Longitude; h.ArrivedAtUtc = _clock.UtcNow; h.Status = "Arrived";
            await AddNotificationsAsync(h, "arrived", token);
            await _db.SaveChangesAsync(token); return Result.Success(await MapAsync(h, token));
        }, ct);

    public Task<Result<DriverSupportHandoverDto>> Handle(ScanDriverSupportHandoverBoxCommand request, CancellationToken ct)
        => MutateAsync(request.HandoverId, async (h, token) =>
        {
            if (!await IsReplacementDriverAsync(h, token)) return Forbidden<DriverSupportHandoverDto>();
            if (h.Status is not ("Arrived" or "Receiving")) return Conflict("HANDOVER_ARRIVAL_REQUIRED", "The replacement driver must arrive first.", "يجب تسجيل وصول السائق البديل أولاً.");
            if (string.IsNullOrWhiteSpace(request.ManifestCode) || string.IsNullOrWhiteSpace(request.ReceiptStatus) || string.IsNullOrWhiteSpace(request.ScanMethod)
                || request.FollowUpNote?.Trim().Length > 500)
                return Validation("SCAN_INVALID", "A scan code, receipt status, and scan method are required.", "رمز المسح وحالة الاستلام وطريقة المسح مطلوبة.");
            var code = NormalizeCode(request.ManifestCode);
            DriverSupportHandoverBox? box = null;
            if (request.ScanMethod == "ManualCode")
            {
                var matches = h.Boxes.Where(x => NormalizeCode(x.ManifestCode) == code).ToList();
                if (matches.Count > 1) return Conflict("MANIFEST_CODE_AMBIGUOUS", "This manifest code identifies more than one box; use the exact barcode.", "رمز القائمة يطابق أكثر من صندوق؛ استخدم الباركود الدقيق.");
                box = matches.SingleOrDefault();
            }
            else if (request.ScanMethod == "Barcode")
            {
                var matches = new List<DriverSupportHandoverBox>();
                foreach (var item in h.Boxes)
                {
                    var stop = await _db.TripStops.FirstOrDefaultAsync(x => x.Id == item.TripStopId, token);
                    if (stop?.MealBarcodeId is not Guid barcodeId) continue;
                    if (await _db.MealBarcodes.AnyAsync(x => x.Id == barcodeId && !x.IsRevoked
                        && x.Status != MealBarcodeStatus.Revoked && x.Status != MealBarcodeStatus.Archived
                        && x.BarcodeValue == request.ManifestCode, token)) matches.Add(item);
                }
                if (matches.Count > 1) return Conflict("MANIFEST_BARCODE_AMBIGUOUS", "This barcode identifies more than one box on the handover.", "هذا الباركود يطابق أكثر من صندوق في عملية النقل.");
                box = matches.SingleOrDefault();
            }
            else return Validation("SCAN_METHOD_INVALID", "Scan method must be Barcode or ManualCode.", "طريقة المسح يجب أن تكون باركود أو إدخالاً يدوياً.");
            if (box is null) return Validation("MANIFEST_BOX_NOT_FOUND", "The code does not match a box on this handover manifest.", "الرمز لا يطابق صندوقاً في قائمة نقل العهدة.");
            var receipt = request.ReceiptStatus.Trim();
            if (receipt is not ("Received" or "Missing" or "Damaged")) return Validation("RECEIPT_STATUS_INVALID", "Receipt status must be Received, Missing, or Damaged.", "حالة الاستلام غير صالحة.");
            if (box.Status != "Pending") return box.Status == receipt ? Result.Success(await MapAsync(h, token)) : Conflict("BOX_ALREADY_RECORDED", "This box already has receipt evidence.", "تم تسجيل دليل استلام هذا الصندوق مسبقاً.");
            var note = request.FollowUpNote?.Trim();
            if (receipt != "Received" && string.IsNullOrWhiteSpace(note)) return Validation("FOLLOW_UP_REQUIRED", "Missing or damaged boxes require a follow-up note.", "الصندوق المفقود أو التالف يتطلب ملاحظة متابعة.");
            if (request.ScanMethod == "Barcode")
            {
                var stop = await _db.TripStops.FirstOrDefaultAsync(x => x.Id == box.TripStopId, token);
                if (stop?.MealBarcodeId is not Guid barcodeId) return Validation("BARCODE_NOT_ASSIGNED", "This manifest box has no active persisted barcode.", "لا يوجد رمز باركود محفوظ لهذا الصندوق.");
                var barcode = await _db.MealBarcodes.FirstOrDefaultAsync(x => x.Id == barcodeId && !x.IsRevoked
                    && x.Status != MealBarcodeStatus.Archived && x.Status != MealBarcodeStatus.Revoked && x.BarcodeValue == request.ManifestCode, token);
                if (barcode is null) return Validation("BARCODE_INVALID", "The barcode is not assigned to this manifest box.", "الباركود غير مرتبط بهذا الصندوق.");
            }
            box.Status = receipt; box.ScanMethod = request.ScanMethod; box.ScannedAtUtc = _clock.UtcNow; box.FollowUpNote = note;
            h.Status = "Receiving";
            AddHandoverOutbox(h, $"box-{receipt.ToLowerInvariant()}", $"driver-support-handover:{h.Id:N}:box:{box.Id:N}:{receipt}", new { BoxId = box.Id, ReceiptStatus = receipt });
            if (receipt is "Missing" or "Damaged") await NotifyDispatchFollowUpAsync(h, token);
            await _db.SaveChangesAsync(token); return Result.Success(await MapAsync(h, token));
        }, ct);

    public Task<Result<DriverSupportHandoverDto>> Handle(CompleteDriverSupportHandoverCommand request, CancellationToken ct)
        => MutateAsync(request.HandoverId, async (h, token) =>
        {
            if (!await IsReplacementDriverAsync(h, token)) return Forbidden<DriverSupportHandoverDto>();
            if (h.Status == "Completed") return Result.Success(await MapAsync(h, token));
            if (h.Status is not ("Arrived" or "Receiving") || h.ArrivedAtUtc is null) return Conflict("HANDOVER_ARRIVAL_REQUIRED", "Arrival and box receipt are required before completion.", "يلزم تسجيل الوصول واستلام الصناديق قبل الإكمال.");
            if (h.Boxes.Any(x => x.Status == "Pending")) return Conflict("HANDOVER_RECEIPTS_INCOMPLETE", "Record every box as Received, Missing, or Damaged.", "سجل كل صندوق كمستلم أو مفقود أو تالف.");
            if (await _db.DriverAssignments.AnyAsync(x => x.DriverProfileId == h.ReplacementDriverProfileId
                && (x.Status == "Active" || x.Status == "Assigned"), token))
                return Conflict("REPLACEMENT_DRIVER_BUSY", "The replacement driver has acquired another active assignment; dispatch must reconcile before transfer.", "أصبح للسائق البديل تعيين نشط آخر؛ يجب على التوزيع تسوية ذلك قبل النقل.");
            var trip = await _db.Trips.FirstOrDefaultAsync(x => x.Id == h.TripId, token);
            if (trip is null) return NotFound();
            foreach (var item in h.Boxes.Where(x => x.Status is "Missing" or "Damaged"))
            {
                var stop = await _db.TripStops.FirstOrDefaultAsync(x => x.Id == item.TripStopId, token);
                if (stop is null) return Conflict("HANDOVER_BOX_MISSING", "A manifest stop no longer exists.", "لم يعد بند الصندوق موجوداً في قائمة الرحلة.");
                var hold = await _db.DeliveryHolds.FirstOrDefaultAsync(x => x.DriverSupportHandoverBoxId == item.Id, token);
                if (hold is null)
                {
                    hold = new DeliveryHold { TripId = stop.TripId, TripStopId = stop.Id, OrderId = stop.DailyOrderId,
                        SubscriptionDayId = stop.SubscriptionDayId, DriverSupportHandoverBoxId = item.Id,
                        Reason = $"Support handover {h.Id:D}: {item.Status}", HeldAtUtc = _clock.UtcNow, Status = "Active",
                        ActorUserId = _user.UserId, Notes = item.FollowUpNote };
                    _db.DeliveryHolds.Add(hold);
                    item.FollowUp = hold;
                    item.FollowUpId = hold.Id;
                }
            }
            var receivedIds = h.Boxes.Where(x => x.Status == "Received").Select(x => x.TripStopId).ToHashSet();
            var receivedStops = await _db.TripStops.Where(x => receivedIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(token);
            if (receivedStops.Any(x => x.Status is TripStopStatus.Delivered or TripStopStatus.ReturnedToKitchen)) return Conflict("TERMINAL_BOX_CANNOT_TRANSFER", "A delivered or returned box cannot be transferred.", "لا يمكن نقل صندوق تم تسليمه أو إرجاعه للمطبخ.");
            if (receivedStops.Count > 0)
            {
                // Keep terminal history on the original trip. The replacement trip contains
                // only the exact boxes the replacement driver accepted into custody.
                var replacementTrip = new Trip { RestaurantId = trip.RestaurantId, DispatcherUserId = _user.UserId,
                    Code = $"HO-{Guid.NewGuid():N}", ShiftName = trip.ShiftName, PlannedDate = trip.PlannedDate, Status = TripStatus.Assigned,
                    Notes = $"Support handover {h.Id:N}" };
                _db.Trips.Add(replacementTrip);
                h.ReplacementTripId = replacementTrip.Id;
                foreach (var stop in receivedStops) { stop.TripId = replacementTrip.Id; stop.Trip = replacementTrip; stop.FailureReason = null; if (stop.Status != TripStopStatus.PickedUp) stop.Status = TripStopStatus.PickedUp; }
                _db.DriverAssignments.Add(new DriverAssignment { TripId = replacementTrip.Id, DriverProfileId = h.ReplacementDriverProfileId, AssignedAtUtc = _clock.UtcNow, Status = "Active" });
            }
            foreach (var assignment in await _db.DriverAssignments.Where(x => x.TripId == h.TripId && (x.Status == "Active" || x.Status == "Assigned")).ToListAsync(token))
                assignment.Status = receivedIds.Count == h.Boxes.Count ? "CustodyTransferred" : "CustodyBlocked";
            h.Status = "Completed"; h.CompletedAtUtc = _clock.UtcNow;
            await AddNotificationsAsync(h, "completed", token);
            await _db.SaveChangesAsync(token); return Result.Success(await MapAsync(h, token));
        }, ct);

    public Task<Result<DriverSupportHandoverDto>> Handle(CancelDriverSupportHandoverCommand request, CancellationToken ct)
        => MutateAsync(request.HandoverId, async (h, token) =>
        {
            if (!IsDispatcher() && !await IsReplacementDriverAsync(h, token) && !await IsOriginalDriverAsync(h, token)) return Forbidden<DriverSupportHandoverDto>();
            if (h.Status == "Cancelled") return Result.Success(await MapAsync(h, token));
            if (h.Status == "Completed") return Conflict("HANDOVER_ALREADY_COMPLETED", "A completed handover cannot be cancelled.", "لا يمكن إلغاء نقل مكتمل.");
            if (h.Boxes.Any(x => x.Status != "Pending")) return Conflict("HANDOVER_RECEIPT_EXISTS", "A handover with receipt evidence cannot be cancelled; finish it with missing or damaged outcomes.", "لا يمكن إلغاء نقل يحتوي على دليل استلام؛ أكمله مع حالة مفقود أو تالف.");
            if (request.Reason?.Trim().Length > 500) return Validation("CANCELLATION_REASON_TOO_LONG", "The cancellation reason cannot exceed 500 characters.", "يجب ألا يتجاوز سبب الإلغاء 500 حرف.");
            h.Status = "Cancelled"; h.CancelledAtUtc = _clock.UtcNow;
            h.CancellationReason = request.Reason?.Trim();
            await AddNotificationsAsync(h, "cancelled", token);
            await _db.SaveChangesAsync(token); return Result.Success(await MapAsync(h, token));
        }, ct);

    public Task<Result<DriverSupportHandoverDto>> Handle(ResolveDriverSupportHandoverFollowUpCommand request, CancellationToken ct)
        => MutateAsync(request.HandoverId, async (h, token) =>
        {
            if (!IsDispatcher()) return Forbidden<DriverSupportHandoverDto>();
            if (h.Status != "Completed") return Conflict("HANDOVER_NOT_COMPLETED", "Follow-up can be resolved only after handover completion.", "لا يمكن إغلاق المتابعة قبل اكتمال نقل العهدة.");
            var note = request.ResolutionNote?.Trim();
            var code = request.ManifestCode?.Trim();
            if (string.IsNullOrWhiteSpace(note) || note.Length is < 5 or > 500 || string.IsNullOrWhiteSpace(code) || code.Length > 100)
                return Validation("FOLLOW_UP_RESOLUTION_INVALID", "Provide an exact manifest code and a resolution note between 5 and 500 characters.", "أدخل رمز القائمة الدقيق وملاحظة تسوية من 5 إلى 500 حرف.");
            var box = h.Boxes.SingleOrDefault(x => x.TripStopId == request.TripStopId);
            if (box is null) return NotFound<DriverSupportHandoverDto>();
            if (box.Status is not ("Missing" or "Damaged")) return Conflict("FOLLOW_UP_NOT_REQUIRED", "Only a recorded missing or damaged box has a support follow-up.", "توجد متابعة فقط للصندوق المسجل كمفقود أو تالف.");
            var hold = await _db.DeliveryHolds.FirstOrDefaultAsync(x => x.DriverSupportHandoverBoxId == box.Id, token);
            if (hold is null) return Conflict("FOLLOW_UP_RECORD_NOT_FOUND", "The persisted support follow-up record is missing.", "سجل متابعة العهدة غير موجود.");
            var stop = await _db.TripStops.FirstOrDefaultAsync(x => x.Id == box.TripStopId, token);
            if (stop is null) return Conflict("HANDOVER_BOX_MISSING", "The manifest stop no longer exists.", "لم يعد بند الصندوق موجوداً في قائمة الرحلة.");
            if (stop.TripId != h.TripId)
                return Conflict("FOLLOW_UP_CUSTODY_CHANGED", "The box moved outside this handover's trip history.", "انتقل الصندوق خارج سجل رحلة نقل العهدة.");
            var supplied = NormalizeCode(code);
            var manualMatches = supplied == NormalizeCode(box.ManifestCode)
                && h.Boxes.Count(x => NormalizeCode(x.ManifestCode) == supplied) == 1;
            var barcodeMatches = stop.MealBarcodeId.HasValue && await _db.MealBarcodes.AnyAsync(x => x.Id == stop.MealBarcodeId.Value
                && !x.IsRevoked && x.Status != MealBarcodeStatus.Revoked && x.Status != MealBarcodeStatus.Archived
                && x.BarcodeValue == code, token);
            if (!manualMatches && !barcodeMatches)
                return Validation("FOLLOW_UP_CODE_MISMATCH", "The code does not match the persisted barcode or exact manifest code for this box.", "الرمز لا يطابق الباركود المحفوظ أو رمز القائمة الدقيق لهذا الصندوق.");
            if (hold.ResolvedAtUtc.HasValue || hold.ReleasedAtUtc.HasValue || hold.Status != "Active")
                return stop.Status == TripStopStatus.ReturnedToKitchen && hold.ResolutionReason == note
                    ? Result.Success(await MapAsync(h, token))
                    : Conflict("FOLLOW_UP_ALREADY_RESOLVED", "This follow-up was resolved with different evidence.", "تمت تسوية هذه المتابعة بدليل مختلف.");
            if (stop.Status is TripStopStatus.Delivered or TripStopStatus.ReturnedToKitchen)
                return Conflict("FOLLOW_UP_STOP_TERMINAL", "A delivered or already-returned box cannot be returned again.", "لا يمكن إرجاع صندوق تم تسليمه أو إرجاعه مسبقاً.");
            var previousStopStatus = stop.Status;
            stop.Status = TripStopStatus.ReturnedToKitchen;
            hold.Status = "Resolved";
            hold.ResolvedAtUtc = _clock.UtcNow;
            hold.ReleasedAtUtc = _clock.UtcNow;
            hold.ResolutionReason = note;
            hold.ModifiedByUserId = _user.UserId;
            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = _user.UserId, Action = "SupportHandoverFollowUpReturnedToKitchen",
                EntityType = nameof(DeliveryHold), EntityId = hold.Id,
                BeforeJson = JsonSerializer.Serialize(new { HoldStatus = "Active", ReceiptStatus = box.Status, StopStatus = previousStopStatus }),
                AfterJson = JsonSerializer.Serialize(new { HoldStatus = hold.Status, StopStatus = stop.Status, ResolvedAtUtc = hold.ResolvedAtUtc }),
                Reason = note, SourceChannel = "DispatcherSupportHandover", OccurredAtUtc = _clock.UtcNow
            });
            AddHandoverOutbox(h, "follow-up-returned", $"driver-support-handover:{h.Id:N}:follow-up:{box.Id:N}:resolved", new { BoxId = box.Id, TripStopId = stop.Id, Status = "ReturnedToKitchen" });
            await _db.SaveChangesAsync(token);
            return Result.Success(await MapAsync(h, token));
        }, ct);

    private async Task<Result<DriverSupportHandoverDto>> MutateAsync(Guid id, Func<DriverSupportHandover, CancellationToken, Task<Result<DriverSupportHandoverDto>>> action, CancellationToken ct)
        => await _transactions.ExecuteResultAsync(async token =>
        {
            var initial = await _db.DriverSupportHandovers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, token);
            if (initial is null) return NotFound<DriverSupportHandoverDto>();
            if (!HasRestaurantScope(initial.RestaurantId) && !await IsDriverOnHandoverAsync(initial, token)) return Forbidden<DriverSupportHandoverDto>();
            await _locks.LockTripAsync(initial.TripId, token);
            await _locks.LockDriverAsync(initial.ReplacementDriverProfileId, token);
            await _locks.LockSupportHandoverAsync(id, token);
            var handover = await _db.DriverSupportHandovers.Include(x => x.Boxes).ThenInclude(x => x.FollowUp).FirstOrDefaultAsync(x => x.Id == id, token);
            if (handover is null) return NotFound<DriverSupportHandoverDto>();
            if (handover.Status is "Assigned" or "Arrived" or "Receiving")
            {
                var originalStillOwnsTrip = await _db.DriverAssignments.AnyAsync(x => x.TripId == handover.TripId
                    && x.DriverProfileId == handover.OriginalDriverProfileId && (x.Status == "Active" || x.Status == "Assigned"), token);
                if (!originalStillOwnsTrip) return Conflict("ORIGINAL_CUSTODY_CHANGED", "The original driver no longer owns this trip; dispatch must reconcile custody.", "لم يعد السائق الأصلي مسؤولاً عن الرحلة؛ يجب على التوزيع تسوية العهدة.");
            }
            return await action(handover, token);
        }, ct);

    public async Task<Result<DriverSupportHandoverDto>> Handle(GetDriverSupportHandoverQuery request, CancellationToken ct)
    {
        var handover = await _db.DriverSupportHandovers.AsNoTracking().Include(x => x.Boxes).ThenInclude(x => x.FollowUp).FirstOrDefaultAsync(x => x.Id == request.HandoverId, ct);
        if (handover is null) return NotFound<DriverSupportHandoverDto>();
        if (!HasRestaurantScope(handover.RestaurantId) && !await IsDriverOnHandoverAsync(handover, ct)) return Forbidden<DriverSupportHandoverDto>();
        return Result.Success(await MapAsync(handover, ct));
    }

    public async Task<Result<IReadOnlyList<DriverSupportHandoverDto>>> Handle(GetMyDriverSupportHandoversQuery request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated || _user.UserType != UserType.Driver || !_user.UserId.HasValue) return Forbidden<IReadOnlyList<DriverSupportHandoverDto>>();
        if (!(await _db.GetActiveAccountUserIdsAsync(new[] { _user.UserId.Value }, ct)).Contains(_user.UserId.Value))
            return Error.Forbidden("DRIVER_ACCOUNT_INACTIVE", _messages.GetMessage("driver.account_inactive", "The driver account is not active."));
        var driverId = await _db.DriverProfiles.Where(x => x.UserId == _user.UserId && !x.IsDeleted).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (!driverId.HasValue) return Result.Success<IReadOnlyList<DriverSupportHandoverDto>>(Array.Empty<DriverSupportHandoverDto>());
        var query = _db.DriverSupportHandovers.AsNoTracking().Include(x => x.Boxes).ThenInclude(x => x.FollowUp)
            .Where(x => x.OriginalDriverProfileId == driverId || x.ReplacementDriverProfileId == driverId);
        if (string.Equals(request.Status, "active", StringComparison.OrdinalIgnoreCase)) query = query.Where(x => x.Status == "Assigned" || x.Status == "Arrived" || x.Status == "Receiving");
        else if (!string.IsNullOrWhiteSpace(request.Status)) query = query.Where(x => x.Status == request.Status);
        var items = await query.OrderByDescending(x => x.AssignedAtUtc).ToListAsync(ct);
        var mapped = new List<DriverSupportHandoverDto>(items.Count);
        foreach (var item in items) mapped.Add(await MapAsync(item, ct));
        return Result.Success<IReadOnlyList<DriverSupportHandoverDto>>(mapped);
    }

    public async Task<Result<DispatcherSupportHandoversDto>> Handle(GetDispatcherSupportHandoversQuery request, CancellationToken ct)
    {
        if (!IsDispatcher()) return Forbidden<DispatcherSupportHandoversDto>();
        var scope = _user.RestaurantId;
        if (request.RestaurantId.HasValue && request.RestaurantId != scope && _user.UserType != UserType.Admin) return Forbidden<DispatcherSupportHandoversDto>();
        if (_user.UserType == UserType.DeliveryManager && !scope.HasValue) return Forbidden<DispatcherSupportHandoversDto>();
        var query = _db.DriverSupportHandovers.AsNoTracking().Include(x => x.Boxes).ThenInclude(x => x.FollowUp).AsQueryable();
        var restaurant = _user.UserType == UserType.Admin ? request.RestaurantId : scope;
        if (restaurant.HasValue) query = query.Where(x => x.RestaurantId == restaurant.Value);
        if (!string.IsNullOrWhiteSpace(request.Status)) query = query.Where(x => x.Status == request.Status);
        var count = await query.CountAsync(ct); var page = Math.Max(1, request.PageNumber); var size = Math.Clamp(request.PageSize, 1, 100);
        var items = await query.OrderByDescending(x => x.AssignedAtUtc).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var mapped = new List<DriverSupportHandoverDto>(items.Count);
        foreach (var item in items) mapped.Add(await MapAsync(item, ct));
        return Result.Success(new DispatcherSupportHandoversDto(mapped, count, page, size));
    }

    private bool IsDispatcher() => _user.IsAuthenticated && _user.UserId.HasValue && _user.UserType is UserType.DeliveryManager or UserType.Admin;
    private bool HasRestaurantScope(Guid id) => IsDispatcher() && (_user.UserType == UserType.Admin || _user.RestaurantId == id);
    private async Task<bool> IsDriverOnHandoverAsync(DriverSupportHandover h, CancellationToken ct) => _user.IsAuthenticated && _user.UserType == UserType.Driver && (await IsReplacementDriverAsync(h, ct) || await IsOriginalDriverAsync(h, ct));
    private async Task<bool> IsReplacementDriverAsync(DriverSupportHandover h, CancellationToken ct)
    {
        if (!_user.IsAuthenticated || _user.UserType != UserType.Driver || !_user.UserId.HasValue) return false;
        var driver = await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == h.ReplacementDriverProfileId
            && d.UserId == _user.UserId.Value && !d.IsDeleted && d.VerificationStatus == VerificationStatus.Approved, ct);
        return driver is not null && IsEligibleReplacement(driver) && await IsActiveAccountAsync(driver.UserId, ct);
    }
    private async Task<bool> IsOriginalDriverAsync(DriverSupportHandover h, CancellationToken ct)
    {
        if (!_user.IsAuthenticated || _user.UserType != UserType.Driver || !_user.UserId.HasValue) return false;
        var driver = await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == h.OriginalDriverProfileId
            && d.UserId == _user.UserId.Value && !d.IsDeleted, ct);
        return driver is not null && await IsActiveAccountAsync(driver.UserId, ct);
    }
    private async Task<bool> IsActiveAccountAsync(Guid userId, CancellationToken ct) => (await _db.GetActiveAccountUserIdsAsync(new[] { userId }, ct)).Contains(userId);
    private bool IsEligibleReplacement(DriverProfile driver)
    {
        var now = _clock.UtcNow;
        return driver.IsAvailable && driver.VerificationStatus == VerificationStatus.Approved
            && (!driver.LicenseExpiryUtc.HasValue || driver.LicenseExpiryUtc.Value > now)
            && (!driver.NationalIdExpiryUtc.HasValue || driver.NationalIdExpiryUtc.Value > now)
            && (!driver.VehicleRegistrationExpiryUtc.HasValue || driver.VehicleRegistrationExpiryUtc.Value > now)
            && (!driver.ContractExpiryUtc.HasValue || driver.ContractExpiryUtc.Value > now);
    }
    private static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().TrimStart('#').Trim();
    private static string ResolveLegacyCategory(string failureReason)
    {
        var value = failureReason.TrimStart().TrimStart('[');
        var close = value.IndexOf(']');
        if (close > 0)
        {
            var category = value[..close].Trim();
            if (category is "AddressIssue" or "SevereDelay" or "RoadIssue" or "VehicleBreakdown" or "TrafficAccident" or "SevereWeather")
                return category;
        }
        return "DeliveryIssue";
    }
    private async Task<DriverSupportHandoverDto> MapAsync(DriverSupportHandover h, CancellationToken ct)
    {
        var complaint = await _db.Complaints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == h.ComplaintId, ct);
        var tripCode = await _db.Trips.AsNoTracking().Where(x => x.Id == h.TripId).Select(x => x.Code).FirstOrDefaultAsync(ct);
        var drivers = await _db.DriverProfiles.AsNoTracking().Where(x => x.Id == h.OriginalDriverProfileId || x.Id == h.ReplacementDriverProfileId).ToListAsync(ct);
        var original = drivers.FirstOrDefault(x => x.Id == h.OriginalDriverProfileId);
        var replacement = drivers.FirstOrDefault(x => x.Id == h.ReplacementDriverProfileId);
        var originalName = original is null ? null : (_messages.IsArabic() ? original.FullNameAr : original.FullNameEn);
        var replacementName = replacement is null ? null : (_messages.IsArabic() ? replacement.FullNameAr : replacement.FullNameEn);
        var originalPhone = original is null ? null : await _db.GetUserPhoneAsync(original.UserId, ct);
        var replacementPhone = replacement is null ? null : await _db.GetUserPhoneAsync(replacement.UserId, ct);
        return new DriverSupportHandoverDto(h.Id, h.ComplaintId, h.TripId, h.OriginalDriverProfileId, h.ReplacementDriverProfileId,
            h.RestaurantId, h.Status, h.PickupLatitude, h.PickupLongitude, h.AssignedAtUtc, h.ArrivedAtUtc, h.CompletedAtUtc,
            h.Boxes.OrderBy(x => x.ManifestCode).Select(x => new DriverSupportHandoverBoxDto(x.TripStopId, x.ManifestCode, x.Status,
                x.Status == "Received" ? x.ScannedAtUtc : null, x.FollowUpNote, x.FollowUpId, x.FollowUp?.Status, x.ScannedAtUtc)).ToArray(),
            h.ReplacementTripId, h.Boxes.Count(x => x.Status == "Received"), h.Boxes.Count(x => x.Status == "Missing"),
            h.Boxes.Count(x => x.Status == "Damaged"), h.Boxes.Count(x => x.Status == "Pending"), h.PickupLocationSource,
            h.PickupLocationRecordedAtUtc, complaint?.Title, complaint?.Category, complaint?.Description, tripCode,
            originalName, originalPhone, replacementName, replacementPhone);
    }
    private string Local(string key, string en, string ar) => _messages.GetMessage(key, _messages.IsArabic() ? ar : en);
    private Result<DriverSupportHandoverDto> Forbidden() => Forbidden<DriverSupportHandoverDto>();
    private Result<T> Forbidden<T>() => Error.Forbidden("HANDOVER_FORBIDDEN", Local("driver_support_handover.forbidden", "You cannot access this handover.", "لا تملك صلاحية الوصول إلى نقل العهدة هذا."));
    private Result<DriverSupportHandoverDto> NotFound() => NotFound<DriverSupportHandoverDto>();
    private Result<T> NotFound<T>() => Error.NotFound("HANDOVER_NOT_FOUND", Local("driver_support_handover.not_found", "The support issue or handover was not found.", "لم يتم العثور على البلاغ أو نقل العهدة."));
    private Result<DriverSupportHandoverDto> Validation(string code, string en, string ar) => Error.Validation(code, Local($"driver_support_handover.{code.ToLowerInvariant()}", en, ar));
    private Result<DriverSupportHandoverDto> Conflict(string code, string en, string ar) => Error.Conflict(code, Local($"driver_support_handover.{code.ToLowerInvariant()}", en, ar));
    private Result<T> Validation<T>(string code, string en, string ar) => Error.Validation(code, Local($"driver_support_handover.{code.ToLowerInvariant()}", en, ar));
    private Result<T> Conflict<T>(string code, string en, string ar) => Error.Conflict(code, Local($"driver_support_handover.{code.ToLowerInvariant()}", en, ar));

    private async Task AddNotificationsAsync(DriverSupportHandover handover, string eventName, CancellationToken ct)
    {
        var replacement = await _db.DriverProfiles.FirstOrDefaultAsync(x => x.Id == handover.ReplacementDriverProfileId, ct);
        var original = await _db.DriverProfiles.FirstOrDefaultAsync(x => x.Id == handover.OriginalDriverProfileId, ct);
        var titleAr = _messages.GetArabicMessage("driver_support_handover.notification_title", "نقل عهدة السائق");
        var titleEn = _messages.GetEnglishMessage("driver_support_handover.notification_title_en", "Driver support handover");
        var bodyAr = _messages.GetArabicMessage($"driver_support_handover.notification_{eventName}", "تم تحديث نقل العهدة.");
        var bodyEn = _messages.GetEnglishMessage($"driver_support_handover.notification_{eventName}_en", "The driver support handover was updated.");
        if (replacement is not null) _db.DriverNotifications.Add(new DriverNotification { UserId = replacement.UserId, DriverProfileId = replacement.Id, Type = "support-handover", TitleAr = titleAr, TitleEn = titleEn, BodyAr = bodyAr, BodyEn = bodyEn, Section = "support", SubjectId = handover.Id, DeepLink = $"/driver/support-handovers/{handover.Id:D}", SentAtUtc = _clock.UtcNow });
        if (original is not null) _db.DriverNotifications.Add(new DriverNotification { UserId = original.UserId, DriverProfileId = original.Id, Type = "support-handover", TitleAr = titleAr, TitleEn = titleEn, BodyAr = bodyAr, BodyEn = bodyEn, Section = "support", SubjectId = handover.Id, DeepLink = $"/driver/support-handovers/{handover.Id:D}", SentAtUtc = _clock.UtcNow });
        AddHandoverOutbox(handover, eventName, $"driver-support-handover:{handover.Id:N}:{eventName}", new { });
    }

    private void AddHandoverOutbox(DriverSupportHandover handover, string eventName, string deduplicationKey, object extra)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["HandoverId"] = handover.Id, ["TripId"] = handover.TripId, ["ReplacementTripId"] = handover.ReplacementTripId,
            ["RestaurantId"] = handover.RestaurantId, ["OriginalDriverProfileId"] = handover.OriginalDriverProfileId,
            ["ReplacementDriverProfileId"] = handover.ReplacementDriverProfileId, ["Status"] = handover.Status, ["Event"] = eventName
        };
        foreach (var property in extra.GetType().GetProperties()) envelope[property.Name] = property.GetValue(extra);
        _db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage
        {
            EventType = "driver-support-handover-changed.v1", AggregateType = nameof(DriverSupportHandover), AggregateId = handover.Id.ToString("D"),
            DeduplicationKey = deduplicationKey, PayloadJson = JsonSerializer.Serialize(envelope), CreatedAtUtc = _clock.UtcNow
        });
    }

    private async Task NotifyDispatchFollowUpAsync(DriverSupportHandover handover, CancellationToken cancellationToken)
    {
        var managers = await _db.GetDeliveryManagerUserIdsAsync(handover.RestaurantId, cancellationToken);
        foreach (var userId in managers)
            DeliveryManagerSupportPushOutbox.Add(_db, new DeliveryManagerNotification { UserId = userId, RestaurantId = handover.RestaurantId, Type = "driver-support-handover", TitleAr = _messages.GetArabicMessage("driver_support_handover.notification_title", "نقل عهدة السائق"), TitleEn = _messages.GetEnglishMessage("driver_support_handover.notification_title_en", "Driver support handover"), BodyAr = _messages.GetArabicMessage("driver_support_handover.notification_dispatch_followup", "نقل العهدة يحتاج متابعة من فريق التوزيع."), BodyEn = _messages.GetEnglishMessage("driver_support_handover.notification_dispatch_followup_en", "The handover needs dispatch follow-up."), Section = "support", SubjectId = handover.Id, DeepLink = $"/dispatcher/support-handovers/{handover.Id:D}", SentAtUtc = _clock.UtcNow });
    }
}
