using MealMate.Domain.Common;
namespace MealMate.Domain.Entities;
public sealed class PayzahGatewayConfig : AuditableEntity
{
    public string GatewayName { get; private set; } = "Payzah";
    public bool IsSandbox { get; private set; } = true;
    public string BaseUrl { get; private set; } = string.Empty;
    public string EncryptedPrivateKey { get; private set; } = string.Empty;
    public string DefaultCurrency { get; private set; } = "KWD";
    public string CountryCode { get; private set; } = "KW";
    public string MaskedToken { get; private set; } = string.Empty;
    private PayzahGatewayConfig() { }
    public PayzahGatewayConfig(bool sandbox, string baseUrl, string encryptedPrivateKey, string maskedKey)
        => Update(sandbox, baseUrl, encryptedPrivateKey, maskedKey);
    public void Update(bool sandbox, string baseUrl, string encryptedPrivateKey, string maskedKey)
    {
        IsSandbox = sandbox; BaseUrl = baseUrl; EncryptedPrivateKey = encryptedPrivateKey; MaskedToken = maskedKey;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
