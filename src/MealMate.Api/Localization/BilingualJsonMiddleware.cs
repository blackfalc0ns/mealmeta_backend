using MealMate.Application.Common.Localization;

namespace MealMate.Api.Localization;

/// <summary>
/// Dashboards that need both Arabic and English fields send <c>X-Bilingual: true</c>.
/// Mobile and other clients get a single language from the request culture header.
/// </summary>
public sealed class BilingualJsonMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var includeSource = context.Request.Headers.TryGetValue(LocalizedJsonScope.BilingualHeader, out var values)
            && values.Any(IsEnabled);

        using var _ = LocalizedJsonScope.Begin(includeSource);
        await next(context);
    }

    private static bool IsEnabled(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
}
