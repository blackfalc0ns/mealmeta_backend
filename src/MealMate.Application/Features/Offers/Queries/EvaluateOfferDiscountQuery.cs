using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record EvaluateOfferDiscountQuery(
    Guid? OfferId = null,
    Guid CustomerId = default,
    decimal CartAmount = 0m,
    int DurationDays = 26,
    string? DeliveryGovernorate = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    Guid? RestaurantId = null,
    bool HasPromoCode = false,
    string? PromoCode = null
) : IQuery<OfferEvaluationDto>;

public sealed record OfferEvaluationDto(
    Guid OfferId,
    bool IsEligible,
    decimal CalculatedDiscountAmount,
    IReadOnlyList<string> IneligibilityReasons,
    bool AllowPromoCodeStacking,
    string? OfferNameAr = null,
    string? PromoCode = null
);

public sealed class EvaluateOfferDiscountQueryHandler : IQueryHandler<EvaluateOfferDiscountQuery, OfferEvaluationDto>
{
    private readonly IAppDbContext _context;
    private readonly IOfferEligibilityEngine _eligibilityEngine;
    private readonly IDateTimeProvider _clock;

    public EvaluateOfferDiscountQueryHandler(
        IAppDbContext context,
        IOfferEligibilityEngine eligibilityEngine,
        IDateTimeProvider clock)
    {
        _context = context;
        _eligibilityEngine = eligibilityEngine;
        _clock = clock;
    }

    public async Task<Result<OfferEvaluationDto>> Handle(EvaluateOfferDiscountQuery request, CancellationToken cancellationToken)
    {
        Offer? offer = null;
        if (request.OfferId.HasValue && request.OfferId.Value != Guid.Empty)
        {
            offer = await _context.Offers
                .AsNoTracking()
                .Include(o => o.EligiblePrograms)
                .Include(o => o.EligibleBundles)
                .Include(o => o.EligibleRestaurants)
                .FirstOrDefaultAsync(o => o.Id == request.OfferId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            var codeNormalized = request.PromoCode.Trim().ToUpper();
            offer = await _context.Offers
                .AsNoTracking()
                .Include(o => o.EligiblePrograms)
                .Include(o => o.EligibleBundles)
                .Include(o => o.EligibleRestaurants)
                .FirstOrDefaultAsync(o => o.PromoCode != null && o.PromoCode.ToUpper() == codeNormalized, cancellationToken);
        }

        if (offer == null)
        {
            return Error.NotFound("Offer.NotFound", "لم يتم العثور على العرض أو كود الخصم المدخل.");
        }

        var priorSubsCount = await _context.Subscriptions
            .CountAsync(s => s.CustomerUserId == request.CustomerId, cancellationToken);

        var customerRedemptionCount = await _context.OfferRedemptions
            .CountAsync(r => r.OfferId == offer.Id && r.CustomerId == request.CustomerId, cancellationToken);

        var evalCtx = new OfferEligibilityContext(
            request.CustomerId,
            priorSubsCount,
            customerRedemptionCount,
            request.CartAmount,
            request.DurationDays,
            request.DeliveryGovernorate,
            request.ProgramId,
            request.BundleId,
            request.RestaurantId,
            request.HasPromoCode);

        var eval = _eligibilityEngine.Evaluate(offer, evalCtx, _clock.UtcNow);

        return new OfferEvaluationDto(
            offer.Id,
            eval.IsEligible,
            eval.CalculatedDiscountAmount,
            eval.IneligibilityReasons,
            offer.AllowPromoCodeStacking,
            offer.NameAr,
            offer.PromoCode);
    }
}
