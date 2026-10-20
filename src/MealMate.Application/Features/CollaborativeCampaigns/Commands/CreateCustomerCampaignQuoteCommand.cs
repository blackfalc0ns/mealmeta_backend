using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using CustomerCampaignQuote = MealMate.Application.Features.CollaborativeCampaigns.DTOs.CustomerCampaignQuote;
using QuoteEntity = MealMate.Domain.CollaborativeCampaigns.CustomerCampaignQuote;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CreateCustomerCampaignQuoteCommand(
    Guid CustomerId,
    Guid CampaignId,
    Guid ProgramId,
    Guid CampaignBundleId,
    int DurationDays,
    DateOnly StartDate,
    Guid AddressId,
    string? PromoCode = null) : ICommand<CustomerCampaignQuote>;

/// <summary>
/// Creates a saved server-side price offer. The price and the service schedule come from the
/// shared evaluation over authorized data; the client's ProgramId must match what the campaign
/// actually references. No capacity is held and no amount is collected by quoting.
/// </summary>
public sealed class CreateCustomerCampaignQuoteCommandHandler
    : ICommandHandler<CreateCustomerCampaignQuoteCommand, CustomerCampaignQuote>
{
    /// <summary>Quote price validity; independent of the reservation hold lifetime.</summary>
    public static readonly TimeSpan QuoteTtl = TimeSpan.FromMinutes(15);

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerCampaignEvaluationService _evaluation;
    private readonly ISubscriptionFlowCouponService _couponService;

    public CreateCustomerCampaignQuoteCommandHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICustomerCampaignEvaluationService evaluation,
        ISubscriptionFlowCouponService couponService)
    {
        _context = context;
        _clock = clock;
        _evaluation = evaluation;
        _couponService = couponService;
    }

    public async Task<Result<CustomerCampaignQuote>> Handle(
        CreateCustomerCampaignQuoteCommand request,
        CancellationToken cancellationToken)
    {
        // An absent AddressId is not a malformed request: evaluation surfaces it as the
        // address_missing gate so browsing-and-fixing stays possible without a 400.
        if (request.StartDate < DateOnly.FromDateTime(_clock.UtcNow))
        {
            return Result.Failure<CustomerCampaignQuote>(
                Error.Validation("Campaign.InvalidStartDate", "The subscription start date cannot be in the past."));
        }

        var evaluation = await _evaluation.EvaluateAsync(
            new CustomerCampaignEvaluationRequest(
                request.CampaignId,
                request.CampaignBundleId,
                request.DurationDays,
                request.CustomerId,
                request.AddressId,
                request.StartDate),
            cancellationToken);

        if (evaluation.IsFailure)
        {
            return Result.Failure<CustomerCampaignQuote>(evaluation.Error!);
        }

        var e = evaluation.Value;

        // The echoed program must be the campaign's own program; clients never define it.
        if (request.ProgramId != e.ProgramId)
        {
            return Result.Failure<CustomerCampaignQuote>(
                Error.Validation("Campaign.SelectionMismatch",
                    "The selected program does not match the program referenced by this campaign bundle."));
        }

        if (e.ServiceDates.Count == 0)
        {
            return Result.Failure<CustomerCampaignQuote>(
                Error.Validation("Campaign.ScheduleUnavailable",
                    "The service schedule for this selection could not be derived."));
        }

        var now = _clock.UtcNow;
        var canCheckout = e.IsCheckoutReady;

        var price = new CustomerCampaignMoney(
            e.Policy is not null ? e.Price.ReferenceCustomerPrice.Amount : null,
            e.Price.CustomerDiscountAmount.Amount,
            e.Price.CampaignCustomerPrice.Amount,
            e.Price.ReferenceCustomerPrice.CurrencyCode,
            e.Policy is not null ? Math.Round(e.Campaign.TotalDiscountRate.Value * 100m, 3) : null,
            e.Policy is not null);

        // Coupon evaluation uses the current individual-flow evaluator with the campaign's own
        // catalog context. A valid code cannot stack on a campaign unless an explicit merge
        // rule allows it — no such rule exists yet, so the code is kept and no discount applied.
        CustomerCampaignCouponStatus? coupon = null;
        if (!string.IsNullOrWhiteSpace(request.PromoCode) && e.Policy is not null && canCheckout)
        {
            var couponEval = await _couponService.EvaluateAsync(
                request.PromoCode,
                request.CustomerId,
                e.ProgramId,
                e.CatalogBundleId,
                request.DurationDays,
                e.Price.CampaignCustomerPrice.Amount,
                cancellationToken,
                subscriptionType: "Individual");

            if (couponEval.Status == SubscriptionCouponStatuses.Valid)
            {
                var combination = new DiscountCombinationPolicy().Evaluate(new DiscountCombinationRequest(
                    BaseAmount: e.Price.ReferenceCustomerPrice.Amount,
                    CampaignAmount: e.Price.CustomerDiscountAmount.Amount,
                    CouponAmount: couponEval.DiscountAmount,
                    ReferralAmount: 0m,
                    CurrencyCode: e.Price.ReferenceCustomerPrice.CurrencyCode,
                    AllowStacking: false));
                if (!combination.IsAllowed)
                {
                    couponEval = couponEval with
                    {
                        Status = SubscriptionCouponStatuses.StackingConflict,
                        DiscountAmount = 0m,
                        MessageAr = "لا يمكن دمج الكوبون مع سعر الحملة.",
                        MessageEn = "The coupon cannot be combined with the campaign price."
                    };
                }
            }

            coupon = new CustomerCampaignCouponStatus(
                couponEval.Code ?? request.PromoCode.Trim(),
                couponEval.Status,
                couponEval.OfferId,
                couponEval.IsValid ? couponEval.DiscountAmount : 0m,
                couponEval.MessageAr,
                couponEval.MessageEn);
        }

        var selection = new CustomerCampaignSelection(
            e.Campaign.Id,
            e.ProgramId,
            e.CampaignBundle.Id,
            request.DurationDays,
            request.StartDate,
            request.AddressId,
            string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim());

        // A selection that is not checkout-ready is answered with blockers and no stored quote.
        if (!canCheckout)
        {
            return Result.Success(new CustomerCampaignQuote(
                Guid.Empty,
                selection,
                price,
                now,
                now,
                CanCheckout: false,
                e.Blocks,
                Coupon: coupon));
        }

        var quote = new QuoteEntity
        {
            CustomerId = request.CustomerId,
            CampaignId = e.Campaign.Id,
            ProgramId = e.ProgramId,
            CampaignBundleId = e.CampaignBundle.Id,
            CatalogBundleId = e.CatalogBundleId,
            DurationDays = request.DurationDays,
            StartDate = request.StartDate,
            AddressId = request.AddressId,
            PromoCode = selection.PromoCode,
            ReferencePrice = e.Price.ReferenceCustomerPrice.Amount,
            DiscountAmount = e.Price.CustomerDiscountAmount.Amount,
            CouponDiscountAmount = coupon?.DiscountAmount ?? 0m,
            FinalPrice = e.Price.CampaignCustomerPrice.Amount,
            CurrencyCode = e.Price.ReferenceCustomerPrice.CurrencyCode,
            CustomerDiscountPercent = price.CustomerDiscountPercent,
            CanShowReferencePrice = price.CanShowReferencePrice,
            PricingPolicyId = e.Policy!.Id,
            PricingPolicyVersion = e.Policy.Version,
            CampaignSnapshotVersion = e.Campaign.CurrentSnapshotVersion,
            ServiceDates = e.ServiceDates.ToArray(),
            ExpiresAtUtc = now.Add(QuoteTtl)
        };

        _context.CustomerCampaignQuotes.Add(quote);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CustomerCampaignQuote(
            quote.Id,
            selection,
            price,
            quote.ExpiresAtUtc,
            now,
            CanCheckout: true,
            Array.Empty<CustomerCampaignBlock>(),
            Coupon: coupon));
    }
}
