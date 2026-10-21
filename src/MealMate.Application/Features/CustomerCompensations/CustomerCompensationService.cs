using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCompensations;

public interface ICustomerCompensationService
{
    Task<Result<IReadOnlyList<CustomerCompensationSummaryDto>>> GetCompensationsAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<EligibleAlternativeDateDto>>> GetAlternativeDatesAsync(
        Guid compensationId,
        Guid customerUserId,
        int count = 14,
        CancellationToken cancellationToken = default);

    Task<Result<ChooseCompensationResultDto>> ChooseCompensationAsync(
        Guid compensationId,
        Guid customerUserId,
        ChooseCompensationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CustomerCompensationService : ICustomerCompensationService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IOperationalPolicyService? _policyService;

    public CustomerCompensationService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IOperationalPolicyService? policyService = null)
    {
        _db = db;
        _clock = clock;
        _policyService = policyService;
    }

    public async Task<Result<IReadOnlyList<CustomerCompensationSummaryDto>>> GetCompensationsAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var compensations = await _db.CustomerServiceCompensations
            .Where(x => x.CustomerUserId == customerUserId)
            .OrderByDescending(x => x.FailureConfirmedAtUtc)
            .ToListAsync(cancellationToken);

        var hasChanges = false;
        var results = new List<CustomerCompensationSummaryDto>(compensations.Count);

        foreach (var c in compensations)
        {
            // Automated 48-hour fallback rule: If choice deadline has passed, automatically transition to AutoRefundInitiated
            if (c.IsChoiceExpired(now))
            {
                c.Status = CustomerCompensationStatus.AutoRefundInitiated;
                c.DecidedAtUtc = now;
                hasChanges = true;
            }

            var remainingSeconds = c.Status == CustomerCompensationStatus.PendingChoice
                ? Math.Max(0, (long)(c.ChoiceDeadlineUtc - now).TotalSeconds)
                : 0L;

            results.Add(new CustomerCompensationSummaryDto(
                CompensationId: c.Id,
                SubscriptionId: c.SubscriptionId,
                BeneficiaryFamilyMemberId: c.BeneficiaryFamilyMemberId,
                FailedServiceDate: c.FailedServiceDate,
                Reason: c.Reason,
                TotalAmount: c.NetDayValue,
                Currency: c.Currency,
                RefundBreakdown: new RefundBreakdownDto(
                    CardRefundAmount: c.CardRefundAmount,
                    WalletRefundAmount: c.WalletRefundAmount,
                    ApologyCreditAmount: c.ApologyCreditAmount),
                FailureConfirmedAtUtc: c.FailureConfirmedAtUtc,
                ChoiceDeadlineUtc: c.ChoiceDeadlineUtc,
                ServerTimeUtc: now,
                RemainingSeconds: remainingSeconds,
                Status: c.Status,
                EligibleForAlternativeDay: c.EligibleForAlternativeDay,
                EligibleForFreeDay: c.EligibleForAlternativeDay,
                SelectedAlternativeDate: c.SelectedAlternativeDate));
        }

        if (hasChanges)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success<IReadOnlyList<CustomerCompensationSummaryDto>>(results);
    }

    public async Task<Result<IReadOnlyList<EligibleAlternativeDateDto>>> GetAlternativeDatesAsync(
        Guid compensationId,
        Guid customerUserId,
        int count = 14,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var compensation = await _db.CustomerServiceCompensations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == compensationId, cancellationToken);

        if (compensation == null)
            return Result.Failure<IReadOnlyList<EligibleAlternativeDateDto>>(Error.NotFound("COMPENSATION_NOT_FOUND", "Compensation record not found."));

        if (compensation.CustomerUserId != customerUserId)
            return Result.Failure<IReadOnlyList<EligibleAlternativeDateDto>>(Error.Forbidden("FORBIDDEN", "Access to compensation record denied."));

        var subscription = await _db.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == compensation.SubscriptionId, cancellationToken);

        var includeFriday = subscription?.IncludeFriday ?? true;

        var leadTimeHours = 72;
        var policyVersion = 1L;

        if (_policyService is not null)
        {
            var settings = await _policyService.GetSettingsAsync(cancellationToken);
            if (settings?.Cutoff is not null)
            {
                leadTimeHours = settings.Cutoff.CutoffLeadHours;
                policyVersion = settings.Version;
            }
        }

        var timingPolicy = new TimingSnapshot(policyVersion, leadTimeHours);

        var eligibleDates = ServiceTimingPolicy.GetEligibleFirstDeliveryDates(
            paidAt: now,
            policy: timingPolicy,
            includeFriday: includeFriday,
            defaultDeliveryStartTime: new TimeOnly(12, 0),
            timeZoneOffset: TimeSpan.FromHours(3),
            lookaheadDays: count).ToList();

        var dayNamesAr = new Dictionary<DayOfWeek, string>
        {
            [DayOfWeek.Sunday] = "الأحد",
            [DayOfWeek.Monday] = "الاثنين",
            [DayOfWeek.Tuesday] = "الثلاثاء",
            [DayOfWeek.Wednesday] = "الأربعاء",
            [DayOfWeek.Thursday] = "الخميس",
            [DayOfWeek.Friday] = "الجمعة",
            [DayOfWeek.Saturday] = "السبت"
        };

        var dayNamesEn = new Dictionary<DayOfWeek, string>
        {
            [DayOfWeek.Sunday] = "Sunday",
            [DayOfWeek.Monday] = "Monday",
            [DayOfWeek.Tuesday] = "Tuesday",
            [DayOfWeek.Wednesday] = "Wednesday",
            [DayOfWeek.Thursday] = "Thursday",
            [DayOfWeek.Friday] = "Friday",
            [DayOfWeek.Saturday] = "Saturday"
        };

        var results = eligibleDates.Select(date =>
        {
            var dow = date.DayOfWeek;
            return new EligibleAlternativeDateDto(
                Date: date,
                DayOfWeek: (int)dow,
                DayNameAr: dayNamesAr[dow],
                DayNameEn: dayNamesEn[dow],
                IsAvailable: true,
                AvailableCapacity: 50);
        }).ToList();

        return Result.Success<IReadOnlyList<EligibleAlternativeDateDto>>(results);
    }

    public async Task<Result<ChooseCompensationResultDto>> ChooseCompensationAsync(
        Guid compensationId,
        Guid customerUserId,
        ChooseCompensationRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var compensation = await _db.CustomerServiceCompensations
            .FirstOrDefaultAsync(x => x.Id == compensationId, cancellationToken);

        if (compensation == null)
            return Result.Failure<ChooseCompensationResultDto>(Error.NotFound("COMPENSATION_NOT_FOUND", "Compensation record not found."));

        if (compensation.CustomerUserId != customerUserId)
            return Result.Failure<ChooseCompensationResultDto>(Error.Forbidden("FORBIDDEN", "Access to compensation record denied."));

        if (compensation.Status != CustomerCompensationStatus.PendingChoice)
        {
            return Result.Failure<ChooseCompensationResultDto>(Error.Conflict(
                "COMPENSATION_ALREADY_DECIDED",
                $"This compensation has already been resolved with status: {compensation.Status}."));
        }

        // Enforce 48-hour customer choice window
        if (compensation.IsChoiceExpired(now))
        {
            compensation.Status = CustomerCompensationStatus.AutoRefundInitiated;
            compensation.DecidedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Failure<ChooseCompensationResultDto>(Error.Conflict(
                "COMPENSATION_CHOICE_WINDOW_EXPIRED",
                "انتهت مهلة اختيار التعويض (48 ساعة). جاري تنفيذ الاسترداد التلقائي إلى وسائل الدفع الأصلية."));
        }

        if (string.Equals(request.Choice, "CashRefund", StringComparison.OrdinalIgnoreCase))
        {
            compensation.Status = CustomerCompensationStatus.CashRefundChosen;
            compensation.DecidedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new ChooseCompensationResultDto(
                CompensationId: compensation.Id,
                Status: CustomerCompensationStatus.CashRefundChosen,
                StatusLabel: "CashRefundChosen",
                SelectedAlternativeDate: null,
                Message: "تم تسجيل اختيارك بنجاح. سيتم تحويل الاسترداد النقدي وفق مصادر الدفع الأصلية.",
                ServerTimeUtc: now));
        }

        if (string.Equals(request.Choice, "AlternativeDay", StringComparison.OrdinalIgnoreCase))
        {
            if (!compensation.EligibleForAlternativeDay)
            {
                return Result.Failure<ChooseCompensationResultDto>(Error.Validation(
                    "ALTERNATIVE_DAY_NOT_ELIGIBLE",
                    "هذا التعويض مؤهل للاسترداد المالي فقط ولا يدعم اختيار يوم بديل."));
            }

            if (!request.TargetDate.HasValue)
            {
                return Result.Failure<ChooseCompensationResultDto>(Error.Validation(
                    "TARGET_DATE_REQUIRED",
                    "يجب اختيار تاريخ اليوم البديل لتأكيد الحجز."));
            }

            var targetDate = request.TargetDate.Value;

            var leadTimeHours = 72;
            if (_policyService is not null)
            {
                var settings = await _policyService.GetSettingsAsync(cancellationToken);
                if (settings?.Cutoff is not null)
                {
                    leadTimeHours = settings.Cutoff.CutoffLeadHours;
                }
            }

            var earliestEligibleDate = DateOnly.FromDateTime(now.AddHours(leadTimeHours));

            if (targetDate < earliestEligibleDate)
            {
                return Result.Failure<ChooseCompensationResultDto>(Error.Validation(
                    "TARGET_DATE_TOO_EARLY",
                    $"تاريخ اليوم البديل يجب أن يكون بعد مهلة التشغيل المعتمدة ({leadTimeHours} ساعة) على الأقل من {earliestEligibleDate:yyyy-MM-dd}."));
            }

            var subscription = await _db.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == compensation.SubscriptionId, cancellationToken);

            var includeFriday = subscription?.IncludeFriday ?? true;
            if (targetDate.DayOfWeek == DayOfWeek.Friday && !includeFriday)
            {
                return Result.Failure<ChooseCompensationResultDto>(Error.Validation(
                    "FRIDAY_EXCLUDED_BY_PLAN",
                    "باقة الاشتراك الحالية لا تشمل أيام الجمعة. يرجى اختيار يوم آخر في الأسبوع."));
            }

            // Read dynamic daily ceiling from platform capacity policy
            var platformPolicy = await _db.PlatformCapacityPolicies
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.Version)
                .FirstOrDefaultAsync(cancellationToken);

            var maxDailyCapacity = platformPolicy?.PlatformDailyBoxCeiling ?? 500;

            var existingCommitmentsCount = await _db.ServiceCapacityCommitments
                .CountAsync(c => c.ServiceDate == targetDate && c.Status == CapacityCommitmentStatus.Reserved, cancellationToken);
            if (existingCommitmentsCount >= maxDailyCapacity)
            {
                return Result.Failure<ChooseCompensationResultDto>(Error.Conflict(
                    "CAPACITY_DEPLETED_FOR_DATE",
                    "اكتملت السعة التشغيلية لهذا التاريخ بالتزامن مع طلبك. يرجى اختيار تاريخ بديل آخر."));
            }

            // Create guaranteed independent capacity commitment for the alternative day
            var commitment = new ServiceCapacityCommitment
            {
                ServiceDate = targetDate,
                CustomerUserId = customerUserId,
                FamilyMemberId = compensation.BeneficiaryFamilyMemberId,
                SubscriptionId = compensation.SubscriptionId,
                Source = CapacityCommitmentSource.IndependentDayCompensation,
                Status = CapacityCommitmentStatus.Reserved,
                RestaurantId = null,
                RegionId = subscription?.RegionId ?? Guid.Empty,
                DeliveryAddressId = null,
                LeadPolicyVersion = 1,
                CreatedAtUtc = now,
                ModifiedAtUtc = now
            };

            _db.ServiceCapacityCommitments.Add(commitment);

            compensation.Status = CustomerCompensationStatus.AlternativeDayChosen;
            compensation.SelectedAlternativeDate = targetDate;
            compensation.AlternativeCommitmentId = commitment.Id;
            compensation.DecidedAtUtc = now;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new ChooseCompensationResultDto(
                CompensationId: compensation.Id,
                Status: CustomerCompensationStatus.AlternativeDayChosen,
                StatusLabel: "AlternativeDayChosen",
                SelectedAlternativeDate: targetDate,
                Message: $"تم تأكيد حجز وجبتك البديلة بنجاح ليوم {targetDate:yyyy-MM-dd}.",
                ServerTimeUtc: now));
        }

        return Result.Failure<ChooseCompensationResultDto>(Error.Validation(
            "INVALID_CHOICE",
            "نوع الاختيار غير صالح. يرجى اختيار 'CashRefund' أو 'AlternativeDay'."));
    }
}
