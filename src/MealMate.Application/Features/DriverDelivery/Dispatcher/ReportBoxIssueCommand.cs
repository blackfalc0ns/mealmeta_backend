using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record ReportBoxIssueCommand(
    Guid BoxId,
    string IssueType,
    string Description,
    string? Severity = "Medium") : ICommand<ReportBoxIssueResponseDto>;

public sealed class ReportBoxIssueCommandHandler : ICommandHandler<ReportBoxIssueCommand, ReportBoxIssueResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IAuditLogger _audit;

    public ReportBoxIssueCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IAuditLogger audit)
    {
        _db = db;
        _messages = messages;
        _audit = audit;
    }

    public async Task<Result<ReportBoxIssueResponseDto>> Handle(
        ReportBoxIssueCommand request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        if (string.IsNullOrWhiteSpace(request.IssueType))
        {
            return Error.Validation(
                MessageCodes.Validation.Required,
                _messages.GetMessage(MessageCodes.Validation.Required, "Issue type is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Error.Validation(
                MessageCodes.Validation.Required,
                _messages.GetMessage(MessageCodes.Validation.Required, "Issue description is required."));
        }

        // 1. Resolve TripStop or SubscriptionDay
        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId || s.SubscriptionDayId == request.BoxId, cancellationToken);

        if (stop is null)
        {
            var subDay = await _db.SubscriptionDays
                .FirstOrDefaultAsync(sd => sd.Id == request.BoxId, cancellationToken);

            if (subDay is null)
            {
                return Error.NotFound(
                    MessageCodes.DispatcherOrders.BoxNotFound,
                    _messages.GetMessage(MessageCodes.DispatcherOrders.BoxNotFound, "Box was not found."));
            }
        }
        else
        {
            stop.FailureReason = $"{request.IssueType}: {request.Description}";
        }

        // 2. Audit log
        var issueId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await _audit.LogAsync(new AuditEntry(
            Action: "BoxIssueReported",
            EntityType: nameof(TripStop),
            EntityId: request.BoxId,
            Before: null,
            After: new
            {
                IssueId = issueId,
                request.BoxId,
                request.IssueType,
                request.Description,
                Severity = request.Severity ?? "Medium"
            },
            Reason: request.IssueType,
            SourceChannel: "DispatcherDashboard"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(
            MessageCodes.DispatcherBoxTracking.IssueReportedSuccess,
            isAr ? "تم الإبلاغ عن المشكلة بنجاح" : "Box issue reported successfully");

        return Result.Success(new ReportBoxIssueResponseDto(
            IssueId: issueId,
            BoxId: request.BoxId,
            ReportedAtUtc: now,
            Message: successMessage));
    }
}
