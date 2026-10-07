using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Settings;
using MealMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace MealMate.Persistence.Seeding;
public static class PayzahGatewayBootstrap
{
    public static async Task EnsureConfiguredAsync(IServiceProvider services, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var key = configuration["Payments:BootstrapPayzah:PrivateKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(key)) return;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        if (await db.PayzahGatewayConfigs.AnyAsync(cancellationToken)) return;
        var protector = scope.ServiceProvider.GetRequiredService<IEmailSecretProtector>();
        var sandbox = configuration.GetValue("Payments:BootstrapPayzah:IsSandbox", true);
        db.PayzahGatewayConfigs.Add(new PayzahGatewayConfig(sandbox, sandbox ? PayzahSettings.SandboxBaseUrl : PayzahSettings.LiveBaseUrl,
            protector.Encrypt(key), PayzahSettings.Mask(key)));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Payzah private key configuration initialized.");
    }
}
