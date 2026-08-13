using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Settings;

public sealed class GetMyFatoorahSettingsQueryHandler : IQueryHandler<GetMyFatoorahSettingsQuery, MyFatoorahSettingsDto>
{
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;

    public GetMyFatoorahSettingsQueryHandler(IAppDbContext db, IEmailSecretProtector protector)
        => (_db, _protector) = (db, protector);

    public async Task<Result<MyFatoorahSettingsDto>> Handle(GetMyFatoorahSettingsQuery request, CancellationToken cancellationToken)
    {
        var config = await _db.MyFatoorahGatewayConfigs.FirstOrDefaultAsync(c => c.GatewayName == "MyFatoorah", cancellationToken);
        if (config == null)
        {
            return Result<MyFatoorahSettingsDto>.Success(new MyFatoorahSettingsDto(
                "MyFatoorah",
                true,
                "https://apitest.myfatoorah.com",
                "Not configured",
                "Not configured",
                true,
                true,
                true,
                "KWD",
                "KWT",
                null,
                false,
                false,
                MyFatoorahSettings.WebhookUrl,
                "V2",
                false
            ));
        }

        var token = MyFatoorahSettings.TryReveal(config.EncryptedApiToken, _protector);
        var secret = MyFatoorahSettings.TryReveal(config.EncryptedWebhookSecret, _protector);
        var hasToken = !string.IsNullOrWhiteSpace(token);
        var hasSecret = !string.IsNullOrWhiteSpace(secret);

        return Result<MyFatoorahSettingsDto>.Success(new MyFatoorahSettingsDto(
            config.GatewayName,
            config.IsSandbox,
            config.BaseUrl,
            MyFatoorahSettings.Mask(token),
            MyFatoorahSettings.Mask(secret),
            config.IsKnetEnabled,
            config.IsVisaMasterEnabled,
            config.IsApplePayEnabled,
            config.DefaultCurrency,
            config.CountryCode,
            config.ModifiedAtUtc,
            hasToken,
            hasSecret,
            MyFatoorahSettings.WebhookUrl,
            "V2",
            hasToken && hasSecret
        ));
    }
}

public sealed class UpdateMyFatoorahSettingsCommandHandler : ICommandHandler<UpdateMyFatoorahSettingsCommand, MyFatoorahSettingsDto>
{
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;

    public UpdateMyFatoorahSettingsCommandHandler(IAppDbContext db, IEmailSecretProtector protector)
        => (_db, _protector) = (db, protector);

    public async Task<Result<MyFatoorahSettingsDto>> Handle(UpdateMyFatoorahSettingsCommand request, CancellationToken cancellationToken)
    {
        var config = await _db.MyFatoorahGatewayConfigs.FirstOrDefaultAsync(c => c.GatewayName == "MyFatoorah", cancellationToken);
        var token = !string.IsNullOrWhiteSpace(request.RawApiToken)
            ? request.RawApiToken.Trim()
            : MyFatoorahSettings.TryReveal(config?.EncryptedApiToken, _protector);
        var secret = !string.IsNullOrWhiteSpace(request.RawWebhookSecret)
            ? request.RawWebhookSecret.Trim()
            : MyFatoorahSettings.TryReveal(config?.EncryptedWebhookSecret, _protector);
        if (string.IsNullOrWhiteSpace(token))
            return Error.Validation("myfatoorah.api_token_required", "MyFatoorah API token is required.");
        if (string.IsNullOrWhiteSpace(secret))
            return Error.Validation("myfatoorah.webhook_secret_required", "MyFatoorah webhook secret is required.");

        var encToken = _protector.Encrypt(token);
        var encSecret = _protector.Encrypt(secret);
        var maskedToken = MyFatoorahSettings.Mask(token);
        var baseUrl = request.IsSandbox ? MyFatoorahSettings.SandboxBaseUrl : MyFatoorahSettings.LiveBaseUrl;

        if (config == null)
        {
            config = new MyFatoorahGatewayConfig(
                "MyFatoorah",
                request.IsSandbox,
                baseUrl,
                encToken,
                encSecret,
                request.IsKnetEnabled,
                request.IsVisaMasterEnabled,
                request.IsApplePayEnabled,
                request.DefaultCurrency,
                MyFatoorahSettings.NormalizeCountry(request.CountryCode),
                maskedToken
            );
            _db.MyFatoorahGatewayConfigs.Add(config);
        }
        else
        {
            config.Update(
                request.IsSandbox,
                baseUrl,
                encToken,
                encSecret,
                request.IsKnetEnabled,
                request.IsVisaMasterEnabled,
                request.IsApplePayEnabled,
                request.DefaultCurrency,
                MyFatoorahSettings.NormalizeCountry(request.CountryCode),
                maskedToken
            );
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result<MyFatoorahSettingsDto>.Success(new MyFatoorahSettingsDto(
            config.GatewayName,
            config.IsSandbox,
            config.BaseUrl,
            config.MaskedToken,
            MyFatoorahSettings.Mask(secret),
            config.IsKnetEnabled,
            config.IsVisaMasterEnabled,
            config.IsApplePayEnabled,
            config.DefaultCurrency,
            config.CountryCode,
            config.ModifiedAtUtc,
            true,
            true,
            MyFatoorahSettings.WebhookUrl,
            "V2",
            true
        ));
    }
}

public sealed class TestMyFatoorahConnectionCommandHandler : ICommandHandler<TestMyFatoorahConnectionCommand, bool>
{
    private readonly IMyFatoorahPaymentService _paymentService;
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;

    public TestMyFatoorahConnectionCommandHandler(IMyFatoorahPaymentService paymentService, IAppDbContext db, IEmailSecretProtector protector)
        => (_paymentService, _db, _protector) = (paymentService, db, protector);

    public async Task<Result<bool>> Handle(TestMyFatoorahConnectionCommand request, CancellationToken cancellationToken)
    {
        var token = request.RawApiToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            var config = await _db.MyFatoorahGatewayConfigs.AsNoTracking()
                .FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", cancellationToken);
            token = MyFatoorahSettings.TryReveal(config?.EncryptedApiToken, _protector);
        }
        if (string.IsNullOrWhiteSpace(token))
            return Error.Validation("myfatoorah.api_token_required", "Save an API token before testing the connection.");
        return Result<bool>.Success(await _paymentService.TestConnectionAsync(token, request.IsSandbox, cancellationToken));
    }
}

internal static class MyFatoorahSettings
{
    public const string SandboxBaseUrl = "https://apitest.myfatoorah.com";
    public const string LiveBaseUrl = "https://api.myfatoorah.com";
    public const string WebhookUrl = "https://maelmate.runasp.net/api/v1/webhooks/myfatoorah";

    public static string NormalizeCountry(string? value)
        => string.Equals(value, "KW", StringComparison.OrdinalIgnoreCase) ? "KWT" : (value?.Trim().ToUpperInvariant() ?? "KWT");

    public static string Mask(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Not configured" : value.Length <= 4 ? "****" : $"****{value[^4..]}";

    public static string? TryReveal(string? stored, IEmailSecretProtector protector)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try { return protector.Decrypt(stored); }
        catch { return stored; } // One-time compatibility for values saved before encryption was enforced.
    }
}
