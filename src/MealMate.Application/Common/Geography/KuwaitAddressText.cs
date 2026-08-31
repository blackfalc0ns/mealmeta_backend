namespace MealMate.Application.Common.Geography;

/// <summary>
/// Kuwait address text helpers for Google route strings like "شارع 2 جادة 2".
/// </summary>
public static class KuwaitAddressText
{
    /// <summary>
    /// Splits a Google route / street string into street + avenue when both appear together.
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
            return (text, null);
        }

        var street = match.Groups["street"].Value.Trim();
        var avenue = match.Groups["avenue"].Value.Trim();
        if (string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(avenue))
        {
            return (text, null);
        }

        return (street, avenue);
    }
}
