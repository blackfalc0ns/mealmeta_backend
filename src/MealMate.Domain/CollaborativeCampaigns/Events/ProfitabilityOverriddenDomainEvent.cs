using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns.Events;

public sealed record ProfitabilityOverriddenDomainEvent(
    Guid CampaignId,
    Guid ApproverUserId,
    decimal ExpectedMarginPct,
    string JustificationReason,
    DateTime OverriddenAtUtc) : DomainEvent;
