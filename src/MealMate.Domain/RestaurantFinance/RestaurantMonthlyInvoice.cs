using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.RestaurantFinance;

public sealed class RestaurantMonthlyInvoice : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public int PeriodYear { get; set; }

    public int PeriodMonth { get; set; }

    public string InvoiceNumber { get; set; } = default!;

    public Money GrossTotal { get; set; } = default!;

    public Money CommissionTotal { get; set; } = default!;

    public Money DeductionsTotal { get; set; } = default!;

    public Money NetTotal { get; set; } = default!;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime? IssuedAtUtc { get; set; }

    public ICollection<RestaurantPartialPayment> PartialPayments { get; set; } = new List<RestaurantPartialPayment>();
}
