using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed record GetRestaurantDriverRegistrationsQuery(
    Guid? RestaurantId = null,
    VerificationStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<DriverRegistrationDto>>;

public sealed class GetRestaurantDriverRegistrationsQueryHandler
    : IQueryHandler<GetRestaurantDriverRegistrationsQuery, PagedResult<DriverRegistrationDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantDriverRegistrationsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<DriverRegistrationDto>>> Handle(
        GetRestaurantDriverRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var targetRestaurantId = _currentUser.RestaurantId ?? request.RestaurantId;
        if (!targetRestaurantId.HasValue || targetRestaurantId.Value == Guid.Empty)
        {
            return Error.Validation(MessageCodes.Validation.Required, "Restaurant ID could not be identified.");
        }

        var query = _db.DriverRegistrations.AsNoTracking()
            .Where(r => r.RestaurantId == targetRestaurantId.Value);

        if (request.Status.HasValue)
        {
            query = query.Where(r => r.Status == request.Status.Value);
        }

        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<DriverRegistrationDto>(
            items.Select(DriverRegistrationMapping.Map).ToList(),
            total,
            page.NormalizedPage,
            page.NormalizedPageSize);
    }
}

public sealed record RestaurantDriverDecisionCommand(
    Guid Id,
    string Action,
    string Reason) : ICommand<DriverRegistrationDto>;

public sealed class RestaurantDriverDecisionCommandValidator : AbstractValidator<RestaurantDriverDecisionCommand>
{
    public RestaurantDriverDecisionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(a => a.Equals("approve", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("reject", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("request-changes", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Action must be approve, reject, or request-changes.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RestaurantDriverDecisionCommandHandler
    : ICommandHandler<RestaurantDriverDecisionCommand, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IDriverRegistrationNotificationService _driverNotifier;

    public RestaurantDriverDecisionCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IDriverRegistrationNotificationService? driverNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _driverNotifier = driverNotifier ?? NullDriverRegistrationNotificationService.Instance;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        RestaurantDriverDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        var currentRestId = _currentUser.RestaurantId;
        if (currentRestId.HasValue && currentRestId.Value != Guid.Empty && registration.RestaurantId != currentRestId.Value)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "You cannot review registrations for another restaurant.");
        }

        var restaurantName = "المطعم";
        if (registration.RestaurantId.HasValue)
        {
            var restProfile = await _db.RestaurantProfiles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == registration.RestaurantId.Value, cancellationToken);
            if (restProfile is not null)
            {
                restaurantName = restProfile.TradeNameAr ?? restProfile.TradeNameEn ?? restProfile.LegalNameAr ?? "المطعم";
            }
        }

        switch (request.Action.ToLowerInvariant())
        {
            case "approve":
                registration.RestaurantApprovalStatus = VerificationStatus.Approved;
                registration.RestaurantReviewedByUserId = _currentUser.UserId;
                registration.RestaurantReviewedAtUtc = _clock.UtcNow;
                registration.RestaurantReviewNotes = request.Reason;
                // Move to UnderReview waiting for admin final review
                registration.Status = VerificationStatus.UnderReview;
                await _audit.LogAsync(new AuditEntry("RestaurantDriverApproved", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "restaurant"), cancellationToken);

                await _driverNotifier.NotifyAdminsDriverApplicationReadyForConfirmationAsync(
                    registration.Id,
                    registration.RestaurantId ?? Guid.Empty,
                    restaurantName,
                    registration.FullNameAr,
                    registration.Phone,
                    cancellationToken);

                await _driverNotifier.NotifyDriverRestaurantApprovedAsync(
                    registration.Id,
                    registration.SubmittedByUserId,
                    registration.Phone,
                    registration.FullNameAr,
                    restaurantName,
                    cancellationToken);
                break;

            case "reject":
                registration.RestaurantApprovalStatus = VerificationStatus.Rejected;
                registration.RestaurantReviewedByUserId = _currentUser.UserId;
                registration.RestaurantReviewedAtUtc = _clock.UtcNow;
                registration.RestaurantReviewNotes = request.Reason;
                registration.Status = VerificationStatus.Rejected;
                registration.RejectionReason = request.Reason;
                await _audit.LogAsync(new AuditEntry("RestaurantDriverRejected", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "restaurant"), cancellationToken);

                await _driverNotifier.NotifyDriverRestaurantRejectedAsync(
                    registration.Id,
                    registration.SubmittedByUserId,
                    registration.Phone,
                    registration.FullNameAr,
                    restaurantName,
                    request.Reason,
                    cancellationToken);
                break;

            case "request-changes":
            case "request_changes":
                registration.RestaurantApprovalStatus = VerificationStatus.NeedsChanges;
                registration.RestaurantReviewedByUserId = _currentUser.UserId;
                registration.RestaurantReviewedAtUtc = _clock.UtcNow;
                registration.RestaurantReviewNotes = request.Reason;
                registration.Status = VerificationStatus.NeedsChanges;
                registration.ChangeRequestNotes = request.Reason;
                await _audit.LogAsync(new AuditEntry("RestaurantDriverChangesRequested", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "restaurant"), cancellationToken);

                await _driverNotifier.NotifyDriverRestaurantChangesRequestedAsync(
                    registration.Id,
                    registration.SubmittedByUserId,
                    registration.Phone,
                    registration.FullNameAr,
                    restaurantName,
                    request.Reason,
                    cancellationToken);
                break;

            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        return DriverRegistrationMapping.Map(registration);
    }
}
