using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace MealMate.Application.Features.Settings;
public sealed class GetPayzahSettingsQueryHandler(IAppDbContext db, IEmailSecretProtector protector, IConfiguration? configuration = null)
    : IQueryHandler<GetPayzahSettingsQuery, PayzahSettingsDto>
{
    public async Task<Result<PayzahSettingsDto>> Handle(GetPayzahSettingsQuery request, CancellationToken ct)
        => Result<PayzahSettingsDto>.Success(PayzahSettings.Dto(await db.PayzahGatewayConfigs.SingleOrDefaultAsync(ct), configuration, protector));
}
public sealed class UpdatePayzahSettingsCommandHandler(IAppDbContext db, IEmailSecretProtector protector, IConfiguration? configuration = null)
    : ICommandHandler<UpdatePayzahSettingsCommand, PayzahSettingsDto>
{
    public async Task<Result<PayzahSettingsDto>> Handle(UpdatePayzahSettingsCommand request, CancellationToken ct)
    {
        var config = await db.PayzahGatewayConfigs.SingleOrDefaultAsync(ct);
        if (config is not null && config.IsSandbox != request.IsSandbox && string.IsNullOrWhiteSpace(request.RawPrivateKey))
            return Error.Validation("payzah.private_key_required", "A new private key is required when switching gateway environment.");
        var key = request.RawPrivateKey?.Trim();
        if (string.IsNullOrWhiteSpace(key)) key = PayzahSettings.TryReveal(config?.EncryptedPrivateKey, protector);
        if (string.IsNullOrWhiteSpace(key)) return Error.Validation("payzah.private_key_required", "Payzah private key is required.");
        var baseUrl = request.IsSandbox ? PayzahSettings.SandboxBaseUrl : PayzahSettings.LiveBaseUrl;
        if (config is null) { config = new(request.IsSandbox, baseUrl, protector.Encrypt(key), PayzahSettings.Mask(key)); db.PayzahGatewayConfigs.Add(config); }
        else config.Update(request.IsSandbox, baseUrl, protector.Encrypt(key), PayzahSettings.Mask(key));
        await db.SaveChangesAsync(ct);
        return Result<PayzahSettingsDto>.Success(PayzahSettings.Dto(config, configuration, protector));
    }
}
public sealed class TestPayzahConnectionCommandHandler(IPaymentGatewayService payments, IAppDbContext db, IEmailSecretProtector protector)
    : ICommandHandler<TestPayzahConnectionCommand, bool>
{
    public Task<Result<bool>> Handle(TestPayzahConnectionCommand request, CancellationToken ct)
        => Task.FromResult<Result<bool>>(Error.Validation("payzah.connection_test_unavailable", "Payzah does not document a read-only credentials test. Validate using an authorized sandbox checkout."));
}
public static class PayzahSettings
{
    public const string SandboxBaseUrl = "https://development.payzah.net/ws/paymentgateway";
    public const string LiveBaseUrl = "https://payzah.net/production770/ws/paymentgateway";
    public static string ResolvePublicBaseUrl(IConfiguration? configuration) => configuration?["Payments:PublicBaseUrl"]?.Trim().TrimEnd('/') ?? string.Empty;
    public static string Mask(string? value) => string.IsNullOrWhiteSpace(value) ? "Not configured" : "****" + (value.Length > 4 ? value[^4..] : "");
    public static string? TryReveal(string? stored, IEmailSecretProtector protector) { if (string.IsNullOrWhiteSpace(stored)) return null; try { return protector.Decrypt(stored); } catch { return null; } }
    public static PayzahSettingsDto Dto(PayzahGatewayConfig? config, IConfiguration? configuration, IEmailSecretProtector protector)
        => new("Payzah", config?.IsSandbox ?? true, config?.BaseUrl ?? SandboxBaseUrl, config?.MaskedToken ?? "Not configured", "KWD", "KW",
            config?.ModifiedAtUtc, !string.IsNullOrWhiteSpace(config?.EncryptedPrivateKey), ResolvePublicBaseUrl(configuration) + "/api/v1/payments/payzah/return", !string.IsNullOrWhiteSpace(TryReveal(config?.EncryptedPrivateKey, protector)) && Uri.TryCreate(ResolvePublicBaseUrl(configuration), UriKind.Absolute, out var url) && url.Scheme == "https");
}
