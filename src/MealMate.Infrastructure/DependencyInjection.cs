using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Infrastructure.Auth;
using MealMate.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MealMate.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<ITokenService, TokenService>();

        if (environment.IsDevelopment())
        {
            services.AddScoped<IOtpDeliveryService, LoggingOtpDeliveryService>();
        }
        else
        {
            // Non-dev environments must not log OTP codes; wire SMS/email delivery here.
            services.AddScoped<IOtpDeliveryService, SilentOtpDeliveryService>();
        }

        services.AddScoped<IFileStorage, LocalFileStorage>();

        services.AddHttpClient<IMyFatoorahPaymentService, MyFatoorahPaymentService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
