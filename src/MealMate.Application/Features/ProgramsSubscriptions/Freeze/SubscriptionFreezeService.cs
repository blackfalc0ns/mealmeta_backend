using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MediatR;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Freeze;

public sealed class SubscriptionFreezeService : ISubscriptionFreezeService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ISender? _sender;

    public SubscriptionFreezeService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ISender? sender = null)
    {
        _db = db;
        _clock = clock;
        _sender = sender;
    }

    public async Task<Result<CustomerSubscriptionFreezePolicyDto>> GetPolicyAsync(CancellationToken ct = default)
    {
        var policyRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);
        var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);
        var earliestStart = _clock.TodayUtc.AddDays(policy.StartDelayDays);
        var leadHours = policy.StartDelayDays * 24;

        var noticeAr = $"عند تجميد الاشتراك: سيتم تمديد تاريخ انتهاء اشتراكك تلقائياً بعدد أيام التجميد. طلبات مهلة المطبخ ({leadHours} ساعة القادمة) مؤكدة وسيتم تسليمها كالمعتاد، ويتوقف التوصيل مؤقتاً أثناء فترة التجميد ويُستأنف تلقائياً بعدها.";
        var noticeEn = $"When freezing: Subscription end date will be extended automatically. Orders within kitchen lead window ({leadHours}h) are confirmed and delivered, and deliveries pause during freeze and resume automatically.";

        return new CustomerSubscriptionFreezePolicyDto(
            policy.DefaultDurationDays,
            policy.StartDelayDays,
            policy.ExtendSubscriptionEnd,
            earliestStart,
            $"يمكن تجميد الاشتراك لمدة حتى {policy.DefaultDurationDays} أيام، ويبدأ التجميد بعد {policy.StartDelayDays} أيام على الأقل من تاريخ {earliestStart:yyyy-MM-dd}.",
            $"You can freeze your subscription for up to {policy.DefaultDurationDays} days. Freezing can start after at least {policy.StartDelayDays} days from {earliestStart:yyyy-MM-dd}.",
            noticeAr,
            noticeEn);
    }

    public async Task<Result<CustomerSubscriptionFreezeDetailsDto>> GetCurrentFreezeDetailsAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await ResolveCurrentSubscriptionAsync(customerUserId, ct);
        if (subscription is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "No current subscription found.");
        }

        return await BuildFreezeDetailsAsync(subscription, ct);
    }

    public async Task<Result<CustomerSubscriptionFreezeDetailsDto>> GetFreezeDetailsAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId, ct);

        if (subscription is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");
        }

        return await BuildFreezeDetailsAsync(subscription, ct);
    }

    public async Task<Result<SubscriptionFreezeRequestDto>> RequestFreezeAsync(Guid subscriptionId, Guid customerUserId, CustomerFreezeRequest request, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId, ct);
        if (subscription is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");
        }

        var hasPending = await _db.SubscriptionFreezeRequests.AsNoTracking()
            .AnyAsync(x => x.SubscriptionId == subscriptionId && x.Status == VerificationStatus.Submitted, ct);
        if (hasPending)
        {
            return Error.Conflict("subscription.freeze_request_already_pending", "A freeze request is already pending for this subscription.");
        }

        if (_sender is not null)
        {
            return await _sender.Send(new RequestSubscriptionFreezeCommand(
                subscriptionId, request.StartDate, request.EndDate, request.Reason, request.AcknowledgeLostSelections), ct);
        }

        // Direct execution fallback if MediatR is not injected
        var handler = new SubscriptionOperationsHandlers(_db, null!, _clock, null!, null!, null!);
        return await handler.Handle(new RequestSubscriptionFreezeCommand(
            subscriptionId, request.StartDate, request.EndDate, request.Reason, request.AcknowledgeLostSelections), ct);
    }

    private async Task<Subscription?> ResolveCurrentSubscriptionAsync(Guid customerUserId, CancellationToken ct)
    {
        var subscriptions = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId)
            .ToListAsync(ct);

        var currentStatuses = new[]
        {
            SubscriptionStatus.Active,
            SubscriptionStatus.Frozen,
            SubscriptionStatus.Scheduled,
            SubscriptionStatus.Paid,
            SubscriptionStatus.SuspendedForReview,
        };

        return subscriptions
            .Where(x => currentStatuses.Contains(x.Status))
            .OrderBy(x => StatusPriority(x.Status))
            .ThenByDescending(x => x.StartDate)
            .FirstOrDefault();
    }

    private static int StatusPriority(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Active => 1,
        SubscriptionStatus.Frozen => 2,
        SubscriptionStatus.Scheduled => 3,
        SubscriptionStatus.Paid or SubscriptionStatus.SuspendedForReview => 4,
        _ => 99,
    };

    private async Task<CustomerSubscriptionFreezeDetailsDto> BuildFreezeDetailsAsync(Subscription subscription, CancellationToken ct)
    {
        var freezeRequests = await _db.SubscriptionFreezeRequests.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var days = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(ct);

        var approvedFreeze = freezeRequests
            .FirstOrDefault(x => x.Status == VerificationStatus.Approved && x.FreezeEndDate >= _clock.TodayUtc);

        var isCurrentlyFrozen = subscription.Status == SubscriptionStatus.Frozen
            || (approvedFreeze is not null && _clock.TodayUtc >= approvedFreeze.FreezeStartDate && _clock.TodayUtc <= approvedFreeze.FreezeEndDate);

        CustomerActiveFreezeDto? currentFreezeDto = null;
        if (approvedFreeze is not null)
        {
            var frozenDates = days
                .Where(x => x.Status == SubscriptionDayStatus.Frozen && x.ServiceDate >= approvedFreeze.FreezeStartDate && x.ServiceDate <= approvedFreeze.FreezeEndDate)
                .Select(x => x.ServiceDate).ToList();

            var compensationDates = days
                .Where(x => x.Status != SubscriptionDayStatus.Frozen && x.ServiceDate > approvedFreeze.FreezeEndDate)
                .TakeLast(frozenDates.Count)
                .Select(x => x.ServiceDate).ToList();

            var isCurrentlyActive = _clock.TodayUtc >= approvedFreeze.FreezeStartDate && _clock.TodayUtc <= approvedFreeze.FreezeEndDate;
            var isUpcoming = _clock.TodayUtc < approvedFreeze.FreezeStartDate;

            var summaryAr = $"تم تجميد {frozenDates.Count} يوم خدمة للفترة من {approvedFreeze.FreezeStartDate:yyyy-MM-dd} إلى {approvedFreeze.FreezeEndDate:yyyy-MM-dd}، وتم تعويضها بإضافة {compensationDates.Count} يوم بديل في نهاية الاشتراك حتى {subscription.EndDate:yyyy-MM-dd}.";
            var summaryEn = $"{frozenDates.Count} service days frozen from {approvedFreeze.FreezeStartDate:yyyy-MM-dd} to {approvedFreeze.FreezeEndDate:yyyy-MM-dd}, compensated with {compensationDates.Count} replacement days at subscription end until {subscription.EndDate:yyyy-MM-dd}.";

            currentFreezeDto = new CustomerActiveFreezeDto(
                approvedFreeze.Id,
                approvedFreeze.FreezeStartDate,
                approvedFreeze.FreezeEndDate,
                approvedFreeze.FreezeDays,
                frozenDates.Count,
                "Approved",
                "معتمد ومطبق",
                isCurrentlyActive,
                isUpcoming,
                approvedFreeze.Reason,
                approvedFreeze.CreatedAtUtc,
                approvedFreeze.ProcessedAtUtc,
                frozenDates,
                compensationDates,
                subscription.EndDate,
                summaryAr,
                summaryEn);
        }

        var history = freezeRequests.Select(x => new CustomerFreezeHistoryItemDto(
            x.Id,
            x.FreezeStartDate,
            x.FreezeEndDate,
            x.FreezeDays,
            x.Status.ToString(),
            x.Status == VerificationStatus.Approved ? "معتمد ومطبق" : x.Status == VerificationStatus.Submitted ? "قيد المراجعة" : "مرفوض",
            x.Reason,
            x.CreatedAtUtc,
            x.ProcessedAtUtc)).ToList();

        var noticeAr = currentFreezeDto is not null
            ? currentFreezeDto.SummaryAr
            : "لا يوجد تجميد نشط حالياً لهذا الاشتراك.";
        var noticeEn = currentFreezeDto is not null
            ? currentFreezeDto.SummaryEn
            : "No active freeze for this subscription.";

        return new CustomerSubscriptionFreezeDetailsDto(
            subscription.Id,
            CustomerFacingStatus(subscription.Status),
            StatusNameAr(subscription.Status),
            approvedFreeze is not null,
            isCurrentlyFrozen,
            currentFreezeDto,
            history,
            noticeAr,
            noticeEn);
    }

    private static string CustomerFacingStatus(SubscriptionStatus status) =>
        status == SubscriptionStatus.SuspendedForReview ? nameof(SubscriptionStatus.Paid) : status.ToString();

    private static string StatusNameAr(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "بانتظار الدفع",
        SubscriptionStatus.Paid or SubscriptionStatus.SuspendedForReview => "تم الدفع",
        SubscriptionStatus.Scheduled => "مجدول",
        SubscriptionStatus.Active => "نشط",
        SubscriptionStatus.Frozen => "مجمد",
        SubscriptionStatus.Completed => "مكتمل",
        SubscriptionStatus.Cancelled => "ملغي",
        SubscriptionStatus.CancellationPending => "إلغاء قيد المتابعة",
        SubscriptionStatus.Expired => "منتهي",
        SubscriptionStatus.PaymentFailed => "فشل الدفع",
        _ => "مسودة",
    };
}
