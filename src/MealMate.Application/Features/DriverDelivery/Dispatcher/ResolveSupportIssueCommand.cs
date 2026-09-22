using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record ResolveSupportIssueCommand(
    Guid IssueId,
    string? ResolutionNotes = null) : ICommand<ResolveSupportIssueResponseDto>;

public sealed class ResolveSupportIssueCommandHandler : ICommandHandler<ResolveSupportIssueCommand, ResolveSupportIssueResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IAuditLogger _audit;

    public ResolveSupportIssueCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IAuditLogger audit)
    {
        _db = db;
        _messages = messages;
        _audit = audit;
    }

    public async Task<Result<ResolveSupportIssueResponseDto>> Handle(
        ResolveSupportIssueCommand request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        var complaint = await _db.Complaints
            .FirstOrDefaultAsync(c => c.Id == request.IssueId, cancellationToken);

        if (complaint != null)
        {
            complaint.Status = ComplaintStatus.Resolved;
        }
        else
        {
            var stop = await _db.TripStops
                .FirstOrDefaultAsync(s => s.Id == request.IssueId, cancellationToken);

            if (stop is null)
            {
                return Error.NotFound(
                    MessageCodes.DispatcherSupport.IssueNotFound,
                    _messages.GetMessage(MessageCodes.DispatcherSupport.IssueNotFound, "Support issue was not found."));
            }

            stop.FailureReason = null;
        }

        var now = DateTime.UtcNow;

        await _audit.LogAsync(new AuditEntry(
            Action: "SupportIssueResolved",
            EntityType: nameof(Complaint),
            EntityId: request.IssueId,
            Before: null,
            After: new
            {
                request.IssueId,
                request.ResolutionNotes,
                ResolvedAtUtc = now
            },
            Reason: request.ResolutionNotes ?? "Manual resolution by dispatcher",
            SourceChannel: "DispatcherDashboard"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(
            MessageCodes.DispatcherSupport.ResolveSuccess,
            isAr ? "تم حل المشكلة بنجاح" : "Issue resolved successfully");

        return Result.Success(new ResolveSupportIssueResponseDto(
            IssueId: request.IssueId,
            ResolvedAtUtc: now,
            Message: successMessage));
    }
}
