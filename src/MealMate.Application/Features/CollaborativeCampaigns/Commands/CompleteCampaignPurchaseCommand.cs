using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CompleteCampaignPurchaseCommand(
    Guid CheckoutSessionId,
    string? DeliveryAddress = null) : ICommand<CompleteCampaignPurchaseResultDto>;

public sealed record CompleteCampaignPurchaseResultDto(
    Guid CheckoutSessionId,
    Guid SubscriptionId,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalAmount);

public sealed class CompleteCampaignPurchaseCommandHandler
    : ICommandHandler<CompleteCampaignPurchaseCommand, CompleteCampaignPurchaseResultDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CompleteCampaignPurchaseCommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CompleteCampaignPurchaseResultDto>> Handle(
        CompleteCampaignPurchaseCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _db.CampaignCheckoutSessions
            .Include(s => s.Allocations)
            .Include(s => s.PaymentTransactions)
            .FirstOrDefaultAsync(s => s.Id == request.CheckoutSessionId, cancellationToken);

        if (session == null)
        {
            return Result.Failure<CompleteCampaignPurchaseResultDto>(
                Error.NotFound("Campaign.SessionNotFound", $"Campaign checkout session '{request.CheckoutSessionId}' was not found."));
        }

        // Idempotency: If already completed, retrieve the existing subscription
        if (session.Status == CampaignCheckoutSessionStatus.Completed)
        {
            var existingSub = await _db.Subscriptions
                .FirstOrDefaultAsync(s => s.CampaignId == session.CampaignId &&
                                          s.CustomerUserId == session.CustomerId &&
                                          s.BundleId == session.BundleId,
                    cancellationToken);

            if (existingSub != null)
            {
                return Result.Success(new CompleteCampaignPurchaseResultDto(
                    session.Id,
                    existingSub.Id,
                    existingSub.Status.ToString(),
                    existingSub.StartDate,
                    existingSub.EndDate,
                    existingSub.PaidAmount?.Amount ?? 0m));
            }
        }

        if (session.Status != CampaignCheckoutSessionStatus.Paid && session.Status != CampaignCheckoutSessionStatus.Committing)
        {
            return Result.Failure<CompleteCampaignPurchaseResultDto>(
                Error.Conflict("Campaign.InvalidSessionState", $"Cannot complete purchase for session in status '{session.Status}'. Session must be Paid."));
        }

        var tx = session.PaymentTransactions.FirstOrDefault(t => t.Status == PaymentTransactionStatus.Succeeded)
                 ?? await _db.CampaignPaymentTransactions
                     .FirstOrDefaultAsync(t => t.CheckoutSessionId == session.Id && t.Status == PaymentTransactionStatus.Succeeded, cancellationToken);

        if (tx == null)
        {
            return Result.Failure<CompleteCampaignPurchaseResultDto>(
                Error.Conflict("Campaign.PaymentMissing", $"No succeeded payment transaction found for session '{session.Id}'."));
        }

        session.MarkCommitting();

        var bundle = await _db.Bundles
            .FirstOrDefaultAsync(b => b.Id == session.BundleId, cancellationToken);

        var dates = session.Allocations
            .Select(a => a.ServiceDate)
            .OrderBy(d => d)
            .ToList();

        var startDate = dates.FirstOrDefault();
        var endDate = dates.LastOrDefault();

        var subscription = new Subscription
        {
            CustomerUserId = session.CustomerId,
            CampaignId = session.CampaignId,
            ProgramId = bundle?.ProgramId ?? Guid.Empty,
            BundleId = session.BundleId,
            DurationDays = dates.Count,
            StartDate = startDate,
            EndDate = endDate,
            Status = SubscriptionStatus.Active,
            DeliveryAddress = request.DeliveryAddress,
            PaidAmount = new Money(tx.Amount, tx.Currency),
            PaymentId = tx.ProviderTransactionId,
            PaymentVerifiedAtUtc = tx.CompletedAtUtc ?? _clock.UtcNow
        };

        _db.Subscriptions.Add(subscription);

        foreach (var alloc in session.Allocations)
        {
            var day = new SubscriptionDay
            {
                SubscriptionId = subscription.Id,
                ServiceDate = alloc.ServiceDate,
                RestaurantId = alloc.RestaurantId,
                Status = SubscriptionDayStatus.AutoSelected
            };

            _db.SubscriptionDays.Add(day);
        }

        session.MarkCompleted();

        // Record Offer Redemption upon verified payment and completion
        var offer = await _db.Offers
            .FirstOrDefaultAsync(o => o.CampaignId == session.CampaignId, cancellationToken);

        if (offer != null)
        {
            var discountRate = offer.DiscountRate?.Value ?? 0m;
            var discountApplied = discountRate > 0 && discountRate < 1m
                ? Math.Round((tx.Amount / (1m - discountRate)) * discountRate, 3)
                : 0m;

            decimal platformFunded = discountApplied;
            decimal restaurantFunded = 0m;
            if (offer.FundingMode == MealMate.Domain.Offers.Enums.OfferFundingMode.RestaurantFull)
            {
                platformFunded = 0m;
                restaurantFunded = discountApplied;
            }
            else if (offer.FundingMode == MealMate.Domain.Offers.Enums.OfferFundingMode.Shared && offer.PlatformShareRate != null && offer.RestaurantShareRate != null)
            {
                var totalRate = offer.PlatformShareRate.Value + offer.RestaurantShareRate.Value;
                if (totalRate > 0)
                {
                    platformFunded = Math.Round(discountApplied * (offer.PlatformShareRate.Value / totalRate), 3);
                    restaurantFunded = Math.Round(discountApplied - platformFunded, 3);
                }
            }

            var redemption = new MealMate.Domain.Offers.OfferRedemption
            {
                OfferId = offer.Id,
                CustomerId = session.CustomerId,
                SubscriptionId = subscription.Id,
                DiscountAppliedAmount = new Money(discountApplied, tx.Currency),
                PlatformFundedAmount = new Money(platformFunded, tx.Currency),
                RestaurantFundedAmount = new Money(restaurantFunded, tx.Currency),
                RedeemedAtUtc = _clock.UtcNow
            };

            offer.CurrentUsageCount++;
            _db.OfferRedemptions.Add(redemption);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CompleteCampaignPurchaseResultDto(
            session.Id,
            subscription.Id,
            subscription.Status.ToString(),
            startDate,
            endDate,
            tx.Amount));
    }
}
