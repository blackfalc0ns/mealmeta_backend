using MealMate.Application.Common.Localization;
using Microsoft.AspNetCore.Localization;

namespace MealMate.Api.Localization;

public static class LocalizationExtensions
{
    public static IServiceCollection AddMealMateLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddScoped<ILocalizedMessageProvider, ResxLocalizedMessageProvider>();
        services.AddScoped<ProblemDetailsLocalizer>();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(SupportedCultures.Default);
            options.SupportedCultures = SupportedCultures.All.ToList();
            options.SupportedUICultures = SupportedCultures.All.ToList();

            options.FallBackToParentCultures = true;
            options.FallBackToParentUICultures = true;
            options.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new LanguageHeaderRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        return services;
    }

    public static IApplicationBuilder UseMealMateLocalization(this IApplicationBuilder app)
        => app.UseRequestLocalization();
}
