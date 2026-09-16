using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CollaborativeCampaigns.Events;

public sealed record CampaignStatusChangedDomainEvent(
    Guid CampaignId,
    CampaignStatus OldStatus,
    CampaignStatus NewStatus,
    string ActorId,
    string Reason
) : DomainEvent;
