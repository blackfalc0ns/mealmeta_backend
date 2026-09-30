using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Renewal;

public sealed class SubscriptionRenewalService : ISubscriptionRenewalService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SubscriptionRenewalService(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CustomerRenewalRequestDto>> RequestRenewalAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId, ct);

        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        if (subscription.Status is SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed or SubscriptionStatus.Cancelled)
            return Error.Conflict("subscription.renewal_not_available", "Renewal is not available for this subscription status.");

        var policy = await MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.ReadAsync(_db, ct);
        if (!MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.IsRenewalAvailable(policy, subscription.EndDate, _clock.TodayUtc))
            return Error.Conflict("subscription.renewal_not_available", "Renewal is disabled or its availability window has not started.");

        var request = await _db.SubscriptionRenewals
            .FirstOrDefaultAsync(x => x.PreviousSubscriptionId == subscriptionId && x.Status == "Requested", ct);

        if (request is null)
        {
            request = new SubscriptionRenewal
            {
                PreviousSubscriptionId = subscriptionId,
                Status = "Requested",
                RequestedAtUtc = _clock.UtcNow,
            };
            _db.SubscriptionRenewals.Add(request);
            await _db.SaveChangesAsync(ct);
        }

        return new CustomerRenewalRequestDto(
            request.Id,
            subscriptionId,
            request.Status,
            subscription.ProgramId,
            subscription.BundleId,
            subscription.SelectedTier,
            subscription.DurationDays,
            subscription.IncludeFriday,
            subscription.RegionId,
            subscription.EndDate.AddDays(1),
            "/api/v1/customer/subscription-flow/configuration",
            "/api/v1/customer/subscriptions/checkout");
    }
}
