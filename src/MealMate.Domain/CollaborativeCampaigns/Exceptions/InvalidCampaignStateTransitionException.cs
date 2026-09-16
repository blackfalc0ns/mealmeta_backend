using MealMate.Domain.Enums;
using MealMate.Domain.Exceptions;

namespace MealMate.Domain.CollaborativeCampaigns.Exceptions;

public sealed class InvalidCampaignStateTransitionException : DomainException
{
    public CampaignStatus FromStatus { get; }
    public CampaignStatus ToStatus { get; }

    public InvalidCampaignStateTransitionException(CampaignStatus fromStatus, CampaignStatus toStatus)
        : base("INVALID_CAMPAIGN_STATE_TRANSITION", $"Cannot transition campaign from '{fromStatus}' to '{toStatus}'.")
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
    }
}
