using System.Globalization;
using MealMate.Api.Resources;
using MealMate.Application.Common.Localization;
using Microsoft.Extensions.Localization;

namespace MealMate.Api.Localization;

public sealed class ResxLocalizedMessageProvider : ILocalizedMessageProvider
{
    private readonly IStringLocalizer<Messages> _localizer;

    public ResxLocalizedMessageProvider(IStringLocalizer<Messages> localizer) => _localizer = localizer;

    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;

    public string GetMessage(string code, string? fallback = null, params object[] arguments)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return _localizer[MessageCodes.Common.Unknown].Value;
        }

        var localized = arguments.Length == 0
            ? _localizer[code]
            : _localizer[code, arguments];

        if (!localized.ResourceNotFound)
        {
            return localized.Value;
        }

        // Never return a description supplied by application/domain code. The
        // resource file is the only source of user-facing translations.
        return _localizer[MessageCodes.Common.Unknown].Value;
    }

    public string GetMessage(string code, CultureInfo culture, string? fallback = null, params object[] arguments)
    {
        var prevCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            return GetMessage(code, fallback, arguments);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevCulture;
        }
    }
}
