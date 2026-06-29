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
}
