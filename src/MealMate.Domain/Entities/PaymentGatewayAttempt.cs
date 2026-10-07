using MealMate.Domain.Common;
namespace MealMate.Domain.Entities;
public sealed class PaymentGatewayAttempt : AuditableEntity
{
    public string Provider { get; set; } = "Payzah";
    public string TrackId { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public decimal ExpectedAmount { get; set; }
    public string ExpectedCurrency { get; set; } = "KWD";
    public bool IsSandbox { get; set; }
    public string? ProviderReference { get; set; }
}
