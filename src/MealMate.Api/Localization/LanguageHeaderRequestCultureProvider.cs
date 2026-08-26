using Microsoft.AspNetCore.Localization;

namespace MealMate.Api.Localization;

/// <summary>
/// Reads <c>Language</c> or <c>X-Language</c> when clients do not send <c>Accept-Language</c>.
/// </summary>
public sealed class LanguageHeaderRequestCultureProvider : RequestCultureProvider
{
    public const string LanguageHeader = "Language";
    public const string XLanguageHeader = "X-Language";

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var value = FirstHeader(httpContext, LanguageHeader)
                    ?? FirstHeader(httpContext, XLanguageHeader);

        if (string.IsNullOrWhiteSpace(value))
        {
            return NullProviderCultureResult;
        }

        var culture = value.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(culture))
        {
            return NullProviderCultureResult;
        }

        var tag = culture.Split(';', 2)[0].Trim();
        if (tag.Length == 0)
        {
            return NullProviderCultureResult;
        }

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(tag, tag));
    }

    private static string? FirstHeader(HttpContext httpContext, string name)
        => httpContext.Request.Headers.TryGetValue(name, out var values)
            ? values.FirstOrDefault()
            : null;
}
