using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Settings;
using MealMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class MyFatoorahGatewayBootstrap
{
    public static async Task EnsureConfiguredAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var apiToken = configuration["Payments:BootstrapMyFatoorah:ApiToken"]?.Trim();
        var webhookSecret = configuration["Payments:BootstrapMyFatoorah:WebhookSecret"]?.Trim();
        if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(webhookSecret))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var existing = await db.MyFatoorahGatewayConfigs
            .FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var protector = scope.ServiceProvider.GetRequiredService<IEmailSecretProtector>();
        var isSandbox = configuration.GetValue("Payments:BootstrapMyFatoorah:IsSandbox", true);
        var baseUrl = isSandbox ? MyFatoorahSettings.SandboxBaseUrl : MyFatoorahSettings.LiveBaseUrl;
        var config = new MyFatoorahGatewayConfig(
            "MyFatoorah",
            isSandbox,
            baseUrl,
            protector.Encrypt(apiToken),
            protector.Encrypt(webhookSecret),
            isKnetEnabled: configuration.GetValue("Payments:BootstrapMyFatoorah:IsKnetEnabled", true),
            isVisaMasterEnabled: configuration.GetValue("Payments:BootstrapMyFatoorah:IsVisaMasterEnabled", true),
            isApplePayEnabled: configuration.GetValue("Payments:BootstrapMyFatoorah:IsApplePayEnabled", true),
            defaultCurrency: configuration["Payments:BootstrapMyFatoorah:DefaultCurrency"] ?? "KWD",
            countryCode: MyFatoorahSettings.NormalizeCountry(configuration["Payments:BootstrapMyFatoorah:CountryCode"]),
            maskedToken: MyFatoorahSettings.Mask(apiToken));
        db.MyFatoorahGatewayConfigs.Add(config);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Bootstrapped MyFatoorah gateway configuration from Payments:BootstrapMyFatoorah.");
    }
}
