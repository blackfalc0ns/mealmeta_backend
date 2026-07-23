using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.RestaurantFinance;

public sealed class RestaurantPartialPayment : AuditableEntity
{
    public Guid MonthlyInvoiceId { get; set; }

    public RestaurantMonthlyInvoice MonthlyInvoice { get; set; } = default!;

    public Money Amount { get; set; } = default!;

    public DateTime PaidAtUtc { get; set; }

    public string? Reference { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Succeeded;
}
