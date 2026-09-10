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

    private static readonly string[] KnownCountryPrefixes = ["965", "966", "971", "974", "973", "968", "20", "962", "961", "963", "964", "970", "967", "212", "213", "216", "218", "249"];

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

        var digitsOnly = normalized.StartsWith('+') ? normalized[1..] : normalized;
        set.Add(digitsOnly);
        set.Add("+" + digitsOnly);
        set.Add("00" + digitsOnly);

        foreach (var prefix in KnownCountryPrefixes)
        {
            if (digitsOnly.StartsWith(prefix, StringComparison.Ordinal) && digitsOnly.Length > prefix.Length)
            {
                var national = digitsOnly[prefix.Length..];
                set.Add(national);
                set.Add("0" + national);
                set.Add("+" + digitsOnly);
                set.Add("00" + digitsOnly);
            }
        }

        var trimmedLeadingZero = digitsOnly.TrimStart('0');
        if (!string.IsNullOrWhiteSpace(trimmedLeadingZero))
        {
            set.Add(trimmedLeadingZero);
            set.Add("0" + trimmedLeadingZero);

            foreach (var prefix in KnownCountryPrefixes)
            {
                set.Add("+" + prefix + trimmedLeadingZero);
                set.Add(prefix + trimmedLeadingZero);
                set.Add("00" + prefix + trimmedLeadingZero);
            }
        }

        return set.ToList();
    }

    /// <summary>
    /// Formats phone number into standard international format e.g. "+965xxxxxxxx" for Kuwait.
    /// If an 8-digit Kuwait number starting with 2, 4, 5, 6, 9 is provided, "+965" is prepended.
    /// If an 11-digit number starting with "965" is provided without "+", "+" is prepended.
    /// </summary>
    public static string? ToInternationalFormat(string? phone)
    {
        var normalized = Normalize(phone);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (normalized.StartsWith("+", StringComparison.Ordinal))
            return normalized;

        if (normalized.StartsWith("965", StringComparison.Ordinal) && normalized.Length == 11)
            return "+" + normalized;

        if (normalized.Length == 8 && (normalized.StartsWith('2') || normalized.StartsWith('4') || normalized.StartsWith('5') || normalized.StartsWith('6') || normalized.StartsWith('9')))
            return "+965" + normalized;

        return "+" + normalized;
    }

    /// <summary>
    /// Extracts the country code (e.g. "+965") and national number (e.g. "50000002").
    /// </summary>
    public static (string? CountryCode, string? NationalNumber) ExtractCountryCodeAndNationalNumber(string? phone)
    {
        var formatted = ToInternationalFormat(phone);
        if (string.IsNullOrWhiteSpace(formatted))
            return (null, null);

        if (formatted.StartsWith("+965", StringComparison.Ordinal) && formatted.Length >= 5)
        {
            return ("+965", formatted[4..]);
        }

        if (formatted.StartsWith("+20", StringComparison.Ordinal) && formatted.Length >= 4)
        {
            return ("+20", formatted[3..]);
        }

        if (formatted.StartsWith("+", StringComparison.Ordinal) && formatted.Length > 4)
        {
            if (formatted.StartsWith("+966", StringComparison.Ordinal) || formatted.StartsWith("+971", StringComparison.Ordinal)
                || formatted.StartsWith("+973", StringComparison.Ordinal) || formatted.StartsWith("+974", StringComparison.Ordinal)
                || formatted.StartsWith("+968", StringComparison.Ordinal))
            {
                return (formatted[..4], formatted[4..]);
            }

            return (formatted[..4], formatted[4..]);
        }

        return (null, formatted);
    }
}

