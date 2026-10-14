using System.Globalization;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyPaymentAllocation(Guid MemberId, decimal PriceBeforeFamilyDiscount,
    decimal FamilyDiscountAllocated, decimal PromoCodeDiscountAllocated, decimal NetPaidAmount);

public interface IFamilyPaymentAllocationService
{
    IReadOnlyList<FamilyPaymentAllocation> Allocate(IReadOnlyList<FamilyQuoteLine> lines, decimal finalPrice, int currencyMinorDigits);
}

public sealed class FamilyPaymentAllocationService : IFamilyPaymentAllocationService
{
    public IReadOnlyList<FamilyPaymentAllocation> Allocate(IReadOnlyList<FamilyQuoteLine> lines, decimal finalPrice, int currencyMinorDigits)
    {
        if (currencyMinorDigits is < 0 or > 4 || finalPrice < 0 || lines.Any(x => x.MemberPrice < 0 || x.PromoDiscount < 0))
            throw new ArgumentOutOfRangeException(nameof(finalPrice));
        var scale = (decimal)Math.Pow(10, currencyMinorDigits);
        var gross = lines.Sum(x => x.MemberPrice);
        var promoTotal = lines.Sum(x => x.PromoDiscount);
        var familyDiscount = gross - finalPrice - promoTotal;
        if (finalPrice > gross || familyDiscount < 0 || finalPrice * scale != decimal.Truncate(finalPrice * scale)
            || lines.Any(x => x.MemberPrice * scale != decimal.Truncate(x.MemberPrice * scale)
                || x.PromoDiscount * scale != decimal.Truncate(x.PromoDiscount * scale)))
            throw new ArgumentException("Amounts must match the currency precision and payable subtotal.");
        if (gross == 0)
            return lines.Select(x => new FamilyPaymentAllocation(x.MemberId, 0, 0, 0, 0)).ToList();
        var parts = lines.Select(x => new { Line = x, Exact = familyDiscount * scale * x.MemberPrice / gross }).ToList();
        var units = parts.ToDictionary(x => x.Line.MemberId, x => decimal.Floor(x.Exact));
        var remaining = (familyDiscount * scale) - units.Values.Sum();
        foreach (var part in parts.OrderByDescending(x => x.Exact - decimal.Floor(x.Exact)).ThenBy(x => x.Line.MemberId).Take((int)remaining))
            units[part.Line.MemberId]++;
        return lines.Select(x => new FamilyPaymentAllocation(x.MemberId, x.MemberPrice,
            units[x.MemberId] / scale, x.PromoDiscount,
            x.MemberPrice - units[x.MemberId] / scale - x.PromoDiscount)).ToList();
    }

    public static int CurrencyMinorDigits(string currency)
    {
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            if (new RegionInfo(culture.Name).ISOCurrencySymbol.Equals(currency, StringComparison.OrdinalIgnoreCase))
                return culture.NumberFormat.CurrencyDecimalDigits;
        }
        throw new ArgumentException("Unknown currency precision.", nameof(currency));
    }
}
