using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class FamilyGroup : AuditableEntity
{
    public Guid ManagerCustomerUserId { get; set; }
    public string Name { get; set; } = default!;
    public FamilyGroupStatus Status { get; set; } = FamilyGroupStatus.Draft;
    public Guid? SelectedFamilyPackageId { get; set; }
    public decimal DiscountPercentSnapshot { get; set; }
    public decimal QuotedFinalPrice { get; set; }
    public string CurrencyCode { get; set; } = "KWD";
    public string? PaymentInvoiceId { get; set; }
    public string? PaymentId { get; set; }
    public Guid? PaymentAnchorSubscriptionId { get; set; }
    public Guid? ConvertedFromSubscriptionId { get; set; }
    public Guid? ManagerIndividualSubscriptionId { get; set; }
    public bool? ManagerIndividualSeatExempt { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public ICollection<FamilyMember> Members { get; set; } = new List<FamilyMember>();
}
