using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignFinancialLedger : BaseEntity
{
    public Guid JournalId { get; set; }

    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid? CheckoutSessionId { get; set; }

    public Guid? RestaurantId { get; set; }

    public CampaignLedgerEntryType EntryType { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "KWD";

    public string Description { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? CreatedByUserId { get; set; }
}
