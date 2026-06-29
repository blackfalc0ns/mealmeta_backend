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
            return fallback ?? MessageCodes.Common.Unknown;
        }

        var localized = arguments.Length == 0
            ? _localizer[code]
            : _localizer[code, arguments];

        if (!localized.ResourceNotFound)
        {
            return localized.Value;
        }

        return fallback ?? code;
    }
}
