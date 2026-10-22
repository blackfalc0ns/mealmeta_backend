using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Result = MealMate.Application.Common.Models.Result;

namespace MealMate.Application.Features.AdminCapacity;

public sealed class GetAdminCapacityOverviewQueryHandler
    : IQueryHandler<GetAdminCapacityOverviewQuery, AdminCapacityOverviewResponseDto>
{
    private readonly IAppDbContext _db;

    public GetAdminCapacityOverviewQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<AdminCapacityOverviewResponseDto>> Handle(
        GetAdminCapacityOverviewQuery request,
        CancellationToken cancellationToken)
    {
        var targetDate = request.ServiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Get or create active platform policy
        var policy = await _db.PlatformCapacityPolicies
            .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

        if (policy is null)
        {
            policy = PlatformCapacityPolicy.Create(
                5000,
                15.0m,
                targetDate,
                1,
                "Initialized default platform capacity policy").Value;

            _db.PlatformCapacityPolicies.Add(policy);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var policyDto = new AdminPlatformCapacityPolicyDto(
            policy.PlatformDailyBoxCeiling,
            policy.RenewalReserveMinimumPercent,
            3,
            72,
            (policy.ModifiedAtUtc ?? policy.CreatedAtUtc).ToString("o")
        );

        // 2. Fetch restaurants
        var restaurants = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => !r.IsArchived)
            .OrderBy(r => r.LegalNameAr)
            .ToListAsync(cancellationToken);

        // Fetch regions for area name mapping
        var regions = await _db.Regions
            .AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r.NameAr, cancellationToken);

        // Fetch capacity policies
        var capacityPolicies = await _db.RestaurantCapacityPolicies
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.RestaurantId, cancellationToken);

        // Fetch daily capacities for target date
        var dailyCaps = await _db.DailyCapacities
            .AsNoTracking()
            .Where(c => c.ServiceDate == targetDate)
            .ToDictionaryAsync(c => c.RestaurantId, cancellationToken);

        // Fetch commitments for target date
        var commitments = await _db.ServiceCapacityCommitments
            .AsNoTracking()
            .Where(c => c.ServiceDate == targetDate && (c.Status == CapacityCommitmentStatus.Reserved || c.Status == CapacityCommitmentStatus.Confirmed))
            .ToListAsync(cancellationToken);

        var commitmentsByRestaurant = commitments
            .Where(c => c.RestaurantId.HasValue)
            .GroupBy(c => c.RestaurantId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Fetch subscription days counts by status from database
        var subscriptionDaysCounts = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(d => d.ServiceDate == targetDate && d.Status != SubscriptionDayStatus.Cancelled && d.RestaurantId.HasValue)
            .GroupBy(d => d.RestaurantId!.Value)
            .Select(g => new { RestaurantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RestaurantId, x => x.Count, cancellationToken);

        var confirmedSubDaysCounts = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(d => d.ServiceDate == targetDate && d.RestaurantId.HasValue &&
                        (d.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                         d.Status == SubscriptionDayStatus.Preparing ||
                         d.Status == SubscriptionDayStatus.OutForDelivery ||
                         d.Status == SubscriptionDayStatus.Delivered))
            .GroupBy(d => d.RestaurantId!.Value)
            .Select(g => new { RestaurantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RestaurantId, x => x.Count, cancellationToken);

        var pendingSubDaysCounts = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(d => d.ServiceDate == targetDate && d.RestaurantId.HasValue &&
                        (d.Status == SubscriptionDayStatus.Locked72h ||
                         d.Status == SubscriptionDayStatus.SentToRestaurant ||
                         d.Status == SubscriptionDayStatus.AutoSelected ||
                         d.Status == SubscriptionDayStatus.CustomerSelected))
            .GroupBy(d => d.RestaurantId!.Value)
            .Select(g => new { RestaurantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RestaurantId, x => x.Count, cancellationToken);

        var frozenSubDaysCounts = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(d => d.ServiceDate == targetDate && d.RestaurantId.HasValue && d.Status == SubscriptionDayStatus.Frozen)
            .GroupBy(d => d.RestaurantId!.Value)
            .Select(g => new { RestaurantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RestaurantId, x => x.Count, cancellationToken);

        // Fetch pending reduction requests
        var pendingReductionsList = await _db.CapacityReductionRequests
            .AsNoTracking()
            .Where(r => r.Status == CapacityReductionStatus.Pending)
            .ToListAsync(cancellationToken);

        var pendingReductions = pendingReductionsList
            .GroupBy(r => r.RestaurantId)
            .ToDictionary(g => g.Key, g => g.First());


        // Fetch all reduction requests for the list
        var allReductionRequests = await _db.CapacityReductionRequests
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var reductionDtos = allReductionRequests.Select(r =>
        {
            var rest = restaurants.FirstOrDefault(x => x.Id == r.RestaurantId);
            var restName = rest?.TradeNameAr ?? rest?.LegalNameAr ?? $"مطعم {r.RestaurantId}";
            return new AdminCapacityReductionDto(
                r.Id.ToString(),
                r.RestaurantId.ToString(),
                restName,
                r.CurrentCapacity,
                r.RequestedCapacity,
                r.RequestedEffectiveDate.ToString("yyyy-MM-dd"),
                (r.ApprovedEffectiveDate ?? r.RequestedEffectiveDate).ToString("yyyy-MM-dd"),
                r.Reason,
                r.Status.ToString(),
                r.CreatedAtUtc.ToString("o")
            );
        }).ToList();

        // Build restaurant rows
        var restaurantRows = new List<AdminRestaurantCapacityRowDto>();

        foreach (var r in restaurants)
        {
            var area = r.PrimaryRegionId.HasValue && regions.TryGetValue(r.PrimaryRegionId.Value, out var areaName)
                ? areaName
                : "العاصمة";

            var policyRow = capacityPolicies.TryGetValue(r.Id, out var pol) ? pol : null;
            var dailyCap = dailyCaps.TryGetValue(r.Id, out var dc) ? dc : null;
            var restCommitments = commitmentsByRestaurant.TryGetValue(r.Id, out var comms) ? comms : new List<ServiceCapacityCommitment>();
            var subDaysCount = subscriptionDaysCounts.TryGetValue(r.Id, out var sdc) ? sdc : 0;

            var limit = policyRow?.DailyBoxLimit ?? dailyCap?.MaxBoxes ?? 100;
            if (limit <= 0) limit = 100;

            // Commitments breakdown
            var paidSubCommitments = restCommitments.Count(c => c.Source == CapacityCommitmentSource.PaidSubscription);
            var subscriptionBooked = Math.Max(paidSubCommitments, subDaysCount);

            var renewalReservePct = policy.RenewalReserveMinimumPercent / 100.0m;
            var renewalCommitments = restCommitments.Count(c => c.Source == CapacityCommitmentSource.ProtectedRenewal);
            var renewalReserved = renewalCommitments > 0
                ? renewalCommitments
                : (int)Math.Ceiling(limit * renewalReservePct);

            var frozenCommitmentCount = restCommitments.Count(c =>
                c.Source == CapacityCommitmentSource.IndependentDayCompensation ||
                c.Source == CapacityCommitmentSource.IndependentDayFreeze);
            var frozenDaysCount = frozenSubDaysCounts.TryGetValue(r.Id, out var fdc) ? fdc : 0;
            var frozenCompensated = Math.Max(frozenCommitmentCount, frozenDaysCount);

            var safetyReserve = Math.Max(0, (int)Math.Round(limit * 0.05m));
            var confirmedMeals = confirmedSubDaysCounts.TryGetValue(r.Id, out var cmc) ? cmc : (dailyCap?.ReservedBoxes ?? 0);
            var pending72h = pendingSubDaysCounts.TryGetValue(r.Id, out var pmc) ? pmc : Math.Max(0, subscriptionBooked - confirmedMeals);

            var bookedMeals = subscriptionBooked + renewalReserved + frozenCompensated;
            var isManualBusy = r.ManualBusyOverride;
            var util = limit > 0 ? (decimal)bookedMeals / limit : 0m;
            var isAutoBusy = util >= 1.0m;

            var blockedMeals = isManualBusy ? Math.Max(0, limit - bookedMeals) : 0;
            var availableNewSales = Math.Max(0, limit - (bookedMeals + blockedMeals + safetyReserve));

            var status = isManualBusy
                ? "busy_manual"
                : isAutoBusy
                    ? "busy_auto"
                    : util >= 0.9m
                        ? "at_risk"
                        : "active";

            var automationReason = status switch
            {
                "busy_manual" => "تم تعليق المبيعات مؤقتاً بقرار تشغيلي يدوي للمطعم.",
                "busy_auto" => $"تم تفعيل أتمتة الازدحام تلقائياً لاكتمال السعة المعتمدة ({bookedMeals}/{limit} وجبة).",
                "at_risk" => $"نسبة الاستخدام بلغت {Math.Round(util * 100)}%، اقتراب الطلب من السقف التشغيلي.",
                _ => "سعة تشغيلية مستقرة مع ضمان كامل لاشتراكات التجديد بنسبة 15% وحصص التجميد."
            };

            var pendingRed = pendingReductions.TryGetValue(r.Id, out var pr)
                ? new AdminRestaurantCapacityReductionInfo(
                    pr.RequestedCapacity,
                    (pr.ApprovedEffectiveDate ?? pr.RequestedEffectiveDate).ToString("yyyy-MM-dd"),
                    pr.CreatedAtUtc.ToString("o"),
                    pr.Reason)
                : null;

            restaurantRows.Add(new AdminRestaurantCapacityRowDto(
                r.Id.ToString(),
                string.IsNullOrWhiteSpace(r.TradeNameAr) ? r.LegalNameAr : r.TradeNameAr,
                string.IsNullOrWhiteSpace(r.TradeNameEn) ? r.LegalNameEn : r.TradeNameEn,
                area,
                "06:00 - 09:30",
                "Daily",
                limit,
                subscriptionBooked,
                renewalReserved,
                frozenCompensated,
                safetyReserve,
                bookedMeals,
                confirmedMeals,
                pending72h,
                blockedMeals,
                availableNewSales,
                status,
                automationReason,
                r.ClosedAtUtc?.ToString("o"),
                $"F031-v{policy.Version}",
                (r.ModifiedAtUtc ?? r.CreatedAtUtc).ToString("o"),
                $"CAP-{r.Id.ToString()[..8].ToUpperInvariant()}",
                pendingRed
            ));
        }

        // 3. Fetch recent audit events
        var recentAudits = await _db.AuditLogs
            .AsNoTracking()
            .Where(x => x.EntityType == "Capacity" || x.Action.StartsWith("CAP-") || x.Action.Contains("Capacity"))
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var auditDtos = recentAudits.Select(a =>
        {
            var rest = restaurants.FirstOrDefault(x => x.Id == a.EntityId);
            var restName = rest?.TradeNameAr ?? rest?.LegalNameAr ?? "المنصة العامة";
            return new AdminCapacityAuditDto(
                a.Id.ToString(),
                a.EntityId?.ToString() ?? "PLATFORM",
                restName,
                a.Action,
                "Operations Admin",
                a.Reason ?? "تحديث تشغيلي لسعة الخدمة والالتزامات",
                a.OccurredAtUtc.ToString("o")
            );
        }).ToList();

        // 4. Calculate aggregate KPIs
        var totalCapacity = restaurantRows.Sum(r => r.CapacityLimit);
        var totalSubBooked = restaurantRows.Sum(r => r.SubscriptionBooked);
        var totalRenReserved = restaurantRows.Sum(r => r.RenewalReserved);
        var totalFrozen = restaurantRows.Sum(r => r.FrozenCompensated);
        var totalSafety = restaurantRows.Sum(r => r.SafetyReserve);
        var totalAvailable = restaurantRows.Sum(r => r.AvailableNewSales);
        var totalBooked = restaurantRows.Sum(r => r.BookedMeals);
        var totalBlocked = restaurantRows.Sum(r => r.BlockedMeals);
        var totalUtilization = totalCapacity > 0 ? (int)Math.Round(((double)totalBooked / totalCapacity) * 100) : 0;
        var busyCount = restaurantRows.Count(r => r.Status == "busy_auto" || r.Status == "busy_manual");
        var atRiskCount = restaurantRows.Count(r => r.Status == "at_risk");
        var overridesCount = restaurantRows.Count(r => r.OverrideUntil != null);
        var pendingReductionsCount = reductionDtos.Count(r => r.Status == "Pending");

        var kpis = new AdminCapacityKpisDto(
            totalCapacity,
            totalSubBooked,
            totalRenReserved,
            totalFrozen,
            totalSafety,
            totalAvailable,
            totalBooked,
            totalBlocked,
            totalUtilization,
            busyCount,
            atRiskCount,
            overridesCount,
            pendingReductionsCount,
            policy.PlatformDailyBoxCeiling,
            policy.CalculateRenewalReserveUnits()
        );

        return Result.Success(new AdminCapacityOverviewResponseDto(
            policyDto,
            restaurantRows,
            reductionDtos,
            auditDtos,
            kpis
        ));
    }
}

public sealed class UpdatePlatformCapacityPolicyCommandHandler
    : ICommandHandler<UpdatePlatformCapacityPolicyCommand, AdminPlatformCapacityPolicyDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdatePlatformCapacityPolicyCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<AdminPlatformCapacityPolicyDto>> Handle(
        UpdatePlatformCapacityPolicyCommand command,
        CancellationToken cancellationToken)
    {
        var req = command.Request;
        var policy = await _db.PlatformCapacityPolicies
            .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

        if (policy is null)
        {
            policy = PlatformCapacityPolicy.Create(
                req.DailyCapTotal ?? 5000,
                req.RenewalReservePercentage ?? 15.0m,
                DateOnly.FromDateTime(DateTime.UtcNow),
                1,
                req.Reason ?? "Initial policy setup").Value;
            _db.PlatformCapacityPolicies.Add(policy);
        }
        else
        {
            if (req.DailyCapTotal.HasValue && req.DailyCapTotal.Value > 0)
                policy.PlatformDailyBoxCeiling = req.DailyCapTotal.Value;

            if (req.RenewalReservePercentage.HasValue && req.RenewalReservePercentage.Value >= 0 && req.RenewalReservePercentage.Value <= 100)
                policy.RenewalReserveMinimumPercent = req.RenewalReservePercentage.Value;

            policy.Version += 1;
            policy.Reason = req.Reason ?? "Updated platform capacity policy";
            policy.ModifiedAtUtc = DateTime.UtcNow;
            policy.ModifiedByUserId = _currentUser.UserId;
        }

        // Record audit
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = "PolicyUpdate",
            EntityType = "Capacity",
            ActorUserId = _currentUser.UserId,
            Reason = $"تحديث سياسة المنصة: سقف السعة {policy.PlatformDailyBoxCeiling}، نسبة التجديد {policy.RenewalReserveMinimumPercent}%. {req.Reason}",
            OccurredAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AdminPlatformCapacityPolicyDto(
            policy.PlatformDailyBoxCeiling,
            policy.RenewalReserveMinimumPercent,
            req.RenewalWindowDays ?? 3,
            req.CutoffLeadHours ?? 72,
            DateTime.UtcNow.ToString("o")
        ));
    }
}

public sealed class UpdateRestaurantCapacityLimitCommandHandler
    : ICommandHandler<UpdateRestaurantCapacityLimitCommand, AdminRestaurantCapacityRowDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateRestaurantCapacityLimitCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<AdminRestaurantCapacityRowDto>> Handle(
        UpdateRestaurantCapacityLimitCommand command,
        CancellationToken cancellationToken)
    {
        if (command.NewCapacityLimit <= 0)
            return Result.Failure<AdminRestaurantCapacityRowDto>(Error.Validation("capacity.invalid_limit", "Capacity limit must be positive."));

        var restaurant = await _db.RestaurantProfiles
            .FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);

        if (restaurant is null)
            return Result.Failure<AdminRestaurantCapacityRowDto>(Error.NotFound("restaurant.not_found", "Restaurant was not found."));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existingPolicy = await _db.RestaurantCapacityPolicies
            .FirstOrDefaultAsync(p => p.RestaurantId == command.RestaurantId && p.IsActive, cancellationToken);

        if (existingPolicy is null)
        {
            var createResult = RestaurantCapacityPolicy.Create(
                command.RestaurantId,
                command.NewCapacityLimit,
                today,
                1,
                command.Reason);

            if (createResult.IsFailure)
                return Result.Failure<AdminRestaurantCapacityRowDto>(Error.Validation("capacity.create_failed", createResult.Error));

            _db.RestaurantCapacityPolicies.Add(createResult.Value);
        }
        else
        {
            existingPolicy.DailyBoxLimit = command.NewCapacityLimit;
            existingPolicy.Version += 1;
            existingPolicy.Reason = command.Reason;
            existingPolicy.ModifiedAtUtc = DateTime.UtcNow;
            existingPolicy.ModifiedByUserId = _currentUser.UserId;
        }

        // Also update or ensure today's DailyCapacity
        var dailyCap = await _db.DailyCapacities
            .FirstOrDefaultAsync(c => c.RestaurantId == command.RestaurantId && c.ServiceDate == today, cancellationToken);

        if (dailyCap is not null)
        {
            dailyCap.MaxBoxes = command.NewCapacityLimit;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityId = command.RestaurantId,
            EntityType = "Capacity",
            Action = "Update",
            ActorUserId = _currentUser.UserId,
            Reason = $"تعديل سعة المطعم إلى {command.NewCapacityLimit} وجبة/يوم: {command.Reason}",
            OccurredAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Return a fresh query row
        var query = new GetAdminCapacityOverviewQuery(today);
        var overviewResult = await new GetAdminCapacityOverviewQueryHandler(_db).Handle(query, cancellationToken);
        var row = overviewResult.Value.Restaurants.FirstOrDefault(r => r.RestaurantId == command.RestaurantId.ToString());

        return row is not null
            ? Result.Success(row)
            : Result.Failure<AdminRestaurantCapacityRowDto>(Error.Failure("capacity.fetch_failed", "Failed to retrieve updated capacity row."));
    }
}

public sealed class ToggleRestaurantCapacityBusyCommandHandler
    : ICommandHandler<ToggleRestaurantCapacityBusyCommand, AdminRestaurantCapacityRowDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ToggleRestaurantCapacityBusyCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<AdminRestaurantCapacityRowDto>> Handle(
        ToggleRestaurantCapacityBusyCommand command,
        CancellationToken cancellationToken)
    {
        var restaurant = await _db.RestaurantProfiles
            .FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);

        if (restaurant is null)
            return Result.Failure<AdminRestaurantCapacityRowDto>(Error.NotFound("restaurant.not_found", "Restaurant was not found."));

        restaurant.ManualBusyOverride = command.IsBusy;
        restaurant.ClosedAtUtc = command.IsBusy
            ? DateTime.UtcNow.AddHours(command.OverrideHours ?? 4)
            : null;

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityId = command.RestaurantId,
            EntityType = "Capacity",
            Action = command.IsBusy ? "Override" : "Release",
            ActorUserId = _currentUser.UserId,
            Reason = command.Reason,
            OccurredAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = new GetAdminCapacityOverviewQuery(today);
        var overviewResult = await new GetAdminCapacityOverviewQueryHandler(_db).Handle(query, cancellationToken);
        var row = overviewResult.Value.Restaurants.FirstOrDefault(r => r.RestaurantId == command.RestaurantId.ToString());

        return row is not null
            ? Result.Success(row)
            : Result.Failure<AdminRestaurantCapacityRowDto>(Error.Failure("capacity.fetch_failed", "Failed to retrieve updated capacity row."));
    }
}

public sealed class ApproveAdminCapacityReductionCommandHandler
    : ICommandHandler<ApproveAdminCapacityReductionCommand, AdminCapacityReductionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ApproveAdminCapacityReductionCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<AdminCapacityReductionDto>> Handle(
        ApproveAdminCapacityReductionCommand command,
        CancellationToken cancellationToken)
    {
        var request = await _db.CapacityReductionRequests
            .FirstOrDefaultAsync(r => r.Id == command.RequestId, cancellationToken);

        if (request is null)
            return Result.Failure<AdminCapacityReductionDto>(Error.NotFound("reduction.not_found", "Capacity reduction request was not found."));

        var adminId = _currentUser.UserId ?? Guid.NewGuid();
        var safeDate = request.ApprovedEffectiveDate ?? request.RequestedEffectiveDate;

        var approveResult = request.Approve(adminId, safeDate, command.Notes);
        if (approveResult.IsFailure)
            return Result.Failure<AdminCapacityReductionDto>(Error.Validation("reduction.approve_failed", approveResult.Error));

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityId = request.RestaurantId,
            EntityType = "Capacity",
            Action = "ReductionSchedule",
            ActorUserId = adminId,
            Reason = $"تم اعتماد طلب تخفيض السعة إلى {request.RequestedCapacity} وجبة بنفاذ آمن في {safeDate:yyyy-MM-dd}.",
            OccurredAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var rest = await _db.RestaurantProfiles.FirstOrDefaultAsync(x => x.Id == request.RestaurantId, cancellationToken);
        var restName = rest?.TradeNameAr ?? rest?.LegalNameAr ?? "مطعم";

        return Result.Success(new AdminCapacityReductionDto(
            request.Id.ToString(),
            request.RestaurantId.ToString(),
            restName,
            request.CurrentCapacity,
            request.RequestedCapacity,
            request.RequestedEffectiveDate.ToString("yyyy-MM-dd"),
            safeDate.ToString("yyyy-MM-dd"),
            request.Reason,
            request.Status.ToString(),
            request.CreatedAtUtc.ToString("o")
        ));
    }
}

public sealed class RejectAdminCapacityReductionCommandHandler
    : ICommandHandler<RejectAdminCapacityReductionCommand, AdminCapacityReductionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public RejectAdminCapacityReductionCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<AdminCapacityReductionDto>> Handle(
        RejectAdminCapacityReductionCommand command,
        CancellationToken cancellationToken)
    {
        var request = await _db.CapacityReductionRequests
            .FirstOrDefaultAsync(r => r.Id == command.RequestId, cancellationToken);

        if (request is null)
            return Result.Failure<AdminCapacityReductionDto>(Error.NotFound("reduction.not_found", "Capacity reduction request was not found."));

        var adminId = _currentUser.UserId ?? Guid.NewGuid();
        var rejectResult = request.Reject(adminId, command.Notes);
        if (rejectResult.IsFailure)
            return Result.Failure<AdminCapacityReductionDto>(Error.Validation("reduction.reject_failed", rejectResult.Error));

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityId = request.RestaurantId,
            EntityType = "Capacity",
            Action = "Override",
            ActorUserId = adminId,
            Reason = $"تم رفض طلب تخفيض السعة: {command.Notes ?? "رفض من العمليات لحماية الاشتراكات"}",
            OccurredAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var rest = await _db.RestaurantProfiles.FirstOrDefaultAsync(x => x.Id == request.RestaurantId, cancellationToken);
        var restName = rest?.TradeNameAr ?? rest?.LegalNameAr ?? "مطعم";

        return Result.Success(new AdminCapacityReductionDto(
            request.Id.ToString(),
            request.RestaurantId.ToString(),
            restName,
            request.CurrentCapacity,
            request.RequestedCapacity,
            request.RequestedEffectiveDate.ToString("yyyy-MM-dd"),
            request.RequestedEffectiveDate.ToString("yyyy-MM-dd"),
            request.Reason,
            request.Status.ToString(),
            request.CreatedAtUtc.ToString("o")
        ));
    }
}
