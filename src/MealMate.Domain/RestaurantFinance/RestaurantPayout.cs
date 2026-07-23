using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.RestaurantFinance;

public sealed class RestaurantPayout : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public int PeriodYear { get; set; }

    public int PeriodMonth { get; set; }

    public Money Amount { get; set; } = default!;

    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;

    public DateTime? PaidAtUtc { get; set; }

    public string? BankReference { get; set; }

    public ICollection<TransferReceipt> TransferReceipts { get; set; } = new List<TransferReceipt>();
}
