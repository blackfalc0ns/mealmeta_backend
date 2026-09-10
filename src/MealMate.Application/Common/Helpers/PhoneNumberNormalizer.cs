using System.Text;

namespace MealMate.Application.Common.Helpers;

public static class PhoneNumberNormalizer
{
    /// <summary>
    /// Normalizes a phone number to standard E.164-like form:
    /// - Strips whitespace, hyphens, parentheses, and dots.
    /// - Replaces leading international prefix "00" with "+".
    /// </summary>
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var trimmed = phone.Trim();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsDigit(ch) || ch == '+')
            {
                sb.Append(ch);
            }
        }

        var cleaned = sb.ToString();
        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        if (cleaned.StartsWith("00", StringComparison.Ordinal))
        {
            cleaned = "+" + cleaned[2..];
        }

        return cleaned;
    }

    /// <summary>
    /// Generates candidate phone representations for querying user databases where
    /// numbers might have been stored with or without "+" or with country codes.
    /// </summary>
    public static IReadOnlyList<string> GetLookupCandidates(string? phone)
    {
        var normalized = Normalize(phone);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalized
        };

        if (normalized.StartsWith("+", StringComparison.Ordinal))
        {
            var withoutPlus = normalized[1..];
            set.Add(withoutPlus);
            set.Add("00" + withoutPlus);
        }
        else
        {
            set.Add("+" + normalized);
            set.Add("00" + normalized);

            // Kuwait local 8-digit mobile numbers start with 5, 6, or 9
            if (normalized.Length == 8 && (normalized.StartsWith("5") || normalized.StartsWith("6") || normalized.StartsWith("9")))
            {
                set.Add("+965" + normalized);
                set.Add("965" + normalized);
                set.Add("00965" + normalized);
            }
        }

        return set.ToList();
    }
}
