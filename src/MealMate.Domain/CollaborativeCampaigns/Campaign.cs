using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class Campaign : AuditableEntity
{
    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public Percentage TotalDiscountRate { get; set; } = default!;

    public Percentage MealMateShareRate { get; set; } = default!;

    public Percentage RestaurantShareRate { get; set; } = default!;

    public CampaignFundingMode FundingMode { get; set; } = CampaignFundingMode.Shared;

    public int? MaxSubscribers { get; set; }

    public int CurrentSubscribers { get; set; }

    public DateTime? EnrollmentOpensAtUtc { get; set; }

    public DateTime? EnrollmentClosesAtUtc { get; set; }

    public ICollection<CampaignDiscountSplit> DiscountSplits { get; set; } = new List<CampaignDiscountSplit>();

    public ICollection<CampaignProgram> Programs { get; set; } = new List<CampaignProgram>();

    public ICollection<CampaignBundle> Bundles { get; set; } = new List<CampaignBundle>();

    public ICollection<CampaignRestaurantEnrollment> RestaurantEnrollments { get; set; } = new List<CampaignRestaurantEnrollment>();

    public ICollection<CampaignRestaurantCapacity> RestaurantCapacities { get; set; } = new List<CampaignRestaurantCapacity>();

    public ICollection<CampaignSubscriberCap> SubscriberCaps { get; set; } = new List<CampaignSubscriberCap>();

    public ICollection<CampaignCapacityReview> CapacityReviews { get; set; } = new List<CampaignCapacityReview>();

    public ICollection<CampaignAccountingEntry> AccountingEntries { get; set; } = new List<CampaignAccountingEntry>();
}
