using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CloseCampaignJoiningCommand(
    Guid CampaignId,
    string? Reason = null) : ICommand<CampaignOfferSnapshot>;

public sealed class CloseCampaignJoiningCommandHandler : ICommandHandler<CloseCampaignJoiningCommand, CampaignOfferSnapshot>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;

    public CloseCampaignJoiningCommandHandler(IAppDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<CampaignOfferSnapshot>> Handle(
        CloseCampaignJoiningCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        var campaign = await _context.Campaigns
            .Include(c => c.RestaurantEnrollments)
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Error.NotFound("Campaign.NotFound", $"Campaign '{request.CampaignId}' was not found.");
        }

        if (campaign.Status != CampaignStatus.OpenForJoining)
        {
            return Error.Validation("Campaign.InvalidStatusForClosingJoining",
                $"Cannot close joining when campaign is in status '{campaign.Status}'. Must be '{CampaignStatus.OpenForJoining}'.");
        }

        var acceptedEnrollments = campaign.RestaurantEnrollments
            .Where(e => e.ParticipationStatus == CampaignRestaurantParticipationStatus.Accepted)
            .ToList();

        if (acceptedEnrollments.Count == 0)
        {
            return Error.Validation("Campaign.NoAcceptedOffers",
                "Cannot close joining without at least one accepted restaurant offer.");
        }

        var actorId = _currentUser.UserId?.ToString() ?? "system";
        var reason = request.Reason ?? "Joining closed and accepted offers snapshotted.";

        campaign.TransitionTo(CampaignStatus.JoiningClosed, actorId, reason);

        // Participants are now fixed: capture this as a new snapshot version for the admin approval.
        campaign.BumpSnapshotVersion();

        var snapshotOffers = new List<AcceptedRestaurantOffer>();
        foreach (var enrollment in acceptedEnrollments)
        {
            enrollment.ParticipationStatus = CampaignRestaurantParticipationStatus.Locked;
            enrollment.AcceptedSnapshotVersion ??= campaign.CurrentSnapshotVersion;
            snapshotOffers.Add(new AcceptedRestaurantOffer(
                enrollment.RestaurantId,
                enrollment.DedicatedDailyCapacity ?? 0));
        }

        var snapshot = new CampaignOfferSnapshot(
            campaign.Id,
            OffersVersion: campaign.CurrentSnapshotVersion,
            CapturedAtUtc: DateTime.UtcNow,
            AcceptedOffers: snapshotOffers);

        var audit = new CampaignAuditLog
        {
            CampaignId = campaign.Id,
            ActorId = actorId,
            Action = "JoiningClosed:OffersSnapshotted",
            BeforeStateJson = JsonSerializer.Serialize(new { Status = CampaignStatus.OpenForJoining.ToString() }),
            AfterStateJson = JsonSerializer.Serialize(new { Status = CampaignStatus.JoiningClosed.ToString() }),
            DetailsJson = JsonSerializer.Serialize(snapshot),
            JustificationReason = reason,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.CampaignAuditLogs.Add(audit);
        await _context.SaveChangesAsync(cancellationToken);

        return snapshot;
    }
}
