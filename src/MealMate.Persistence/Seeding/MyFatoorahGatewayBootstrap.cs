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
        var protector = scope.ServiceProvider.GetRequiredService<IEmailSecretProtector>();
        var isSandbox = configuration.GetValue("Payments:BootstrapMyFatoorah:IsSandbox", true);
        var baseUrl = isSandbox ? MyFatoorahSettings.SandboxBaseUrl : MyFatoorahSettings.LiveBaseUrl;
        var maskedToken = MyFatoorahSettings.Mask(apiToken);
        var countryCode = MyFatoorahSettings.NormalizeCountry(configuration["Payments:BootstrapMyFatoorah:CountryCode"]);
        var defaultCurrency = configuration["Payments:BootstrapMyFatoorah:DefaultCurrency"] ?? "KWD";
        var isKnet = configuration.GetValue("Payments:BootstrapMyFatoorah:IsKnetEnabled", true);
        var isVisaMaster = configuration.GetValue("Payments:BootstrapMyFatoorah:IsVisaMasterEnabled", true);
        var isApplePay = configuration.GetValue("Payments:BootstrapMyFatoorah:IsApplePayEnabled", true);

        if (existing is not null)
        {
            existing.Update(
                isSandbox,
                baseUrl,
                protector.Encrypt(apiToken),
                protector.Encrypt(webhookSecret),
                isKnetEnabled: isKnet,
                isVisaMasterEnabled: isVisaMaster,
                isApplePayEnabled: isApplePay,
                defaultCurrency: defaultCurrency,
                countryCode: countryCode,
                maskedToken: maskedToken);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Updated MyFatoorah gateway configuration from Payments:BootstrapMyFatoorah.");
            return;
        }

        var config = new MyFatoorahGatewayConfig(
            "MyFatoorah",
            isSandbox,
            baseUrl,
            protector.Encrypt(apiToken),
            protector.Encrypt(webhookSecret),
            isKnetEnabled: isKnet,
            isVisaMasterEnabled: isVisaMaster,
            isApplePayEnabled: isApplePay,
            defaultCurrency: defaultCurrency,
            countryCode: countryCode,
            maskedToken: maskedToken);
        db.MyFatoorahGatewayConfigs.Add(config);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Bootstrapped MyFatoorah gateway configuration from Payments:BootstrapMyFatoorah.");
    }
}
