using MealMate.Domain.Common;

namespace MealMate.Domain.Entities;

public sealed class MyFatoorahGatewayConfig : AuditableEntity
{
    public string GatewayName { get; private set; } = "MyFatoorah";
    public bool IsSandbox { get; private set; } = true;
    public string BaseUrl { get; private set; } = "https://apitest.myfatoorah.com";
    public string EncryptedApiToken { get; private set; } = string.Empty;
    public string EncryptedWebhookSecret { get; private set; } = string.Empty;
    public bool IsKnetEnabled { get; private set; } = true;
    public bool IsVisaMasterEnabled { get; private set; } = true;
    public bool IsApplePayEnabled { get; private set; } = true;
    public string DefaultCurrency { get; private set; } = "KWD";
    public string CountryCode { get; private set; } = "KW";
    public string MaskedToken { get; private set; } = string.Empty;

    private MyFatoorahGatewayConfig() { }

    public MyFatoorahGatewayConfig(
        string gatewayName,
        bool isSandbox,
        string baseUrl,
        string encryptedApiToken,
        string encryptedWebhookSecret,
        bool isKnetEnabled,
        bool isVisaMasterEnabled,
        bool isApplePayEnabled,
        string defaultCurrency,
        string countryCode,
        string maskedToken)
    {
        GatewayName = gatewayName;
        IsSandbox = isSandbox;
        BaseUrl = baseUrl;
        EncryptedApiToken = encryptedApiToken;
        EncryptedWebhookSecret = encryptedWebhookSecret;
        IsKnetEnabled = isKnetEnabled;
        IsVisaMasterEnabled = isVisaMasterEnabled;
        IsApplePayEnabled = isApplePayEnabled;
        DefaultCurrency = defaultCurrency;
        CountryCode = countryCode;
        MaskedToken = maskedToken;
    }

    public void Update(
        bool isSandbox,
        string baseUrl,
        string encryptedApiToken,
        string encryptedWebhookSecret,
        bool isKnetEnabled,
        bool isVisaMasterEnabled,
        bool isApplePayEnabled,
        string defaultCurrency,
        string countryCode,
        string maskedToken)
    {
        IsSandbox = isSandbox;
        BaseUrl = baseUrl;
        if (!string.IsNullOrWhiteSpace(encryptedApiToken)) EncryptedApiToken = encryptedApiToken;
        if (!string.IsNullOrWhiteSpace(encryptedWebhookSecret)) EncryptedWebhookSecret = encryptedWebhookSecret;
        IsKnetEnabled = isKnetEnabled;
        IsVisaMasterEnabled = isVisaMasterEnabled;
        IsApplePayEnabled = isApplePayEnabled;
        DefaultCurrency = defaultCurrency;
        CountryCode = countryCode;
        if (!string.IsNullOrWhiteSpace(maskedToken)) MaskedToken = maskedToken;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
