using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CampaignStatusChangeResultDto(
    Guid CampaignId,
    string PreviousStatus,
    string NewStatus,
    string Reason);

public sealed record StopCampaignCommand(
    Guid CampaignId,
    string Reason,
    string StoppedByUserId) : ICommand<CampaignStatusChangeResultDto>;

public sealed class StopCampaignCommandHandler
    : ICommandHandler<StopCampaignCommand, CampaignStatusChangeResultDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public StopCampaignCommandHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CampaignStatusChangeResultDto>> Handle(
        StopCampaignCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FindAsync(new object[] { request.CampaignId }, cancellationToken);
        if (campaign == null)
        {
            return Result.Failure<CampaignStatusChangeResultDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign '{request.CampaignId}' was not found."));
        }

        var canTransition = campaign.CanTransitionTo(CampaignStatus.Stopped);
        if (canTransition.IsFailure)
        {
            return Result.Failure<CampaignStatusChangeResultDto>(
                Error.Conflict("Campaign.InvalidStateTransition", canTransition.Error ?? "Cannot stop campaign."));
        }

        var oldStatus = campaign.Status;
        campaign.TransitionTo(CampaignStatus.Stopped, request.StoppedByUserId, request.Reason);

        _db.CampaignAuditLogs.Add(new CampaignAuditLog
        {
            CampaignId = campaign.Id,
            ActorId = request.StoppedByUserId,
            Action = "CampaignStopped",
            JustificationReason = request.Reason,
            DetailsJson = $"{{\"PreviousStatus\":\"{oldStatus}\",\"NewStatus\":\"{CampaignStatus.Stopped}\"}}",
            CreatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CampaignStatusChangeResultDto(
            campaign.Id,
            oldStatus.ToString(),
            campaign.Status.ToString(),
            request.Reason));
    }
}

public sealed record EndCampaignCommand(
    Guid CampaignId,
    string Reason,
    string EndedByUserId) : ICommand<CampaignStatusChangeResultDto>;

public sealed class EndCampaignCommandHandler
    : ICommandHandler<EndCampaignCommand, CampaignStatusChangeResultDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EndCampaignCommandHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CampaignStatusChangeResultDto>> Handle(
        EndCampaignCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FindAsync(new object[] { request.CampaignId }, cancellationToken);
        if (campaign == null)
        {
            return Result.Failure<CampaignStatusChangeResultDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign '{request.CampaignId}' was not found."));
        }

        var canTransition = campaign.CanTransitionTo(CampaignStatus.Ended);
        if (canTransition.IsFailure)
        {
            return Result.Failure<CampaignStatusChangeResultDto>(
                Error.Conflict("Campaign.InvalidStateTransition", canTransition.Error ?? "Cannot end campaign."));
        }

        var oldStatus = campaign.Status;
        campaign.TransitionTo(CampaignStatus.Ended, request.EndedByUserId, request.Reason);

        _db.CampaignAuditLogs.Add(new CampaignAuditLog
        {
            CampaignId = campaign.Id,
            ActorId = request.EndedByUserId,
            Action = "CampaignEnded",
            JustificationReason = request.Reason,
            DetailsJson = $"{{\"PreviousStatus\":\"{oldStatus}\",\"NewStatus\":\"{CampaignStatus.Ended}\"}}",
            CreatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CampaignStatusChangeResultDto(
            campaign.Id,
            oldStatus.ToString(),
            campaign.Status.ToString(),
            request.Reason));
    }
}
