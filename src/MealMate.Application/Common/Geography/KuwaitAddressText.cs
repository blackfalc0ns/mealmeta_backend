namespace MealMate.Application.Common.Geography;

/// <summary>
/// Kuwait address text helpers for Google route strings like "شارع 2 جادة 2".
/// </summary>
public static class KuwaitAddressText
{
    /// <summary>
    /// Splits a Google route / street string into street + avenue when both appear together.
    /// Always normalizes English Street/Avenue labels to Arabic شارع/جادة.
    /// </summary>
    public static (string? Street, string? Avenue) SplitStreetAndAvenue(string? routeOrStreet)
    {
        if (string.IsNullOrWhiteSpace(routeOrStreet))
        {
            return (null, null);
        }

        var text = routeOrStreet.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"^(?<street>.+?)\s+(?<avenue>(?:جادة|جاده|جادّة|avenue|ave\.?)\s*.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant
            | System.Text.RegularExpressions.RegexOptions.Singleline);

        if (!match.Success)
        {
            return (ToArabicStreet(text), null);
        }

        var street = match.Groups["street"].Value.Trim();
        var avenue = match.Groups["avenue"].Value.Trim();
        if (string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(avenue))
        {
            return (ToArabicStreet(text), null);
        }

        return (ToArabicStreet(street), ToArabicAvenue(avenue));
    }

    /// <summary>Normalizes "Street 2" / "St 2" → "شارع 2".</summary>
    public static string? ToArabicStreet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\bstreets?\b|\bst\.?(?=\s|$)",
            "شارع",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return CollapseSpaces(text);
    }

    /// <summary>Normalizes "Avenue 2" / "Ave 2" / "جاده 2" → "جادة 2".</summary>
    public static string? ToArabicAvenue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\bavenues?\b|\bave\.?(?=\s|$)",
            "جادة",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"جاد[هّ]",
            "جادة");
        return CollapseSpaces(text);
    }

    private static string CollapseSpaces(string value)
        => System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
}
