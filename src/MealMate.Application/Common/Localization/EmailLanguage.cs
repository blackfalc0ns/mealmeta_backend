using System.Globalization;

namespace MealMate.Application.Common.Localization;

/// <summary>
/// Resolves the two-letter language code used for email template rendering.
/// <para>
/// Resolution order:
/// <list type="number">
///   <item>An explicit supported value (e.g. "ar", "en", "ar-SA", "en-US") wins.</item>
///   <item>When <c>null</c> (absent), <see cref="CultureInfo.CurrentUICulture"/> is consulted.</item>
///   <item>Unsupported or empty values safely fall back to
///         <see cref="SupportedCultures.Default"/> two-letter language.</item>
/// </list>
/// </para>
/// </summary>
public static class EmailLanguage
{
    /// <summary>
    /// Resolves the two-letter ISO language code for email rendering.
    /// </summary>
    /// <param name="explicitLanguage">
    /// The language value explicitly provided by the caller (dashboard selection).
    /// May be <c>null</c>, empty, a two-letter code, or a full culture name such as "ar-SA".
    /// </param>
    /// <returns>A supported two-letter language code ("ar" or "en").</returns>
    public static string Resolve(string? explicitLanguage)
    {
        // Null (absent): the dashboard did not select a language → use ambient UI culture.
        if (explicitLanguage is null)
        {
            return TryMatchSupported(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                   ?? SupportedCultures.Default.TwoLetterISOLanguageName;
        }

        // Empty / whitespace: treat as invalid → default.
        if (string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return SupportedCultures.Default.TwoLetterISOLanguageName;
        }

        // Explicit non-empty value: match or fall back to default.
        return TryMatchSupported(explicitLanguage)
               ?? SupportedCultures.Default.TwoLetterISOLanguageName;
    }

    /// <summary>
    /// Attempts to match <paramref name="language"/> against
    /// <see cref="SupportedCultures.All"/>. Accepts two-letter codes ("ar", "en")
    /// and full culture names ("ar-SA", "en-US").
    /// </summary>
    /// <returns>The matched two-letter code, or <c>null</c> when unsupported.</returns>
    private static string? TryMatchSupported(string language)
    {
        // Fast path: direct two-letter comparison.
        foreach (var culture in SupportedCultures.All)
        {
            if (string.Equals(language, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                return culture.TwoLetterISOLanguageName;
        }

        // Slow path: parse as a full culture name (e.g. "ar-SA", "en-GB").
        try
        {
            var ci = CultureInfo.GetCultureInfo(language);
            var twoLetter = ci.TwoLetterISOLanguageName;

            foreach (var culture in SupportedCultures.All)
            {
                if (string.Equals(twoLetter, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                    return culture.TwoLetterISOLanguageName;
            }
        }
        catch (CultureNotFoundException)
        {
            // Unknown culture → unsupported.
        }

        return null;
    }
}
