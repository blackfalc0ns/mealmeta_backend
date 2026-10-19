using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCheckout;

namespace MealMate.Application.Features.Offers;

/// <summary>Localized reason text for the offer purchase-options contract.</summary>
public static class OfferPurchaseOptionsLocalization
{
    public static MobileBlockingReasonDto Reason(
        ILocalizedMessageProvider messages,
        string code,
        string? field = null,
        MobileRetryAdviceDto? advice = null)
        => new(
            code,
            messages.GetArabicMessage(code),
            messages.GetEnglishMessage(code),
            field,
            advice);
}
