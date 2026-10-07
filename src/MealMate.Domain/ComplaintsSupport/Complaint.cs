using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class Complaint : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? RestaurantId { get; set; }

    public Guid? DriverProfileId { get; set; }

    /// <summary>
    /// Optional driver-reported location carried by driver reassignment requests
    /// (screen 08.02): where the driver is stuck and where the box can be handed
    /// over. Never the customer's destination coordinates — those live on TripStop.
    /// </summary>
    public decimal? Latitude { get; set; }

    /// <inheritdoc cref="Latitude" />
    public decimal? Longitude { get; set; }

    public ComplaintStatus Status { get; set; } = ComplaintStatus.Submitted;

    public string Category { get; set; } = default!;

    public string Title { get; set; } = default!;

    public string Description { get; set; } = default!;

    public DateTime SubmittedAtUtc { get; set; }

    public ICollection<ComplaintEvidencePhoto> EvidencePhotos { get; set; } = new List<ComplaintEvidencePhoto>();

    public ICollection<ComplaintClassification> Classifications { get; set; } = new List<ComplaintClassification>();

    public ICollection<AdminComplaintReview> AdminReviews { get; set; } = new List<AdminComplaintReview>();

    public ICollection<RestaurantComplaintResponse> RestaurantResponses { get; set; } = new List<RestaurantComplaintResponse>();

    public ICollection<ResponsibilityDecision> ResponsibilityDecisions { get; set; } = new List<ResponsibilityDecision>();

    public ICollection<CustomerCompensation> Compensations { get; set; } = new List<CustomerCompensation>();

    public ICollection<RestaurantBoxDeduction> BoxDeductions { get; set; } = new List<RestaurantBoxDeduction>();

    public ICollection<FreeDayCompensation> FreeDayCompensations { get; set; } = new List<FreeDayCompensation>();
}
