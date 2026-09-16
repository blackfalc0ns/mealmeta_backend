using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record InviteRestaurantToCampaignCommand(
    Guid CampaignId,
    Guid RestaurantId) : ICommand<CampaignEnrollmentDto>;

public sealed class InviteRestaurantToCampaignCommandHandler : ICommandHandler<InviteRestaurantToCampaignCommand, CampaignEnrollmentDto>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;

    public InviteRestaurantToCampaignCommandHandler(IAppDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<CampaignEnrollmentDto>> Handle(
        InviteRestaurantToCampaignCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        var campaign = await _context.Campaigns
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found.");
        }

        if (campaign.Status is not (CampaignStatus.Draft or CampaignStatus.OpenForJoining))
        {
            return Error.Validation("Campaign.InvalidStatusForInvitation", $"Cannot invite restaurants when campaign is in '{campaign.Status}' status.");
        }

        var alreadyInvited = await _context.CampaignRestaurantEnrollments
            .AnyAsync(e => e.CampaignId == request.CampaignId && e.RestaurantId == request.RestaurantId, cancellationToken);

        if (alreadyInvited)
        {
            return Error.Conflict("Campaign.AlreadyInvited", $"Restaurant '{request.RestaurantId}' is already invited to campaign '{request.CampaignId}'.");
        }

        var enrollment = new CampaignRestaurantEnrollment
        {
            CampaignId = campaign.Id,
            RestaurantId = request.RestaurantId,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Invited,
            RequestedAtUtc = DateTime.UtcNow
        };

        _context.CampaignRestaurantEnrollments.Add(enrollment);
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
