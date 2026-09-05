using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;

public sealed record DeliveryManagerRegistrationDetailDto(
    DeliveryManagerRegistrationDto Registration,
    IReadOnlyList<DocumentDto> Documents);

public sealed record ListDeliveryManagerRegistrationsQuery(
    VerificationStatus? Status = null,
    Guid? RestaurantId = null) : IQuery<IReadOnlyList<DeliveryManagerRegistrationDto>>;

public sealed class ListDeliveryManagerRegistrationsQueryHandler
    : IQueryHandler<ListDeliveryManagerRegistrationsQuery, IReadOnlyList<DeliveryManagerRegistrationDto>>
{
    private readonly IAppDbContext _db;

    public ListDeliveryManagerRegistrationsQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<DeliveryManagerRegistrationDto>>> Handle(
        ListDeliveryManagerRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.DeliveryManagerRegistrations.AsNoTracking();

        if (request.Status.HasValue)
        {
            query = query.Where(r => r.Status == request.Status.Value);
        }

        if (request.RestaurantId.HasValue)
        {
            query = query.Where(r => r.RestaurantId == request.RestaurantId.Value);
        }

        var list = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken);

        var dtos = list.Select(r => new DeliveryManagerRegistrationDto(
            r.Id,
            r.RestaurantId,
            r.FullName,
            r.Phone,
            r.Email,
            r.NationalId,
            r.NationalIdExpiryUtc,
            r.ContractExpiryUtc,
            r.CoverageType,
            r.CoverageScopeId,
            r.CoverageScopeName,
            r.Status,
            r.CreatedAtUtc,
            r.RejectionReason,
            r.ChangeRequestNotes,
            r.ResultingUserId)).ToList();

        return dtos;
    }
}

public sealed record GetDeliveryManagerRegistrationByIdQuery(Guid Id) : IQuery<DeliveryManagerRegistrationDetailDto>;

public sealed class GetDeliveryManagerRegistrationByIdQueryHandler
    : IQueryHandler<GetDeliveryManagerRegistrationByIdQuery, DeliveryManagerRegistrationDetailDto>
{
    private readonly IAppDbContext _db;

    public GetDeliveryManagerRegistrationByIdQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<DeliveryManagerRegistrationDetailDto>> Handle(
        GetDeliveryManagerRegistrationByIdQuery request,
        CancellationToken cancellationToken)
    {
        var r = await _db.DeliveryManagerRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id || x.ResultingUserId == request.Id, cancellationToken);

        if (r is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Delivery manager registration not found.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .AsNoTracking()
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == r.Id)
            .OrderBy(d => d.DocumentType)
            .ToListAsync(cancellationToken);

        var docDtos = docs.Select(document =>
        {
            var latest = document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            return new DocumentDto(
                document.Id,
                document.OwnerType,
                document.OwnerId,
                document.DocumentType,
                document.FileName,
                document.ContentType,
                document.SizeBytes,
                latest?.Status,
                document.ExpiresAtUtc,
                latest?.Reason);
        }).ToList();

        var regDto = new DeliveryManagerRegistrationDto(
            r.Id,
            r.RestaurantId,
            r.FullName,
            r.Phone,
            r.Email,
            r.NationalId,
            r.NationalIdExpiryUtc,
            r.ContractExpiryUtc,
            r.CoverageType,
            r.CoverageScopeId,
            r.CoverageScopeName,
            r.Status,
            r.CreatedAtUtc,
            r.RejectionReason,
            r.ChangeRequestNotes,
            r.ResultingUserId);

        return new DeliveryManagerRegistrationDetailDto(regDto, docDtos);
    }
}

public sealed record AdminDeliveryManagerDecisionCommand(
    Guid Id,
    string Action,
    string Reason) : ICommand<DeliveryManagerRegistrationDto>;

public sealed class AdminDeliveryManagerDecisionCommandValidator : AbstractValidator<AdminDeliveryManagerDecisionCommand>
{
    public AdminDeliveryManagerDecisionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Action).NotEmpty().Must(a =>
            a.Equals("approve", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("reject", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("request-changes", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public sealed class AdminDeliveryManagerDecisionCommandHandler
    : ICommandHandler<AdminDeliveryManagerDecisionCommand, DeliveryManagerRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public AdminDeliveryManagerDecisionCommandHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DeliveryManagerRegistrationDto>> Handle(
        AdminDeliveryManagerDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DeliveryManagerRegistrations
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Delivery manager registration not found.");
        }

        var action = request.Action.ToLowerInvariant();
        var reviewerUserId = _currentUser.UserId;
        var now = _clock.UtcNow;

        var docs = await _db.Documents
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        var verificationStatus = action switch
        {
            "approve" => VerificationStatus.Approved,
            "reject" => VerificationStatus.Rejected,
            _ => VerificationStatus.ResubmissionRequired
        };

        var reasonOrNotes = action == "approve" ? null : request.Reason;

        foreach (var doc in docs)
        {
            _db.DocumentVerifications.Add(new DocumentVerification
            {
                DocumentId = doc.Id,
                Status = verificationStatus,
                ReviewedByUserId = reviewerUserId,
                ReviewedAtUtc = now,
                Reason = reasonOrNotes,
                CreatedAtUtc = now
            });
        }

        switch (action)
        {
            case "approve":
                registration.Status = VerificationStatus.Approved;
                registration.ReviewedAtUtc = now;
                registration.ReviewedByUserId = reviewerUserId;
                registration.RejectionReason = null;
                registration.ChangeRequestNotes = null;

                if (registration.ResultingUserId.HasValue)
                {
                    await _users.UpdateAccountStatusAsync(registration.ResultingUserId.Value, AccountStatus.Active, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "DeliveryManagerApprovedByAdmin",
                    "DeliveryManagerRegistration",
                    registration.Id,
                    After: new { registration.FullName, Status = VerificationStatus.Approved },
                    SourceChannel: "admin-portal"), cancellationToken);
                break;

            case "reject":
                registration.Status = VerificationStatus.Rejected;
                registration.ReviewedAtUtc = now;
                registration.ReviewedByUserId = reviewerUserId;
                registration.RejectionReason = request.Reason;

                if (registration.ResultingUserId.HasValue)
                {
                    await _users.UpdateAccountStatusAsync(registration.ResultingUserId.Value, AccountStatus.Suspended, cancellationToken);
                }

                await _audit.LogAsync(new AuditEntry(
                    "DeliveryManagerRejectedByAdmin",
                    "DeliveryManagerRegistration",
                    registration.Id,
                    After: new { registration.FullName, Status = VerificationStatus.Rejected, Reason = request.Reason },
                    SourceChannel: "admin-portal"), cancellationToken);
                break;

            case "request-changes":
                registration.Status = VerificationStatus.ResubmissionRequired;
                registration.ReviewedAtUtc = now;
                registration.ReviewedByUserId = reviewerUserId;
                registration.ChangeRequestNotes = request.Reason;

                await _audit.LogAsync(new AuditEntry(
                    "DeliveryManagerChangesRequestedByAdmin",
                    "DeliveryManagerRegistration",
                    registration.Id,
                    After: new { registration.FullName, Status = VerificationStatus.ResubmissionRequired, Notes = request.Reason },
                    SourceChannel: "admin-portal"), cancellationToken);
                break;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new DeliveryManagerRegistrationDto(
            registration.Id,
            registration.RestaurantId,
            registration.FullName,
            registration.Phone,
            registration.Email,
            registration.NationalId,
            registration.NationalIdExpiryUtc,
            registration.ContractExpiryUtc,
            registration.CoverageType,
            registration.CoverageScopeId,
            registration.CoverageScopeName,
            registration.Status,
            registration.CreatedAtUtc,
            registration.RejectionReason,
            registration.ChangeRequestNotes,
            registration.ResultingUserId);
    }
}
