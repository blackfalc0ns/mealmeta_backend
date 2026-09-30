using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CancelCampaignSubscriptionResultDto(
    Guid SubscriptionId,
    Guid CampaignId,
    string Status,
    decimal RefundAmount,
    string Reason, string? RefundStatus = null, string? RefundReference = null);

public sealed record CancelCampaignSubscriptionCommand(
    Guid SubscriptionId,
    string Reason,
    bool IssueRefund = true) : ICommand<CancelCampaignSubscriptionResultDto>;

public sealed class CancelCampaignSubscriptionCommandHandler
    : ICommandHandler<CancelCampaignSubscriptionCommand, CancelCampaignSubscriptionResultDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IPaymentRefundGateway _refundGateway;

    public CancelCampaignSubscriptionCommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IPaymentRefundGateway refundGateway)
    {
        _db = db;
        _clock = clock;
        _refundGateway = refundGateway;
    }

    public async Task<Result<CancelCampaignSubscriptionResultDto>> Handle(
        CancelCampaignSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var sub = await _db.Subscriptions.FindAsync(new object[] { request.SubscriptionId }, cancellationToken);
        if (sub == null)
        {
            return Result.Failure<CancelCampaignSubscriptionResultDto>(
                Error.NotFound("Campaign.SubscriptionNotFound", $"Subscription '{request.SubscriptionId}' was not found."));
        }

        if (!sub.CampaignId.HasValue)
        {
            return Result.Failure<CancelCampaignSubscriptionResultDto>(
                Error.Validation("Campaign.NotCampaignSubscription", "Subscription is not associated with any collaborative campaign."));
        }

        if (sub.Status == SubscriptionStatus.Cancelled)
        {
            return Result.Failure<CancelCampaignSubscriptionResultDto>(
                Error.Conflict("Campaign.AlreadyCancelled", "Subscription is already cancelled."));
        }

        var campaignId = sub.CampaignId.Value;
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        // 1. Release future scheduled capacity
        var upcomingDays = await _db.SubscriptionDays
            .Where(d => d.SubscriptionId == sub.Id &&
                        d.ServiceDate >= today &&
                        d.Status != SubscriptionDayStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var restIds = upcomingDays.Where(d => d.RestaurantId.HasValue).Select(d => d.RestaurantId!.Value).Distinct().ToList();
        var dates = upcomingDays.Select(d => d.ServiceDate).Distinct().ToList();

        var capacities = await _db.CampaignRestaurantCapacities
            .Where(c => c.CampaignId == campaignId &&
                        restIds.Contains(c.RestaurantId) &&
                        c.ServiceDate.HasValue &&
                        dates.Contains(c.ServiceDate.Value))
            .ToListAsync(cancellationToken);

        foreach (var day in upcomingDays)
        {
            day.Status = SubscriptionDayStatus.Cancelled;

            if (day.RestaurantId.HasValue)
            {
                var cap = capacities.FirstOrDefault(c => c.RestaurantId == day.RestaurantId.Value && c.ServiceDate == day.ServiceDate);
                if (cap != null)
                {
                    cap.ReservedBoxes = Math.Max(0, cap.ReservedBoxes - 1);
                }
            }
        }

        // 2. Decrement subscriber cap count
        var capState = await _db.CampaignSubscriberCaps
            .FirstOrDefaultAsync(c => c.CampaignId == campaignId, cancellationToken);
        if (capState != null)
        {
            capState.CurrentCount = Math.Max(0, capState.CurrentCount - 1);
        }

        // 2b. Decrement Offer usage and remove/rollback redemption record
        var offer = await _db.Offers
            .FirstOrDefaultAsync(o => o.CampaignId == campaignId, cancellationToken);
        if (offer != null && offer.CurrentUsageCount > 0)
        {
            offer.CurrentUsageCount = Math.Max(0, offer.CurrentUsageCount - 1);
        }

        var offerRedemption = await _db.OfferRedemptions
            .FirstOrDefaultAsync(r => r.SubscriptionId == sub.Id, cancellationToken);
        if (offerRedemption != null)
        {
            _db.OfferRedemptions.Remove(offerRedemption);
        }

        // Reserve a provider refund; ledger entries are posted only after provider confirmation.
        var refundAmount = (request.IssueRefund && sub.PaidAmount != null) ? sub.PaidAmount.Amount : 0m;
        var currency = sub.PaidAmount?.CurrencyCode ?? "KWD";
        RefundGatewayResult? refund = null;
        if (refundAmount > 0)
        {
            var payment = await _db.CampaignPaymentTransactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CampaignId == campaignId &&
                    (x.ProviderTransactionId == sub.PaymentId || x.ProviderPaymentReference == sub.PaymentId), cancellationToken);
            refund = await _refundGateway.RequestRefundAsync(payment?.PaymentProvider ?? "Unknown",
                payment?.ProviderPaymentReference ?? sub.PaymentId ?? "", refundAmount, currency, request.Reason,
                "campaign-cancel-" + sub.Id, cancellationToken);
        }
        // 4. Update status & write audit log
        sub.Status = SubscriptionStatus.Cancelled;

        _db.CampaignAuditLogs.Add(new CampaignAuditLog
        {
            CampaignId = campaignId,
            ActorId = "System",
            Action = "SubscriptionCancelled",
            JustificationReason = request.Reason,
            DetailsJson = $"{{\"SubscriptionId\":\"{sub.Id}\",\"RefundAmount\":{refundAmount}}}",
            CreatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CancelCampaignSubscriptionResultDto(
            sub.Id,
            campaignId,
            sub.Status.ToString(),
            refundAmount,
            request.Reason, refund?.State.ToString(), refund?.ProviderReference));
    }
}
