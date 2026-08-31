using System.Globalization;
using MealMate.Application.Common.Localization;
using Microsoft.AspNetCore.Localization;

namespace MealMate.Api.Localization;

/// <summary>
/// Dashboards that need both Arabic and English fields send <c>X-Bilingual: true</c>.
/// Mobile and other clients get a single language from the request culture header.
/// Maps address autofill is always Arabic (Kuwait form fields).
/// </summary>
public sealed class BilingualJsonMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var includeSource = context.Request.Headers.TryGetValue(LocalizedJsonScope.BilingualHeader, out var values)
            && values.Any(IsEnabled);

        var forceArabicMaps = context.Request.Path.StartsWithSegments("/api/v1/maps");
        CultureInfo? previousCulture = null;
        CultureInfo? previousUiCulture = null;
        if (forceArabicMaps)
        {
            previousCulture = CultureInfo.CurrentCulture;
            previousUiCulture = CultureInfo.CurrentUICulture;
            var ar = CultureInfo.GetCultureInfo(SupportedCultures.Arabic);
            CultureInfo.CurrentCulture = ar;
            CultureInfo.CurrentUICulture = ar;
            context.Features.Set<IRequestCultureFeature>(
                new RequestCultureFeature(new RequestCulture(ar), provider: null));
        }

        try
        {
            using var _ = LocalizedJsonScope.Begin(includeSource);
            await next(context);
        }
        finally
        {
            if (previousCulture is not null)
            {
                CultureInfo.CurrentCulture = previousCulture;
            }

            if (previousUiCulture is not null)
            {
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }
    }

    private static bool IsEnabled(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
}
