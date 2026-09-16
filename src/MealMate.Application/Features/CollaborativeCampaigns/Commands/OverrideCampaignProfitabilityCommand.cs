using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Events;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record OverrideCampaignProfitabilityCommand(
    Guid CampaignId,
    string JustificationReason) : ICommand;

public sealed class OverrideCampaignProfitabilityCommandHandler : ICommandHandler<OverrideCampaignProfitabilityCommand>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;

    public OverrideCampaignProfitabilityCommandHandler(IAppDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        OverrideCampaignProfitabilityCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        if (string.IsNullOrWhiteSpace(request.JustificationReason) || request.JustificationReason.Trim().Length < 20)
        {
            return Error.Validation("Campaign.JustificationTooShort", "Mandatory justification must be at least 20 characters.");
        }

        var snapshot = await _context.CampaignProfitabilitySnapshots
            .OrderByDescending(s => s.EvaluatedAtUtc)
            .FirstOrDefaultAsync(s => s.CampaignId == request.CampaignId, cancellationToken);

        if (snapshot == null)
        {
            return Error.NotFound("Campaign.ProfitabilitySnapshotNotFound", $"Profitability snapshot for campaign '{request.CampaignId}' was not found.");
        }

        var justification = request.JustificationReason.Trim();
        var approverId = _currentUser.UserId ?? Guid.Empty;

        snapshot.RequiresOverride = false;
        snapshot.IsViable = true;
        snapshot.OverrideReason = justification;
        snapshot.OverrideApprovedByUserId = approverId;
        snapshot.OverrideApprovedAtUtc = DateTime.UtcNow;

        var audit = new CampaignAuditLog
        {
            CampaignId = snapshot.CampaignId,
            ActorId = approverId.ToString(),
            Action = "ProfitabilityOverridden",
            DetailsJson = JsonSerializer.Serialize(new
            {
                snapshot.OverallExpectedMarginPct,
                snapshot.OverallWorstCaseMarginPct,
                Justification = justification
            }),
            JustificationReason = justification,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.CampaignAuditLogs.Add(audit);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
