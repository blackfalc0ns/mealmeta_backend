using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

/// <summary>
/// Admin-only rescheduling of the review window (and optionally the sales window).
/// Rescheduling invalidates any stored approval and re-notifies affected restaurants;
/// the review must be re-approved under the new dates.
/// </summary>
public sealed record RescheduleCampaignReviewCommand(
    Guid CampaignId,
    DateTime ReviewStartsAtUtc,
    DateTime ReviewEndsAtUtc,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Reason,
    int ExpectedSnapshotVersion) : ICommand;

public sealed class RescheduleCampaignReviewCommandHandler : ICommandHandler<RescheduleCampaignReviewCommand>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RescheduleCampaignReviewCommandHandler(
        IAppDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(RescheduleCampaignReviewCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation("Campaign.ReasonRequired", "A reason is mandatory when rescheduling a review window.");
        }

        var campaign = await _context.Campaigns
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found.");
        }

        if (campaign.CurrentSnapshotVersion != request.ExpectedSnapshotVersion)
        {
            return Error.Conflict(MessageCodes.Campaign.ConcurrentModification,
                $"Expected snapshot version {request.ExpectedSnapshotVersion} but campaign is at v{campaign.CurrentSnapshotVersion}.");
        }

        if (campaign.Status is not (CampaignStatus.UnderReview or CampaignStatus.JoiningClosed or CampaignStatus.NeedsChanges))
        {
            return Error.Validation("Campaign.InvalidTransition",
                $"Review rescheduling is only allowed before the campaign is scheduled or active (current status '{campaign.Status}').");
        }

        var previous = new
        {
            campaign.StartsAtUtc,
            campaign.EndsAtUtc,
            campaign.ReviewStartsAtUtc,
            campaign.ReviewEndsAtUtc,
            campaign.ApprovedSnapshotVersion
        };

        campaign.StartsAtUtc = request.StartsAtUtc;
        campaign.EndsAtUtc = request.EndsAtUtc;
        campaign.ReviewStartsAtUtc = request.ReviewStartsAtUtc;
        campaign.ReviewEndsAtUtc = request.ReviewEndsAtUtc;

        var ordering = Campaign.ValidateScheduleOrdering(
            campaign.StartsAtUtc,
            campaign.EndsAtUtc,
            campaign.EnrollmentOpensAtUtc,
            campaign.EnrollmentClosesAtUtc,
            campaign.ReviewStartsAtUtc,
            campaign.ReviewEndsAtUtc,
            requireComplete: true);

        if (ordering.IsFailure)
        {
            // Restore previous values; a failed reschedule never leaves a half-applied schedule.
            campaign.StartsAtUtc = previous.StartsAtUtc;
            campaign.EndsAtUtc = previous.EndsAtUtc;
            campaign.ReviewStartsAtUtc = previous.ReviewStartsAtUtc;
            campaign.ReviewEndsAtUtc = previous.ReviewEndsAtUtc;
            return Error.Validation(MessageCodes.Campaign.ReviewWindowOrderingInvalid, ordering.Error ?? "Invalid schedule ordering.");
        }

        campaign.BumpSnapshotVersion();

        var actorId = _currentUser.UserId?.ToString() ?? "system";
        _context.CampaignAuditLogs.Add(new CampaignAuditLog
        {
            CampaignId = campaign.Id,
            ActorId = actorId,
            Action = "ReviewRescheduled",
            BeforeStateJson = JsonSerializer.Serialize(previous),
            AfterStateJson = JsonSerializer.Serialize(new
            {
                campaign.StartsAtUtc,
                campaign.EndsAtUtc,
                campaign.ReviewStartsAtUtc,
                campaign.ReviewEndsAtUtc,
                campaign.CurrentSnapshotVersion
            }),
            JustificationReason = request.Reason,
            CreatedAtUtc = DateTime.UtcNow
        });

        // Re-notify accepted participants: the terms they accepted now carry a new version.
        var acceptedRestaurantIds = await _context.CampaignRestaurantEnrollments
            .Where(e => e.CampaignId == campaign.Id && e.ParticipationStatus == Domain.CollaborativeCampaigns.Enums.CampaignRestaurantParticipationStatus.Accepted)
            .Select(e => e.RestaurantId)
            .ToListAsync(cancellationToken);

        _context.OutboxMessages.Add(new Domain.Common.OutboxMessage
        {
            EventType = "CampaignReviewRescheduled",
            DeduplicationKey = $"campaign-review-rescheduled:{campaign.Id}:v{campaign.CurrentSnapshotVersion}",
            AggregateType = nameof(Campaign),
            AggregateId = campaign.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new
            {
                CampaignId = campaign.Id,
                campaign.NameAr,
                campaign.NameEn,
                campaign.ReviewStartsAtUtc,
                campaign.ReviewEndsAtUtc,
                campaign.StartsAtUtc,
                campaign.EndsAtUtc,
                campaign.CurrentSnapshotVersion,
                AffectedRestaurantIds = acceptedRestaurantIds,
                Reason = request.Reason
            }),
            CreatedAtUtc = _dateTimeProvider.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(MessageCodes.Campaign.ConcurrentModification,
                "The campaign was modified by someone else. Reload and retry.");
        }

        return Result.Success();
    }
}
