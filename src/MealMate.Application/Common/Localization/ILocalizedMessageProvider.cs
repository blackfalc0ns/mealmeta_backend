using System;
using System.Globalization;

namespace MealMate.Application.Common.Localization;

/// <summary>
/// Resolves user-facing messages from stable codes. Implementations use the
/// current request culture (Accept-Language) set by the API host.
/// </summary>
public interface ILocalizedMessageProvider
{
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Returns the localized string for <paramref name="code"/>, or
    /// <paramref name="fallback"/> / the code itself when no resource exists.
    /// </summary>
    string GetMessage(string code, string? fallback = null, params object[] arguments);

    /// <summary>
    /// Returns the localized string for <paramref name="code"/> using the specified culture.
    /// </summary>
    string GetMessage(string code, CultureInfo culture, string? fallback = null, params object[] arguments)
        => GetMessage(code, fallback, arguments);
}

public static class LocalizedMessageProviderExtensions
{
    public static string GetArabicMessage(
        this ILocalizedMessageProvider provider,
        string code,
        string? fallback = null,
        params object[] arguments)
        => provider.GetMessage(code, SupportedCultures.ArabicCulture, fallback, arguments);

    public static string GetEnglishMessage(
        this ILocalizedMessageProvider provider,
        string code,
        string? fallback = null,
        params object[] arguments)
        => provider.GetMessage(code, SupportedCultures.EnglishCulture, fallback, arguments);
}

public sealed class NullLocalizedMessageProvider : ILocalizedMessageProvider
{
    public static readonly NullLocalizedMessageProvider Instance = new();

    public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

    public string GetMessage(string code, string? fallback = null, params object[] arguments)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return arguments.Length == 0 ? fallback : string.Format(CultureInfo.InvariantCulture, fallback, arguments);
        }
        return code;
    }

    public string GetMessage(string code, CultureInfo culture, string? fallback = null, params object[] arguments)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return arguments.Length == 0 ? fallback : string.Format(culture, fallback, arguments);
        }
        return code;
    }
}
