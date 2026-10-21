using MealMate.Domain.Common;

namespace MealMate.Domain.CentralAccounting;

public sealed class AccountingPeriod : AuditableEntity
{
    public Guid LegalEntityId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDateExclusive { get; set; }
    public AccountingPeriodState State { get; set; } = AccountingPeriodState.Open;
    public long CloseVersion { get; set; }
    public DateTime? PendingCloseAtUtc { get; set; }
    public Guid? PendingCloseByUserId { get; set; }
    public string? PendingCloseReason { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string? CloseReason { get; set; }
    public long? ClosePostedJournalCount { get; set; }
    public decimal? CloseDebitTotal { get; set; }
    public decimal? CloseCreditTotal { get; set; }
    public DateTime? CloseWatermarkUtc { get; set; }

    public bool Contains(DateOnly date) => date >= StartDate && date < EndDateExclusive;
}
