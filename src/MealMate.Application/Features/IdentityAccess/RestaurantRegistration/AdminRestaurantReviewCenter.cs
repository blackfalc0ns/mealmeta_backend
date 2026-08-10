using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record AdminRestaurantReviewListQuery(
    string? Section = null,
    VerificationStatus? Status = null,
    Guid? RestaurantId = null,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<RestaurantReviewItemDto>>;

public sealed record AdminReviewItemDecisionCommand(
    Guid ReviewItemId,
    string Action,
    string? Reason) : ICommand<RestaurantReviewItemDto>;

public sealed class AdminRestaurantReviewHandlers :
    IQueryHandler<AdminRestaurantReviewListQuery, PagedResult<RestaurantReviewItemDto>>,
    ICommandHandler<AdminReviewItemDecisionCommand, RestaurantReviewItemDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly IDateTimeProvider _clock;

    public AdminRestaurantReviewHandlers(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, IRegistrationReviewNotifier notifier, IDateTimeProvider clock)
    {
        _db = db; _currentUser = currentUser; _audit = audit; _notifier = notifier; _clock = clock;
    }

    public async Task<Result<PagedResult<RestaurantReviewItemDto>>> Handle(AdminRestaurantReviewListQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.HasPermission(PermissionCodes.RestaurantRegView))
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        var query = _db.RestaurantReviewItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Section)) query = query.Where(x => x.Section == request.Section);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.RestaurantId is { } restaurantId) query = query.Where(x => x.RestaurantId == restaurantId);
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.SubmittedAtUtc ?? x.CreatedAtUtc).Skip(page.Skip).Take(page.NormalizedPageSize)
            .Select(x => new RestaurantReviewItemDto(x.Id, x.Section, x.SubjectType, x.SubjectId, x.FieldKey, x.Status, x.Reason, x.ReviewedAtUtc, x.SubmittedAtUtc)).ToListAsync(cancellationToken);
        return new PagedResult<RestaurantReviewItemDto>(items, total, page.NormalizedPage, page.NormalizedPageSize);
    }

    public async Task<Result<RestaurantReviewItemDto>> Handle(AdminReviewItemDecisionCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        var action = request.Action.Trim().ToLowerInvariant();
        if (action is not ("approve" or "request-changes"))
            return Error.Validation(MessageCodes.Validation.Unknown, "Action must be approve or request-changes.");
        var reason = request.Reason?.Trim();
        if (action == "request-changes" && string.IsNullOrWhiteSpace(reason))
            return Error.Validation(MessageCodes.Validation.Required, "A reason is required.");

        var item = await _db.RestaurantReviewItems.FirstOrDefaultAsync(x => x.Id == request.ReviewItemId, cancellationToken);
        if (item is null) return Error.NotFound(MessageCodes.Common.NotFound, "Review item not found.");
        if (item.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Review item is not reviewable.");
        var old = item.Status;
        item.Status = action == "approve" ? VerificationStatus.Approved : VerificationStatus.NeedsChanges;
        item.Reason = action == "approve" ? null : reason;
        item.ReviewedByUserId = _currentUser.UserId;
        item.ReviewedAtUtc = _clock.UtcNow;
        _db.RestaurantReviewHistories.Add(new RestaurantReviewHistory
        {
            ReviewItemId = item.Id, FromStatus = old, ToStatus = item.Status, Reason = item.Reason,
            ActorUserId = _currentUser.UserId, CreatedAtUtc = _clock.UtcNow,
        });
        var registration = item.RegistrationId is { } registrationId
            ? await _db.RestaurantRegistrations
                .Include(x => x.FieldReviews)
                .FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken)
            : null;

        // Keep the unified review item and the legacy registration field review
        // in sync. The onboarding editor uses the latter to decide whether a
        // field may be changed; without this bridge, rejecting a restaurant name
        // in Review Center leaves the registration as Submitted and every edit
        // is rejected by the registration update handler.
        if (registration is not null
            && string.Equals(item.SubjectType, "field", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.FieldKey)
            && RegistrationFieldKeys.IsValid(item.FieldKey))
        {
            var fieldReview = registration.FieldReviews.FirstOrDefault(x => x.FieldKey == item.FieldKey);
            if (fieldReview is null)
            {
                fieldReview = new RestaurantRegistrationFieldReview
                {
                    RegistrationId = registration.Id,
                    FieldKey = item.FieldKey,
                    CreatedAtUtc = _clock.UtcNow,
                };
                _db.RestaurantRegistrationFieldReviews.Add(fieldReview);
            }

            fieldReview.Status = item.Status;
            fieldReview.Reason = item.Reason;
            fieldReview.ReviewedByUserId = item.ReviewedByUserId;
            fieldReview.ReviewedAtUtc = item.ReviewedAtUtc;

            if (item.Status == VerificationStatus.NeedsChanges
                && registration.Status == VerificationStatus.Submitted)
            {
                registration.Status = VerificationStatus.UnderReview;
            }
        }

        if (registration?.SubmittedByUserId is { } userId)
            await _notifier.NotifyRegistrationUpdated(userId, registration.Id, action == "approve" ? "review-approved" : "review-changes-requested", cancellationToken);
        await _audit.LogAsync(new AuditEntry("RestaurantReviewItemDecision", nameof(RestaurantReviewItem), item.Id, After: new { item.Status, item.Reason }, Reason: item.Reason, SourceChannel: "admin"), cancellationToken);
        return new RestaurantReviewItemDto(item.Id, item.Section, item.SubjectType, item.SubjectId, item.FieldKey, item.Status, item.Reason, item.ReviewedAtUtc, item.SubmittedAtUtc);
    }
}
