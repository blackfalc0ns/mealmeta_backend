using MediatR;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.CollaborativeCampaigns.Events;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.Offers.EventHandlers;

public sealed class SyncCampaignOfferOnStatusChangedHandler : INotificationHandler<DomainEventNotification<CampaignStatusChangedDomainEvent>>
{
    private readonly ICampaignOfferSyncService _syncService;
    private readonly ILogger<SyncCampaignOfferOnStatusChangedHandler>? _logger;

    public SyncCampaignOfferOnStatusChangedHandler(
        ICampaignOfferSyncService syncService,
        ILogger<SyncCampaignOfferOnStatusChangedHandler>? logger = null)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task Handle(DomainEventNotification<CampaignStatusChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var campaignId = notification.DomainEvent.CampaignId;
        var newStatus = notification.DomainEvent.NewStatus;

        _logger?.LogInformation("Syncing Offer for Campaign '{CampaignId}' on status transition to '{NewStatus}'.", campaignId, newStatus);
        await _syncService.SyncCampaignToOfferAsync(campaignId, cancellationToken);
    }
}
