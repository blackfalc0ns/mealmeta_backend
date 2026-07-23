using MealMate.Domain.Common;

namespace MealMate.Domain.CustomerFinance;

public sealed class CustomerBankDetails : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public string Iban { get; set; } = default!;

    public string BankName { get; set; } = default!;

    public string AccountHolderName { get; set; } = default!;

    public bool IsVerified { get; set; }
}
