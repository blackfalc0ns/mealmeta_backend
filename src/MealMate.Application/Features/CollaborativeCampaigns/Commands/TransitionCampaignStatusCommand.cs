using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
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
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICampaignOfferSyncService? _syncService;

    public TransitionCampaignStatusCommandHandler(
        IAppDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ICampaignOfferSyncService? syncService = null)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _syncService = syncService;
    }

    public TransitionCampaignStatusCommandHandler(
        IAppDbContext context,
        ICurrentUser currentUser,
        ICampaignOfferSyncService? syncService = null)
        : this(context, currentUser, new SystemDateTimeProvider(), syncService)
    {
    }

    private sealed class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
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

        if (request.TargetStatus == CampaignStatus.OpenForJoining)
        {
            var canOpen = campaign.CanOpenForJoining();
            if (canOpen.IsFailure)
            {
                var errorCode = !campaign.ReviewStartsAtUtc.HasValue && !campaign.ReviewEndsAtUtc.HasValue
                    ? MessageCodes.Campaign.ReviewWindowMissing
                    : (!campaign.ReviewStartsAtUtc.HasValue || !campaign.ReviewEndsAtUtc.HasValue)
                        ? MessageCodes.Campaign.ReviewWindowIncomplete
                        : (!campaign.EnrollmentOpensAtUtc.HasValue || !campaign.EnrollmentClosesAtUtc.HasValue)
                            ? MessageCodes.Campaign.EnrollmentDatesRequired
                            : MessageCodes.Campaign.ReviewWindowOrderingInvalid;

                return Error.Validation(errorCode, canOpen.Error ?? "Campaign cannot be opened for joining.");
            }
        }

        if (request.TargetStatus == CampaignStatus.Scheduled)
        {
            var now = _dateTimeProvider.UtcNow;
            var canApprove = campaign.CanApproveAt(now);
            if (canApprove.IsFailure)
            {
                var errorCode = !campaign.ReviewStartsAtUtc.HasValue || !campaign.ReviewEndsAtUtc.HasValue
                    ? MessageCodes.Campaign.ReviewWindowMissing
                    : now < campaign.ReviewStartsAtUtc.Value
                        ? MessageCodes.Campaign.ReviewWindowNotStarted
                        : MessageCodes.Campaign.ReviewWindowExpired;

                return Error.Validation(errorCode, canApprove.Error ?? "Campaign cannot be approved at this time.");
            }
        }

        if (request.TargetStatus == CampaignStatus.Active && campaign.Status == CampaignStatus.Scheduled)
        {
            var canActivate = campaign.CanActivate();
            if (canActivate.IsFailure)
            {
                return Error.Validation(MessageCodes.Campaign.ApprovalStale, canActivate.Error ?? "Campaign approval is stale.");
            }
        }

        var beforeStatus = campaign.Status;
        var actorId = _currentUser.UserId?.ToString() ?? "system";

        // Record the explicit approval while still UnderReview, then transition.
        if (request.TargetStatus == CampaignStatus.Scheduled && _currentUser.UserId.HasValue)
        {
            var approveResult = campaign.Approve(_currentUser.UserId.Value, _dateTimeProvider.UtcNow);
            if (approveResult.IsFailure)
            {
                return Error.Validation(MessageCodes.Campaign.ReviewWindowNotOpen, approveResult.Error ?? "Campaign cannot be approved at this time.");
            }
        }

        campaign.TransitionTo(request.TargetStatus, actorId, request.Reason ?? string.Empty);

        var auditLog = new CampaignAuditLog
        {
            CampaignId = campaign.Id,
            ActorId = actorId,
            Action = $"StatusChanged:{beforeStatus}->{request.TargetStatus}",
            BeforeStateJson = JsonSerializer.Serialize(new { Status = beforeStatus.ToString() }),
            AfterStateJson = JsonSerializer.Serialize(new
            {
                Status = request.TargetStatus.ToString(),
                campaign.ApprovedSnapshotVersion,
                campaign.ApprovedByUserId,
                campaign.ApprovedAtUtc
            }),
            JustificationReason = request.Reason,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.CampaignAuditLogs.Add(auditLog);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(MessageCodes.Campaign.ConcurrentModification,
                "The campaign was modified by someone else. Reload and retry.");
        }

        if (_syncService != null)
        {
            await _syncService.SyncCampaignToOfferAsync(campaign, cancellationToken);
        }

        return Result.Success();
    }
}
