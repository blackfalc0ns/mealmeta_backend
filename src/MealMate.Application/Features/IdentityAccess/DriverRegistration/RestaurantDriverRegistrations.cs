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
using System.Text.Json;
using OutboxMessage = MealMate.Domain.Common.OutboxMessage;
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
        var isAdmin = _currentUser.Roles.Any(AppRoles.IsAdminRole) || _currentUser.HasPermission(PermissionCodes.DriverRegView);
        Guid targetRestaurantId;

        if (isAdmin)
        {
            if (request.RestaurantId.HasValue && request.RestaurantId.Value != Guid.Empty)
            {
                targetRestaurantId = request.RestaurantId.Value;
            }
            else if (_currentUser.RestaurantId.HasValue && _currentUser.RestaurantId.Value != Guid.Empty)
            {
                targetRestaurantId = _currentUser.RestaurantId.Value;
            }
            else
            {
                return Error.Validation(MessageCodes.Validation.Required, "Restaurant ID could not be identified.");
            }
        }
        else
        {
            if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "Restaurant scope is required to view driver registrations.");
            }

            if (request.RestaurantId.HasValue && request.RestaurantId.Value != _currentUser.RestaurantId.Value)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "You cannot access registrations for another restaurant.");
            }

            targetRestaurantId = _currentUser.RestaurantId.Value;
        }

        var query = _db.DriverRegistrations.AsNoTracking()
            .Where(r => r.RestaurantId == targetRestaurantId);

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

        var isAdmin = _currentUser.Roles.Any(AppRoles.IsAdminRole) || _currentUser.HasPermission(PermissionCodes.DriverRegApprove);
        if (!isAdmin)
        {
            if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "Restaurant scope is required to review driver registrations.");
            }

            if (registration.RestaurantId != _currentUser.RestaurantId.Value)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "You cannot review registrations for another restaurant.");
            }
        }

        // Terminal state guards
        if (registration.Status == VerificationStatus.Approved || registration.RestaurantApprovalStatus == VerificationStatus.Approved)
        {
            if (request.Action.Equals("approve", StringComparison.OrdinalIgnoreCase))
            {
                return DriverRegistrationMapping.Map(registration);
            }
            return Error.Conflict("ALREADY_APPROVED", "This registration has already been approved by the restaurant.");
        }

        if (registration.Status == VerificationStatus.Rejected || registration.RestaurantApprovalStatus == VerificationStatus.Rejected)
        {
            return Error.Conflict("ALREADY_REJECTED", "This registration has already been rejected.");
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

                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "DriverRegistrationRestaurantApproved",
                    AggregateType = "DriverRegistration",
                    AggregateId = registration.Id.ToString(),
                    DeduplicationKey = $"DriverRegistrationRestaurantApproved_{registration.Id}_{Guid.NewGuid():N}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        DriverUserId = registration.SubmittedByUserId,
                        Phone = registration.Phone,
                        DriverName = registration.FullNameAr,
                        RestaurantName = restaurantName,
                        RestaurantId = registration.RestaurantId,
                        Action = "approve",
                        Reason = request.Reason,
                        ReviewStage = 2,
                        ApplicationStatus = "UnderReview"
                    }),
                    CreatedAtUtc = _clock.UtcNow
                });
                break;

            case "reject":
                registration.RestaurantApprovalStatus = VerificationStatus.Rejected;
                registration.RestaurantReviewedByUserId = _currentUser.UserId;
                registration.RestaurantReviewedAtUtc = _clock.UtcNow;
                registration.RestaurantReviewNotes = request.Reason;
                registration.Status = VerificationStatus.Rejected;
                registration.RejectionReason = request.Reason;
                await _audit.LogAsync(new AuditEntry("RestaurantDriverRejected", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "restaurant"), cancellationToken);

                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "DriverRegistrationRestaurantRejected",
                    AggregateType = "DriverRegistration",
                    AggregateId = registration.Id.ToString(),
                    DeduplicationKey = $"DriverRegistrationRestaurantRejected_{registration.Id}_{Guid.NewGuid():N}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        DriverUserId = registration.SubmittedByUserId,
                        Phone = registration.Phone,
                        DriverName = registration.FullNameAr,
                        RestaurantName = restaurantName,
                        RestaurantId = registration.RestaurantId,
                        Action = "reject",
                        Reason = request.Reason,
                        ReviewStage = 3,
                        ApplicationStatus = "Rejected"
                    }),
                    CreatedAtUtc = _clock.UtcNow
                });
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

                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "DriverRegistrationRestaurantChangesRequested",
                    AggregateType = "DriverRegistration",
                    AggregateId = registration.Id.ToString(),
                    DeduplicationKey = $"DriverRegistrationRestaurantChangesRequested_{registration.Id}_{Guid.NewGuid():N}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        DriverUserId = registration.SubmittedByUserId,
                        Phone = registration.Phone,
                        DriverName = registration.FullNameAr,
                        RestaurantName = restaurantName,
                        RestaurantId = registration.RestaurantId,
                        Action = "request-changes",
                        Reason = request.Reason,
                        ReviewStage = 2,
                        ApplicationStatus = "NeedsChanges"
                    }),
                    CreatedAtUtc = _clock.UtcNow
                });
                break;

            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        registration.ModifiedAtUtc = _clock.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "The registration status was modified by another concurrent operation. Please refresh and try again.");
        }

        return DriverRegistrationMapping.Map(registration);
    }
}
