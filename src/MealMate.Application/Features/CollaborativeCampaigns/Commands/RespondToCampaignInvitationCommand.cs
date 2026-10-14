using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record RespondToCampaignInvitationCommand(
    Guid EnrollmentId,
    bool Accept,
    int? DedicatedDailyCapacity = null,
    string? DeclineReason = null) : ICommand<CampaignEnrollmentDto>;

public sealed class RespondToCampaignInvitationCommandHandler : ICommandHandler<RespondToCampaignInvitationCommand, CampaignEnrollmentDto>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;

    public RespondToCampaignInvitationCommandHandler(IAppDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<CampaignEnrollmentDto>> Handle(
        RespondToCampaignInvitationCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        var enrollment = await _context.CampaignRestaurantEnrollments
            .Include(e => e.Campaign)
            .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId, cancellationToken);

        if (enrollment == null)
        {
            return Error.NotFound("Campaign.EnrollmentNotFound", $"Enrollment '{request.EnrollmentId}' was not found.");
        }

        // Ownership: the enrollment must belong to the restaurant in the caller's trusted identity.
        if (!_currentUser.RestaurantId.HasValue || enrollment.RestaurantId != _currentUser.RestaurantId.Value)
        {
            return Error.Forbidden(MessageCodes.Campaign.EnrollmentOwnershipMismatch,
                "This campaign invitation does not belong to your restaurant.");
        }

        // A stored response is final; re-responding never overwrites the recorded decision.
        if (enrollment.ParticipationStatus != CampaignRestaurantParticipationStatus.Invited ||
            enrollment.RespondedAtUtc.HasValue)
        {
            return Error.Conflict(MessageCodes.Campaign.AlreadyResponded,
                "An invitation response has already been recorded for this campaign enrollment.");
        }

        if (enrollment.Campaign.Status != CampaignStatus.OpenForJoining)
        {
            return Error.Validation("Campaign.JoiningClosed", $"Cannot respond to invitation because campaign joining is closed (Campaign Status: '{enrollment.Campaign.Status}').");
        }

        if (request.Accept)
        {
            if (!request.DedicatedDailyCapacity.HasValue || request.DedicatedDailyCapacity.Value <= 0)
            {
                return Error.Validation("Campaign.CapacityRequired", "DedicatedDailyCapacity is required and must be greater than zero when accepting an invitation.");
            }

            enrollment.ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted;
            enrollment.DedicatedDailyCapacity = request.DedicatedDailyCapacity.Value;
            enrollment.DeclineReason = null;
            enrollment.AcceptedSnapshotVersion = enrollment.Campaign.CurrentSnapshotVersion;
            enrollment.RespondedAtUtc = DateTime.UtcNow;
            enrollment.ApprovedAtUtc = DateTime.UtcNow;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.DeclineReason))
            {
                return Error.Validation("Campaign.DeclineReasonRequired", "DeclineReason is required when declining an invitation.");
            }

            enrollment.ParticipationStatus = CampaignRestaurantParticipationStatus.Declined;
            enrollment.DeclineReason = request.DeclineReason.Trim();
            enrollment.DedicatedDailyCapacity = null;
            enrollment.RespondedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var dto = new CampaignEnrollmentDto(
            enrollment.Id,
            enrollment.CampaignId,
            enrollment.RestaurantId,
            enrollment.ParticipationStatus,
            enrollment.RequestedAtUtc,
            enrollment.DedicatedDailyCapacity,
            enrollment.DeclineReason);

        return dto;
    }
}
