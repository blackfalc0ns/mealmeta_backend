using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Infrastructure.Auth;
using MealMate.Infrastructure.Email;
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

        // Email secret protector (requires Email:SecretsEncryptionKey in configuration)
        services.AddSingleton<IEmailSecretProtector, EmailSecretProtector>();

        // Resend HTTP client
        services.AddHttpClient<ITransactionalEmailSender, ResendTransactionalEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MealMate/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        if (environment.IsDevelopment())
        {
            services.AddScoped<IOtpDeliveryService, LoggingOtpDeliveryService>();
        }
        else
        {
            // Non-dev environments use Resend for email OTP delivery; never logs OTP codes.
            services.AddScoped<IOtpDeliveryService, ResendOtpDeliveryService>();
        }

        services.AddScoped<IFileStorage, LocalFileStorage>();

        return services;
    }
}
