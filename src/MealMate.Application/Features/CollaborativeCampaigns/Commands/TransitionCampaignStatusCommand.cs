using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

using MealMate.Application.Features.Offers.Services;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record TransitionCampaignStatusCommand(
    Guid CampaignId,
    CampaignStatus TargetStatus,
    string? Reason = null) : ICommand;

public sealed class TransitionCampaignStatusCommandHandler : ICommandHandler<TransitionCampaignStatusCommand>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ICampaignOfferSyncService? _syncService;

    public TransitionCampaignStatusCommandHandler(
        IAppDbContext context,
        ICurrentUser currentUser,
        ICampaignOfferSyncService? syncService = null)
    {
        _context = context;
        _currentUser = currentUser;
        _syncService = syncService;
    }

    public async Task<Result> Handle(
        TransitionCampaignStatusCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        if (request.TargetStatus is CampaignStatus.Cancelled or CampaignStatus.NeedsChanges)
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Error.Validation("Campaign.ReasonRequired", $"Reason is mandatory when transitioning to '{request.TargetStatus}'.");
            }
        }

        var campaign = await _context.Campaigns
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found.");
        }

        var canTransition = campaign.CanTransitionTo(request.TargetStatus);
        if (canTransition.IsFailure)
        {
            return Error.Validation("Campaign.InvalidTransition", canTransition.Error);
        }

        var beforeStatus = campaign.Status;
        var actorId = _currentUser.UserId?.ToString() ?? "system";

        campaign.TransitionTo(request.TargetStatus, actorId, request.Reason ?? string.Empty);

        var auditLog = new CampaignAuditLog
        {
            CampaignId = campaign.Id,
            ActorId = actorId,
            Action = $"StatusChanged:{beforeStatus}->{request.TargetStatus}",
            BeforeStateJson = JsonSerializer.Serialize(new { Status = beforeStatus.ToString() }),
            AfterStateJson = JsonSerializer.Serialize(new { Status = request.TargetStatus.ToString() }),
            JustificationReason = request.Reason,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.CampaignAuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);

        if (_syncService != null)
        {
            await _syncService.SyncCampaignToOfferAsync(campaign, cancellationToken);
        }

        return Result.Success();
    }
}
